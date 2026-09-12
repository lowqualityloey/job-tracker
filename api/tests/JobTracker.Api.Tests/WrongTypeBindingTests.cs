using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-069 — **a wrong-typed wire member is a `400` with a `code`, not a `500` with nothing.** Seam Integration, p0.
///
/// ## What `-057` found and deliberately left alone
///
/// Its record closes with: "Wrong-typed *values* in the other members still die in the binder on the same server…
/// `companyName: 42` → **500, no `code`**; `notes: [1,2]` → **500**; `status: 5` → **500**; and the valid control → **201**,
/// so the probe was live." It stayed a proposal because "a sweep across the contract is the human's call to rate, not a
/// change that should ride inside this commit."
///
/// ## The measured cause, which is not a framework limitation
///
/// A live probe against this repository's own dev server captured the exception chain behind every one of those 500s:
///
///     Microsoft.AspNetCore.Http.BadHttpRequestException: Failed to read parameter "NewApplicationRequest request" …
///      ---> System.Text.Json.JsonException: The JSON value could not be converted to JobTracker.Api.NewApplicationRequest
///       ---> System.InvalidOperationException: Cannot get the value of a token type 'Number' as a string.
///         at Microsoft.AspNetCore.Http.RequestDelegateFactory.Log.InvalidJsonRequestBody(...)
///
/// The framework logged it as **`InvalidJsonRequestBody`** and wrapped it in a `BadHttpRequestException` **whose own
/// `StatusCode` property is 400**. So the information needed to answer correctly is in hand two frames before the response
/// is written — and the exception handler throws it away, mapping every unhandled exception to a generic 500. This is not
/// ASP.NET being unhelpful; it is this app's problem pipeline declining to read what it was handed.
///
/// ## Why `code` matters more than the status
///
/// AC-8 and DECISION-m3-backend-api-004 established that every problem this API emits carries a `code` extension, and
/// `-060` built the client's mapping table on exactly that discriminator. A `500` without one is not merely the wrong
/// number: `httpApplicationRepository` classifies an unrecognised document as `corrupt-data`, so the user of a cross-origin
/// client sees "the server sent something we cannot parse" while the server's own log says "invalid JSON request body". Two
/// true statements about the same event that lead debugging in opposite directions — which is the reason the last case here
/// asserts the emitted code against `contracts/problem-codes.json` rather than against a literal in this file.
///
/// The pointer half is the other reason. The binder knows *which member* failed (`JsonException.Path` is `$.companyName`),
/// so the response can name it, and the form can put the message on the field the user mistyped instead of above the form.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WrongTypeBindingTests(PostgresFixture postgres) : IDisposable
{
    private const string Email = "binding-069@example.test";
    private const string Password = "the-binding-seed-password-8e5";
    private const string ValidationType = "https://job-tracker.local/probs/validation";

    private readonly List<IDisposable> _hosts = [];
    private HttpClient? _http;

    private sealed class BindingFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Auth:Bootstrap:Email", Email);
            builder.UseSetting("Auth:Bootstrap:Password", Password);
        }
    }

    /// <summary>One host per class, as in <c>EventStreamScopingTests</c>: nothing here needs two processes, and each extra
    /// host is another <c>Migrate()</c> against the container the whole collection shares.</summary>
    private HttpClient Http
    {
        get
        {
            if (_http is not null)
            {
                return _http;
            }

            var factory = new BindingFactory(postgres.ConnectionString);
            _hosts.Add(factory);
            return _http = factory.CreateClient();
        }
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    private async Task<string> CookieAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                $$"""{ "email": "{{Email}}", "password": "{{Password}}" }""",
                new MediaTypeHeaderValue("application/json")),
        };
        var response = await Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "login issued no cookie");
        return Assert.Single(values.ToList()).Split(';', 2)[0];
    }

    private async Task<(HttpStatusCode Status, JsonElement Doc, string Body)> SendAsync(
        HttpMethod method, string path, string json, string cookie)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
        return (response.StatusCode, doc, body);
    }

    /// <summary>Every failure message in this file carries the whole document. "Expected 400, got 500" would be a diagnosis
    /// withheld from whoever reads the CI log next.</summary>
    private static string Describe((HttpStatusCode Status, JsonElement Doc, string Body) response) =>
        $"status={(int)response.Status} body={response.Body}";

    private static string? CodeOf((HttpStatusCode Status, JsonElement Doc, string Body) response) =>
        response.Doc.ValueKind == JsonValueKind.Object && response.Doc.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    private static List<string> PointersOf((HttpStatusCode Status, JsonElement Doc, string Body) response)
    {
        if (response.Doc.ValueKind != JsonValueKind.Object
            || !response.Doc.TryGetProperty("errors", out var errors)
            || errors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. errors.EnumerateArray()
            .Where(e => e.TryGetProperty("pointer", out var p))
            .Select(e => e.GetProperty("pointer").GetString() ?? "")];
    }

    private static string Body(string id, string member, string wrongValue) =>
        $$"""{"id":"{{id}}","companyName":"Binding Co","jobTitle":"Engineer","status":"Applied","{{member}}":{{wrongValue}}}""";

    private async Task<long> RowCountAsync()
    {
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*)::int from applications";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("companyName", "42")]
    [InlineData("jobTitle", "true")]
    [InlineData("status", "5")]
    [InlineData("notes", @"[1,2]")]
    public async Task A_wrong_typed_member_is_a_400_that_names_the_member(string member, string wrongValue)
    {
        var cookie = await CookieAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/applications",
            Body(Guid.NewGuid().ToString(), member, wrongValue), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.NotNull(CodeOf(response));
        Assert.Equal("validation", CodeOf(response));
        Assert.Equal(ValidationType, response.Doc.GetProperty("type").GetString());

        // The binder reports which path failed ($.companyName), so there is no excuse for a response that makes the user
        // guess which field to fix. Asserted as a set containing the one pointer, so an extra sibling error is allowed and
        // a missing one is not.
        var pointers = PointersOf(response);
        Assert.Contains($"#/{member}", pointers);
        Assert.True(pointers.Count > 0, $"no errors array at all: {Describe(response)}");
    }

    [Fact]
    public async Task A_malformed_body_is_also_rejected_as_a_problem_not_an_unexpected_error()
    {
        // The truncated document is the same class of caller mistake and the log line is the same
        // `InvalidJsonRequestBody`, but there is no member path to name -- so the contract's minimum is a status and a
        // code, and this case pins that the code survives without a pointer rather than only that the status changes.
        var cookie = await CookieAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/applications",
            """{"companyName": "Co", "jobTitle": """, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation", CodeOf(response));
    }

    [Fact]
    public async Task A_refused_create_writes_nothing()
    {
        // A 400 that also wrote the row would be the worse bug, and a status-code test cannot see it: the binder rejects
        // the shape, so the handler never runs -- which is exactly the property worth pinning rather than assuming.
        var cookie = await CookieAsync();
        var before = await RowCountAsync();
        var id = Guid.NewGuid().ToString();

        var response = await SendAsync(HttpMethod.Post, "/api/applications", Body(id, "companyName", "42"), cookie);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);

        Assert.Equal(before, await RowCountAsync());
        await using var conn = new NpgsqlConnection(postgres.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*)::int from applications where id = @id";
        cmd.Parameters.AddWithValue(Guid.Parse(id));
        Assert.Equal(0, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task A_wrong_typed_update_is_refused_and_leaves_the_record_untouched()
    {
        var cookie = await CookieAsync();
        var id = Guid.NewGuid();
        var created = await SendAsync(HttpMethod.Post, "/api/applications",
            $$"""{"id":"{{id}}","companyName":"Original Co","jobTitle":"Engineer","status":"Applied"}""", cookie);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var revision = created.Doc.GetProperty("revision").ToString();

        var update = $$"""{"id":"{{id}}","companyName":42,"jobTitle":"Engineer","status":"Applied"}""";
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/applications/{id}");
        request.Headers.Add("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("If-Match", revision);
        request.Content = new StringContent(update, Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("validation", body, StringComparison.Ordinal);
        Assert.Contains("#/companyName", body, StringComparison.Ordinal);

        var after = await SendAsync(HttpMethod.Get, $"/api/applications/{id}", "{}", cookie);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        Assert.Equal("Original Co", after.Doc.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task A_correctly_typed_body_still_creates()
    {
        // The control, and the only thing standing between "the 500 became a 400" and "the endpoint became a 400 machine".
        var cookie = await CookieAsync();
        var id = Guid.NewGuid();
        var response = await SendAsync(HttpMethod.Post, "/api/applications",
            $$"""{"id":"{{id}}","companyName":"Well Formed Co","jobTitle":"Engineer","status":"Applied"}""", cookie);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal("Well Formed Co", response.Doc.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task The_code_on_a_binding_failure_is_a_code_the_shared_contract_knows_about()
    {
        // contracts/problem-codes.json is the artifact -060 built the client's mapping table from. Asserting against it
        // rather than against a literal here means a fix that invents a fifth code -- "binding-error", say -- fails this
        // test instead of shipping a discriminator the client cannot map, which is the exact shape of the gap DECISION-m4
        // -auth-005 had to close.
        var path = Path.Combine(AppContext.BaseDirectory, "problem-codes.json");
        Assert.True(File.Exists(path), "the shared contract is not in the output directory");
        var codes = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("codes")
            .EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);

        var cookie = await CookieAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/applications",
            Body(Guid.NewGuid().ToString(), "notes", "\"not-an-array\""), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        var code = CodeOf(response);
        Assert.NotNull(code);
        Assert.True(codes.Contains(code),
            $"the binding path emitted code '{code}', which the shared contract ({string.Join(", ", codes)}) does not define: " +
            Describe(response));
    }
}
