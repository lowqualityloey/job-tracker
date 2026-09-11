using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobTracker.Api.Tests.Infrastructure;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// <c>BEHAVIOR-m3-backend-api-046</c> (p0, <c>area:backend</c>) — the event stream the client subscribes to.
///
/// <para>
/// This endpoint was **missing from the task record's behaviour ladder**, and the omission is why it matters that
/// the tests are written against the spec rather than the ladder: <c>DECISION-m3-backend-api-005</c> (owner-approved,
/// "SSE at <c>GET /api/applications/events</c>… one <c>event: change</c> message per committed write") and §4.3's
/// contract table both name it, while the only SSE behaviour in the ladder was <c>-040</c>, which is *client*-side.
/// Implementing <c>-040</c> alone would have shipped a browser opening a subscription to a route that answers 404,
/// and AC-11 — "a write from another client triggers the callback" — could then have been checked off as verified
/// against a fake. The app's cross-client sync would be silently worse than it was in M2, where the
/// <c>StorageEvent</c> path genuinely worked.
/// </para>
///
/// <para>
/// Every assertion here is on **bytes off the wire**, because SSE is a text framing protocol and the only failure
/// mode that matters is a browser's parser not recognising what arrives. A frame missing its blank-line terminator,
/// or a <c>content-type</c> of <c>application/json</c>, or headers withheld until the first event (which makes
/// <c>EventSource</c> sit in "connecting" while the user sees a stale list) all pass a test that inspects objects
/// instead of bytes.
/// </para>
/// </summary>
public sealed class EventStreamTests(ApplicationsApiFixture fixture) : IClassFixture<ApplicationsApiFixture>
{
    /// <summary>
    /// How long a test waits for a frame before declaring it never came. Generous on purpose: this asserts a
    /// *push*, so a slow pass is still a pass, and a too-tight bound would turn CI contention into a flaky suite —
    /// the failure mode M2b's cross-tab tests had to be reasoned about carefully.
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(10);

    private async Task<Stream> OpenStreamAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications/events");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        // ResponseHeadersRead is the whole test: with the default completion option the client buffers until the
        // response body ends, and an SSE body never ends, so the request would hang rather than fail.
        var response = await fixture.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType?.ToString());

        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Reads until <paramref name="needle"/> appears, failing rather than hanging.
    ///
    /// A cancelled <see cref="CancellationTokenSource"/> surfaces as an <c>OperationCanceledException</c>, which xUnit
    /// reports as a timeout; a bare <c>ReadAsync</c> with no bound would hold the collection until the runner kills
    /// it, which is a far worse diagnostic and costs minutes.
    /// </summary>
    private static async Task<string> ReadUntilAsync(Stream stream, string needle)
    {
        using var timeout = new CancellationTokenSource(FrameTimeout);
        var buffer = new byte[4096];
        var seen = new StringBuilder();

        while (!timeout.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                break;
            }

            seen.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (seen.ToString().Contains(needle, StringComparison.Ordinal))
            {
                return seen.ToString();
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"no frame containing \"{needle}\" within {FrameTimeout.TotalSeconds}s. Received: {seen}");
    }

    [Fact]
    public async Task Event_stream_is_served_as_text_event_stream()
    {
        // The literal-segment question hides in this route: `/api/applications/{id}` was registered first, and a
        // router that preferred the parameter pattern would answer 404 with a problem document for an application
        // called "events". Asserting the media type is what distinguishes the two 200-shaped outcomes.
        await using var stream = await OpenStreamAsync();

        // Opening the stream must itself produce bytes: §4.3 says "a dropped stream reconnects", and a browser's
        // EventSource only leaves `readyState: CONNECTING` once a response body starts flowing. A server that sets
        // the media type and then writes nothing would satisfy the content-type assertion while no client ever
        // considers the connection open.
        await ReadUntilAsync(stream, ":");
    }

    [Fact]
    public async Task A_committed_create_publishes_a_change_event_carrying_its_id()
    {
        await fixture.ExecuteAsync("delete from applications;");
        await using var stream = await OpenStreamAsync();

        var id = Guid.NewGuid();
        var created = await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id,
            companyName = "Fishermend",
            jobTitle = "Frontend Engineer",
            location = "Auckland, NZ",
            status = "Saved",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var frame = await ReadUntilAsync(stream, id.ToString());

        // Both lines, checked as text. `EventSource` dispatches on the `event:` field and hands the handler the
        // `data:` field; a frame that carries the id but omits the event name arrives as a generic `message`, which
        // the adapter's `addEventListener('change', …)` never sees.
        Assert.Contains("event: change", frame, StringComparison.Ordinal);
        Assert.Contains("data:", frame, StringComparison.Ordinal);

        // §4.3's frame separator. Without the blank line the client keeps buffering and dispatches nothing, so a
        // server can be correct in content and broken in framing.
        Assert.Contains("\n\n", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_committed_update_and_delete_also_publish()
    {
        // "after each committed write" is three verbs, and the client's read-modify-write makes PUT the common one.
        // A stream that fired only on create would leave the app's own status dropdown looking dead on every other
        // client — the failure a reviewer would notice first, and the one a create-only implementation hides.
        await fixture.ExecuteAsync("delete from applications;");
        await using var stream = await OpenStreamAsync();

        var id = Guid.NewGuid();
        var created = await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id,
            companyName = "Sleek",
            jobTitle = "Backend Engineer",
            location = "Wellington, NZ",
            status = "Saved",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ReadUntilAsync(stream, id.ToString());

        var update = await SendUpdateAsync(id, await RevisionAsync(id), "Interview");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // The assertion is "a frame arrived after this write", and it is carried by **read position**, not by content:
        // §4.3's payload is the id alone, so an update frame and a delete frame are byte-identical by design. That is
        // fine here and worth stating, because it is also why the client cannot tell *what* changed from the stream
        // and must re-read the catalog — the event is an invalidation signal, not a delta.
        var afterUpdate = await ReadUntilAsync(stream, id.ToString());
        Assert.Contains("event: change", afterUpdate, StringComparison.Ordinal);

        var deletion = new HttpRequestMessage(HttpMethod.Delete, $"/api/applications/{id}");
        deletion.Headers.TryAddWithoutValidation("If-Match", $"\"{await RevisionAsync(id)}\"");
        var deleted = await fixture.Http.SendAsync(deletion);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await ReadUntilAsync(stream, id.ToString());
        Assert.Contains("event: change", afterDelete, StringComparison.Ordinal);

        // Not asserted: a count. This host is shared across the class and a sibling test's write legitimately pushes
        // frames, so "exactly N" here would be asserting something the test does not control — the same trap
        // M2's file-scoped global resets exist to avoid, in a different building.
    }

    /// <summary>The current <c>revision</c> of a stored record, read from the API rather than assumed (§4.3's
    /// <c>xmin</c> → <c>revision</c>). F-3 made <c>If-Match</c> required on DELETE, so a test that skips this is
    /// refused for a reason unrelated to the stream.</summary>
    private async Task<uint> RevisionAsync(Guid id)
    {
        using var stored = JsonDocument.Parse(await fixture.Http.GetStringAsync($"/api/applications/{id}"));
        return stored.RootElement.GetProperty("revision").GetUInt32();
    }

    private Task<HttpResponseMessage> SendUpdateAsync(Guid id, uint revision, string status)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/applications/{id}")
        {
            Content = JsonContent.Create(new
            {
                id = id.ToString(),
                companyName = "Sleek",
                jobTitle = "Backend Engineer",
                location = "Wellington, NZ",
                status,
            }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\"");
        return fixture.Http.SendAsync(request);
    }

    [Fact]
    public async Task A_rejected_write_publishes_nothing()
    {
        // The contract's word is **committed**. A validation-refused POST changes no row, so a stream that published
        // on every request would make every client re-read the catalog on somebody else's typo — and in a two-tab
        // demo of a form rejecting input, the visible symptom is the other tab's list jumping.
        await fixture.ExecuteAsync("delete from applications;");
        await using var stream = await OpenStreamAsync();

        var rejected = await fixture.Http.PostAsJsonAsync("/api/applications", new
        {
            id = Guid.NewGuid(),
            companyName = "",
            jobTitle = "",
            location = "Auckland, NZ",
            status = "Saved",
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // Negative assertions need a bound that is long enough to be honest and short enough to be cheap: 500 ms of
        // silence after a 400 is the observable claim, and `ReadUntilAsync` would have thrown only after 10 s.
        var buffer = new byte[4096];
        using var patience = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        string received;
        try
        {
            var read = await stream.ReadAsync(buffer, patience.Token);
            received = Encoding.UTF8.GetString(buffer, 0, Math.Max(read, 0));
        }
        catch (OperationCanceledException)
        {
            received = string.Empty;
        }

        // The opening comment frame is allowed — that is the handshake, not an event. A `change` frame is not.
        Assert.DoesNotContain("event: change", received, StringComparison.Ordinal);
    }
}
