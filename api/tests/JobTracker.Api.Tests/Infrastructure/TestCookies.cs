using System.Net.Http.Headers;
using JobTracker.Api.Auth;

namespace JobTracker.Api.Tests.Infrastructure;

/// <summary>
/// Reads cookies out of a response's <c>Set-Cookie</c> headers, **by name**.
///
/// ## Why this exists as one file
///
/// Six test files each carried their own three-line version of this, and all six said
/// <c>Assert.Single(response.Headers.TryGetValues("Set-Cookie", …))</c>. That assertion was never about the number
/// three: it was a stand-in for "login sets the session cookie and nothing else", which held until BEHAVIOR-m4-auth-063
/// added the antiforgery token and made it false for a good reason. Six files then failed at once with
/// <c>The collection contained 2 items</c>, and the fix could not honestly be "expect two" — a pin that counts cookies
/// says nothing about the thing every one of those tests actually cares about, which is the attributes on *this* cookie.
///
/// So the selection is named, in one place. The positional versions were also quietly load-bearing:
/// <c>OwnershipTests</c> used <c>v.First()</c>, which works only while the session cookie happens to be emitted first.
/// That is the same class of latent coupling <c>ApplicationsApiFixture</c> carried, and a helper that cannot express
/// "the cookie called X" is how a reordering becomes a false pass rather than a loud failure.
///
/// ## What deliberately stays per-file
///
/// The assertions on attributes. Those are the claims <c>-051</c>, <c>-052</c> and <c>AC-2</c> exist to make, and
/// pulling them into a shared helper would turn a ratified promise about the session cookie into a utility nobody
/// reads. This file answers "which header is this"; it must not start answering "is it any good".
/// </summary>
internal static class TestCookies
{
    /// <summary>Every raw <c>Set-Cookie</c> value on the response, in the order emitted.</summary>
    internal static IReadOnlyList<string> All(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : [];

    /// <summary>
    /// The full <c>Set-Cookie</c> value for <paramref name="name"/>, attributes included — for tests that assert on
    /// attributes, which is most of them. Throws rather than returning null: every caller is mid-assertion, and a
    /// missing cookie is the failure, not a value to keep passing around.
    /// </summary>
    internal static string Required(HttpResponseMessage response, string name)
    {
        var hit = All(response).FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal));
        if (hit is null)
        {
            throw new InvalidOperationException(
                $"no `{name}` in this response's Set-Cookie headers — got [{string.Join(" | ", All(response))}]");
        }

        return hit;
    }

    /// <summary>
    /// Just the <c>name=value</c> pair, for replaying as a request <c>Cookie:</c> header. The attributes belong to the
    /// browser, and echoing <c>Secure; HttpOnly</c> back in a request header is how a cookie test quietly stops testing
    /// the thing it names.
    /// </summary>
    internal static string Pair(HttpResponseMessage response, string name) =>
        Required(response, name).Split(';', 2)[0];

    /// <summary>Just the value — for a token that must be sent in a header, where the name must NOT be included.</summary>
    internal static string Value(HttpResponseMessage response, string name) =>
        Pair(response, name).Substring(name.Length + 1);

    /// <summary>The session pair a fixture-style client replays as its default <c>Cookie</c> header.</summary>
    internal static string SessionPair(HttpResponseMessage response) =>
        Pair(response, AuthCatalog.SessionCookieName);

    // ---------------------------------------------------------------------------------------------
    // The jar. Read this before deciding the design is too clever for a test suite.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Session pair → the antiforgery token the server issued alongside it.
    ///
    /// <c>-063</c> means a request that carries a session cookie must also carry that session's token, and five test
    /// files build requests by hand from a cookie string they got from a login helper. The alternatives were threading
    /// a second value through seven call sites and several method signatures, or a registry keyed by something that is
    /// already globally unique (a session id, minted per login, never reused — see DECISION-m4-auth-003's rotation
    /// rule). This is the registry, and <see cref="SessionOf"/> is the only way into it, so an entry can only exist for
    /// a session this process actually logged in and received a token for.
    ///
    /// It is <c>ConcurrentDictionary</c> because xunit runs test classes in parallel here; it is never cleared because a
    /// session id from a previous test cannot collide with one from a later one. What it does cost: reading a login also
    /// recording its token is a side effect, and a helper named like a getter doing it is the kind of thing that confuses
    /// someone at midnight. Hence the name — <c>SessionOf</c> says "take the session out of this login", and the token
    /// is part of what a login hands out.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Jar = new();

    /// <summary>
    /// The session pair from a login response, recorded so that <see cref="Attach"/> can supply the matching token.
    /// A test that means to withhold the token must not come through here — <see cref="AntiforgeryGateTests"/> reads
    /// <c>Set-Cookie</c> directly and asserts both the present and absent cases.
    /// </summary>
    internal static string SessionOf(HttpResponseMessage login)
    {
        var session = Pair(login, AuthCatalog.SessionCookieName);
        Jar[session] = Value(login, Antiforgery.CookieName);
        return session;
    }

    /// <summary>
    /// Adds the <c>Cookie</c> and the <see cref="Antiforgery.HeaderName"/> that belongs with it. Throws if this session
    /// was never issued by a login in this process: a request that silently arrives without a header would answer
    /// <c>403</c>, and the suite would blame the gate for a mistake in the harness.
    /// </summary>
    internal static HttpRequestMessage Attach(HttpRequestMessage request, string sessionPair)
    {
        request.Headers.Add("Cookie", sessionPair);

        if (!Jar.TryGetValue(sessionPair, out var token))
        {
            throw new InvalidOperationException(
                $"no antiforgery token recorded for `{sessionPair}` — it did not come from `SessionOf`, so the gate "
                + "will refuse this request for a reason the test is not actually testing.");
        }

        request.Headers.Add(Antiforgery.HeaderName, token);
        return request;
    }
}
