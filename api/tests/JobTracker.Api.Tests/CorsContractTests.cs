using System.Net;
using JobTracker.Api.Tests.Infrastructure;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// BEHAVIOR-m3-backend-api-042's server half — **may a browser at another origin use this API at all?**
///
/// Written because the real-browser harness found the hole that no in-process test could see: `WebApplicationFactory`
/// answers a request, but a browser first asks permission, and it was asking and getting nothing. Preflight came back
/// **405 Method Not Allowed** and a cross-origin `GET` came back **without `Access-Control-Allow-Origin`** — while all
/// 60 API tests and all 197 jsdom tests stayed green, because jsdom's `fetch` is a stub that enforces nothing.
///
/// Three decisions this file pins down, each of which was genuinely open:
///
/// 1. **The policy is global, not per-endpoint.** `-046` is the evidence: the SSE endpoint was added to the catalog
///    *after* the CRUD routes existed, and a per-endpoint `.RequireCors(...)` is the kind of thing that gets written
///    once and forgotten on the second. One `UseCors()` covers whatever gets added next.
/// 2. **Origins are an allow-list from configuration; headers and methods are open.** The direction of strictness is
///    deliberate. `If-Match` is a non-simple request header, so the optimistic-write path cannot work cross-origin
///    unless preflight permits it — and AC-7's retry-idempotency will want another custom header again. A hand-kept
///    header whitelist is a trap that fails on the *next* feature, silently, in the browser only. Being permissive
///    about headers costs nothing while origins stay strict, because the origin is the actual security boundary a
///    browser enforces; the header list is not a defence, it's a compatibility surface.
/// 3. **No configuration means no cross-origin access, not `*`.** Failing closed is the only sane default for a
///    forgotten key, and `Allow-Credentials` combined with a wildcard origin is rejected outright by browsers, so a
///    wildcard would be both unsafe and, the moment auth arrives in M4, broken.
/// </summary>
public sealed class CorsContractTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    private readonly HttpClient _http = fixture.Http;

    // A deadline, because the stream endpoint never completes on its own — and `-046` needed the same 10-second
    // reasoning. A hung read here would hang the whole assembly.
    private static readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(10));

    private static HttpRequestMessage Preflight(string path, string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", headers);
        return request;
    }

    [Fact]
    public async Task Preflight_from_an_allowed_origin_is_answered_with_the_policy()
    {
        // `if-match` is the header that matters: omit it and every optimistic write fails in a browser while
        // continuing to pass in jsdom, which is precisely the split this behaviour exists to close.
        // The request that actually needs permission: `PUT` carrying `If-Match`. A preflight grants the
        // (method, headers) pair it was asked about, so asserting that a POST preflight also names PUT would be
        // asserting something browsers do not do — the first version of this test failed on exactly that, and the
        // code was right.
        var response = await _http.SendAsync(Preflight(
            "/api/applications", ApplicationsApiFactory.AllowedOrigin, "PUT", "content-type, if-match"));

        Assert.True(
            response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"preflight must be answered, not 405 — got {(int)response.StatusCode} {response.StatusCode}");

        Assert.Equal(ApplicationsApiFactory.AllowedOrigin, Single(response, "Access-Control-Allow-Origin"));

        var methods = Single(response, "Access-Control-Allow-Methods");
        Assert.Contains("PUT", methods);

        // Either an explicit echo of what was asked, or the `*` that means "any header". Both are acceptable; a
        // missing value is the bug.
        var headers = Single(response, "Access-Control-Allow-Headers");
        Assert.True(headers.Length == 0 || headers.Equals("*", StringComparison.OrdinalIgnoreCase) || headers.Contains("if-match", StringComparison.OrdinalIgnoreCase),
            $"preflight must permit If-Match — got \"{headers}\"");
    }

    [Fact]
    public async Task A_preflight_for_delete_names_delete()
    {
        // Two verbs are asserted across the two preflight cases because `AllowAnyMethod()` would also be satisfied by
        // an implementation that happened to hardcode the verb under test. The browser asks per verb; so does this.
        var response = await _http.SendAsync(Preflight(
            "/api/applications", ApplicationsApiFactory.AllowedOrigin, "DELETE", "content-type"));

        Assert.True(
            response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"preflight must be answered, not 405 — got {(int)response.StatusCode} {response.StatusCode}");
        Assert.Contains("DELETE", Single(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task A_read_from_an_allowed_origin_carries_the_origin_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.TryAddWithoutValidation("Origin", ApplicationsApiFactory.AllowedOrigin);

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApplicationsApiFactory.AllowedOrigin, Single(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task An_unlisted_origin_gets_an_answer_but_no_permission()
    {
        // The server still answers — CORS is not access control, and a browser-blocked request looks like a 200 whose
        // response the page may not read. What must be absent is the header that grants that read.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications");
        request.Headers.TryAddWithoutValidation("Origin", "http://not-allowed.test");

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out _),
            "an absent header is the fail-closed behaviour; a wildcard here would make the allow-list decorative");
    }

    [Fact]
    public async Task The_event_stream_is_cross_origin_readable_too()
    {
        // The whole point of the global policy. `-046`'s endpoint did not exist when the CRUD routes were written, so
        // a per-endpoint approach would leave the stream unreadable from any deployed frontend — the exact class of
        // gap that opened as `-046` in the first place (a subscription to a route the ladder never had).
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications/events");
        request.Headers.TryAddWithoutValidation("Origin", ApplicationsApiFactory.AllowedOrigin);

        // ResponseHeadersRead, or this hangs until the read timeout: the stream stays open on purpose.
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Deadline.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApplicationsApiFactory.AllowedOrigin, Single(response, "Access-Control-Allow-Origin"));
        response.Dispose();
    }

    private static string Single(HttpResponseMessage response, string name)
    {
        // Checked across both header bags: ASP.NET writes CORS headers on the *response* line, but a hand-written
        // middleware could put them on the content, and an assertion that reads the wrong bag reports absence for a
        // header that is right there.
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(",", values);
        }

        return response.Content.Headers.TryGetValues(name, out var content) ? string.Join(",", content) : string.Empty;
    }
}
