using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace JobTracker.Api.Auth;

/// <summary>
/// BEHAVIOR-m4-auth-063 / AC-11 / DECISION-m4-auth-006 — the antiforgery half of the session design.
///
/// ## What this is not
///
/// It is **not authorisation**. The session cookie already decides whether the caller may act; this decides whether the
/// *browser* was the one that chose to send. A `403` here does not mean "you may not delete records", it means "a request
/// that arrived with your credentials but without anything proving your page asked for it". Conflating the two is how a
/// CSRF control ends up being the only access check on a route, and `SessionGate` stays responsible for that.
///
/// ## Why a header, when the cookie is already in the request
///
/// Because a cross-site page can *send* whatever cookies the browser holds and cannot set an arbitrary header. That
/// asymmetry is the entire mechanism: the value must be one the requester can read, and reading another origin's cookies
/// is the thing the platform will not allow. Which is also why this cookie is deliberately **not** `HttpOnly` — a token
/// the client cannot read cannot be echoed, and a token that is only ever sent automatically protects nothing. The
/// `__Host-` prefix is still applied, for the reason it is applied to the session cookie: no other origin may set or
/// replace it (`Secure`, no `Domain`, `Path=/`).
///
/// ## Why the value is bound to the session
///
/// `DECISION-m4-auth-006` specifies a token the server can verify and a foreign page cannot forge, and a value that was
/// the same for every caller would be a shared secret instead — stealable once, reusable forever, and unrotatable
/// without a global change. So the token is derived from *this* session's id: log out, log in, and the old value stops
/// validating. See `A_token_belonging_to_another_session_is_refused`.
///
/// ## The one place this deviates from the decision's literal wording, stated rather than glossed
///
/// The decision says "HMAC over the session id". This uses the ASP.NET Core **data protection** stack
/// (`CreateProtector(purpose).Protect`), which is an authenticated, keyed, *encrypted* envelope rather than a bare HMAC.
/// A hand-rolled HMAC would need a key configured into every environment plus a boot gate to insist on it — a new
/// invariant to break and a new secret to leak. The property the decision asks for is unchanged: unverifiable without
/// the server's key, and bound to the session. Recorded as a note under the decision, dated, appended not replaced.
///
/// <b>And one claim corrected the hard way.</b> This comment originally said the framework gives "the shared key ring
/// on deployed instances for free". It does not. The default ring is **ephemeral per process**: two instances of this
/// app, or one instance restarted, cannot read each other's tokens, and every write routed to the "wrong" one answers
/// `403`. `OwnershipTests` discovered it by building a fresh test host per request and watching six of its own cases
/// turn into antiforgery refusals. Single-instance dev cannot see this; the deployed shape in <c>docs/aws-deployment.md</c>
/// can. Making the ring survive is a deployment configuration step — <c>PersistKeysTo*</c> over a store every instance can
/// reach — and it is <c>TDD-PRACTICE-m4-authentication-074</c>, named here because a reader of this file is the reader who
/// needs it. Until then, the honest statement of behaviour is: <strong>a session may only be written to by the instance that
/// issued it.</strong>
///
/// <b>That paragraph was wrong four times, and <c>-074</c> is why each is corrected here rather than left.</b>
/// (1) <em>"Single-instance dev cannot see this"</em> — it can, and immediately: log in, write (201), restart the process
/// against the same database, replay the token (<c>403</c>). Measured that way on 127.0.0.1 with one scratch database.
/// Framing it as a multi-instance concern made a row that was breaking users on every deploy look hypothetical.
/// (2) <em><c>docs/aws-deployment.md</c></em> — that file has never existed in this repository
/// (<c>git log --all --diff-filter=A -- docs/aws-deployment.md</c> returns nothing); it is cited as a source in six places
/// across four documents and corrected where each is maintained. (3) <em><c>PersistKeysTo*</c></em> — no such extension is
/// in the shared framework: reflection over <c>Microsoft.AspNetCore.DataProtection.dll</c> finds only the two
/// <c>AddDataProtection</c> overloads, because the persistence family is separate packages. (4)
/// <em><c>TDD-PRACTICE-m4-authentication-074</c></em> — that ID series never existed (`git grep -c TDD-PRACTICE origin/main -- docs`, anchored at a revision on purpose: a working-tree grep now matches the sentences correcting it); the row lives in the behaviour
/// register as <c>-074</c> and its record is <c>TDD-EXEC-m4-authentication-074</c>.
///
/// <b>And the last sentence is now false in the present tense, deliberately kept.</b> The ring is persisted:
/// <see cref="PostgresKeyRing"/> stores it in <c>data_protection_keys</c>, the database the sessions already live in, so a
/// token survives a restart and crosses instances that share a database. Read the sentence above as what was true at
/// <c>-063</c>, because the reasoning that produced it — a claim about a framework, corrected by an experiment rather than
/// by rereading the claim — is the part worth keeping.
/// </summary>
public static class Antiforgery
{
    /// <summary>The header a client must set on a state-changing call. Named in AC-11's own text, so it is not renameable.</summary>
    public const string HeaderName = "X-CSRF-Token";

    /// <summary>
    /// The carrier the client reads the token from. `__Host-` with the same three constraints as the session cookie, and
    /// for the same reason; **no `HttpOnly`**, on purpose, and the paragraph above is the defence of that choice.
    /// </summary>
    public const string CookieName = "__Host-JTCsrf";

    /// <summary>
    /// A purpose string is a domain separator: a protected value from one protector will not unprotect under another.
    /// Versioned in the literal so a future change invalidates every token issued under the old one, which is what a
    /// token format change should do.
    /// </summary>
    private const string Purpose = "JobTracker.Api.Auth.Antiforgery.v1";

    /// <summary>
    /// The cookie attributes for the token, written as a literal rather than through <c>CookieOptions</c> for the same
    /// reason the session cookie is: the emitted string is the artifact a browser actually reacts to, and this file's
    /// whole subject is a prefix rule a subtly-different attribute set silently breaks. Never conditional on environment.
    /// </summary>
    public const string CookieAttributes = "Secure; SameSite=Lax; Path=/";

    public static string Issue(IDataProtectionProvider provider, Guid sessionId) =>
        provider.CreateProtector(Purpose).Protect(sessionId.ToString("D"));

    /// <summary>
    /// Constant in effort where it is easy to be, and honest where it is not: the comparison is between two *decrypted*
    /// session ids, so the secret is never compared byte-for-byte and timing says nothing about it. A malformed or
    /// foreign token fails to unprotect, which is a distinct and equally rejecting path.
    /// </summary>
    public static bool Matches(IDataProtectionProvider provider, string? token, Guid sessionId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            return provider.CreateProtector(Purpose).Unprotect(token) == sessionId.ToString("D");
        }
        catch (CryptographicException)
        {
            // Tampered, truncated, or produced by another key ring. All three mean "not issued by this server for this
            // session", and the distinction is not worth exposing to a client that is being rejected either way.
            return false;
        }
    }
}

/// <summary>
/// The middleware half: refuse a state-changing request that carries a session but not a matching token.
/// Registered **after** <see cref="SessionGate"/> on purpose — see the ordering note in `Program.cs`.
/// </summary>
public static class AntiforgeryGate
{
    private static readonly PathString DataPrefix = new("/api/applications");

    /// <summary>
    /// The methods that can change state. Written as an allow-list of *unsafe* verbs rather than "everything except GET",
    /// because `OPTIONS` (a preflight) and `HEAD` would otherwise be refused with a `403` that no browser can explain to
    /// a developer — and a preflight arriving here would mean CORS had already been bypassed, which is a different bug
    /// that this gate should not be allowed to disguise.
    /// </summary>
    private static readonly string[] UnsafeMethods = [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    /// <summary>
    /// Scope: the data routes. `/api/auth/*` is deliberately out — login cannot carry a token it has not yet been
    /// issued, and **logout is exempt on the merits, not by oversight**: a forgery there costs the victim their own
    /// session and nothing else, and requiring a token there would mean a user whose token is somehow unreadable can be
    /// locked out of signing out. If that calculus changes, change it in a row, not in a passing edit: this comment is
    /// the only place the reasoning survives.
    /// </summary>
    private static bool Applies(HttpContext ctx) =>
        ctx.Request.Path.StartsWithSegments(DataPrefix)
        && Array.IndexOf(UnsafeMethods, ctx.Request.Method) >= 0;

    public static IApplicationBuilder UseAntiforgeryGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AntiforgeryMiddleware>();

    private sealed class AntiforgeryMiddleware(RequestDelegate next, IDataProtectionProvider protection)
    {
        public async Task InvokeAsync(HttpContext ctx)
        {
            if (!Applies(ctx))
            {
                await next(ctx);
                return;
            }

            // The session id is read from the cookie rather than from anything the gate stored in `HttpContext.Items`:
            // this middleware is not allowed to assume SessionGate ran, and if it ever stops running first, the correct
            // failure is a rejection here — not a token check silently validating nothing.
            if (!ctx.Request.Cookies.TryGetValue(AuthCatalog.SessionCookieName, out var rawSession)
                || !Guid.TryParse(rawSession, out var sessionId)
                || !Antiforgery.Matches(protection, ctx.Request.Headers[Antiforgery.HeaderName].ToString(), sessionId))
            {
                // Three causes folded into one answer on purpose. Whether the session is missing, unreadable, or the
                // token simply does not belong to it, what the caller needs to know is identical, and distinguishing
                // them would tell an attacker whether a guessed session id parses.
                //
                // Same envelope the handlers emit and the same two-step as `SessionGate` line 132: the 403 is set, the
                // status is reset to 200, and `IResult.ExecuteAsync` writes the real one — so the `code` member cannot
                // drift between the two ways of refusing, and `UseStatusCodePages` is never involved because this is
                // rejected before any endpoint runs.
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                await Problems.Antiforgery(ctx.Request.Path.Value ?? Antiforgery.HeaderName).ExecuteAsync(ctx);
            }
            else
            {
                await next(ctx);
            }
        }
    }
}
