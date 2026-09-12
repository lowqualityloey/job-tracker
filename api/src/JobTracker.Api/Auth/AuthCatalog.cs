using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api;

/// <summary>
/// The <c>/api/auth/*</c> surface (<c>BEHAVIOR-051</c>), kept in its own catalog the way <see cref="ApplicationCatalog"/>
/// is: <c>Program.cs</c> is composition, and the route table should read like a list of capabilities rather than a stack of
/// lambdas.
///
/// <b>Nothing here enforces authentication on the application routes.</b> That is §2.1 and a later behaviour. Adding a
/// global 401 alongside the login endpoint would have been the classic auth mistake: every existing M3 test starts failing
/// for a reason unrelated to the code under test, and the one behaviour that was supposed to be small becomes a
/// milestone-wide change. Landing the credential path first and the gate second is also the only order that can be
/// verified — you need a working login to write a test that says "this route now requires it".
/// </summary>
public static class AuthCatalog
{
    /// <summary>
    /// The <c>__Host-</c> prefixed session cookie name. The prefix is a browser-enforced contract, not a naming taste: a
    /// cookie with it must be <c>Secure</c>, must carry <b>no</b> <c>Domain</c> attribute, and must be <c>Path=/</c>.
    /// Setting <c>Domain=.example.com</c> would silently stop the browser honouring the prefix at all — so the attributes
    /// below are asserted as a set in <c>-051</c>, not reviewed as prose.
    /// </summary>
    public const string SessionCookieName = "__Host-JTSession";

    /// <summary>
    /// Provisional idle window. <c>BEHAVIOR-055</c>/<c>-056</c> own expiry and are the behaviours that make this a
    /// configuration value with asserted boundaries; hard-coding it here is the smallest thing that satisfies the one
    /// demand made so far — that <c>expires_at</c> is written rather than left null.
    /// </summary>
    private static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(30);

    public static IEndpointRouteBuilder MapAuthCatalog(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (
            LoginRequest? request, JobTrackerDb db, IPasswordService passwords, HttpContext http) =>
        {
            var errors = new List<FieldError>();
            if (request is null || string.IsNullOrWhiteSpace(request.Email))
            {
                errors.Add(new FieldError("email", "An email address is required."));
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Password))
            {
                errors.Add(new FieldError("password", "A password is required."));
            }

            // Before any database work: an empty body is a malformed request, not a failed authentication, and answering
            // it with 401 would tell an attacker that the shape of the request was acceptable. This is also gap 12's
            // class — inputs that used to reach a binder and throw -- handled explicitly at the route that matters most.
            if (errors.Count > 0)
            {
                return Problems.Validation(errors, "/api/auth/login", "Email and password are required.");
            }

            var email = request!.Email!;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            // BEHAVIOR-053 / spec 2.4 -- the short-circuit IS the oracle. `user is null || !Verify(...)` returns the same
            // 401 body either way, but the absent path never pays for PBKDF2, so it answers ~300 ms early and an attacker
            // with a stopwatch has a member list. Absent users now get a full verify against an envelope that belongs to
            // nobody (see DummyCredential for why that envelope is generated rather than pasted).
            if (user is null)
            {
                passwords.Verify(DummyCredential.Envelope, request.Password!);
                return Problems.Unauthorized("/api/auth/login");
            }

            if (!passwords.Verify(user.PasswordHash, request.Password!))
            {
                // One response for both causes, with no dummy verify yet: byte-parity between "no such account" and "wrong
                // password" -- including the structural proof that a comparison happened either way -- is -053's claim, and
                // pre-empting it here would leave that behaviour asserting nothing new.
                return Problems.Unauthorized("/api/auth/login");
            }

            // BEHAVIOR-052 / AC-5 -- session rotation. <b>The id the client arrived with never outlives this login.</b>
            //
            // Why <i>here</i>, at the moment credentials are proven, and not in the gate: fixation is defeated only if the
            // superseded id dies in the same transaction that creates its replacement. Revoked in the gate, a stale id
            // keeps working until someone's next request happens to be a read. Revoked at logout, it works forever, which
            // is the bug -051 shipped without meaning to: it issued a fresh Guid per login and left every earlier one
            // valid, and "the id changed" is precisely the property a fixation attack survives.
            //
            // Revoked rather than deleted, so the audit question stays answerable: when did this session stop working, and
            // was that logout, expiry, or a second login? ExecuteUpdate so the sweep is one statement and cannot be
            // reordered against the insert below -- and filtered on RevokedAt == null, because re-logging in with an
            // already-dead cookie must not overwrite the moment it first died.
            var superseded = http.Request.Cookies[SessionCookieName];
            if (!string.IsNullOrWhiteSpace(superseded) && Guid.TryParse(superseded, out var supersededId))
            {
                await db.Sessions
                    .Where(s => s.Id == supersededId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, DateTime.UtcNow));
            }

            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.Add(IdleWindow)
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            http.Response.Headers.Append("Set-Cookie",
                $"{SessionCookieName}={session.Id}; Secure; HttpOnly; SameSite=Lax; Path=/");
            return Results.NoContent();
        });

        // BEHAVIOR-054 / AC-6 / spec 4.3 -- logout. 204 + a clearing Set-Cookie + sessions.revoked_at, and the gate in
        // SessionGate is what makes it "authorized"; this handler never asks whether the caller is logged in, because a
        // handler that re-implements the gate is a second gate to keep in sync.
        //
        // The server-side row is the control. The cookie header below is courtesy: without it a client keeps replaying an
        // id that is already dead, which is harmless to the caller but noise in every log. AC-6's own wording is the
        // ordering: "clearing the browser cookie is not the control", so the test for THIS behaviour reads the database
        // and treats the header as secondary.
        endpoints.MapPost("/api/auth/logout", async (JobTrackerDb db, HttpContext http) =>
        {
            // Scoped to the presented session id, not to the user. `WHERE user_id = @me` would pass every other test in
            // -054 and turns a stolen cookie into a per-account denial-of-service handle; "log out everywhere" is a
            // different feature, listed as out of scope in spec 4.3's neighbours, and it would need its own behaviour row.
            // Revoked, never deleted, and filtered on RevokedAt IS NULL for the same reason -052's rotation filters on it:
            // revoked_at records the moment a session stopped being valid, and a repeat call must not rewrite history.
            var presented = http.Request.Cookies[SessionCookieName];
            if (!string.IsNullOrWhiteSpace(presented) && Guid.TryParse(presented, out var sessionId))
            {
                await db.Sessions
                    .Where(s => s.Id == sessionId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, DateTime.UtcNow));
            }

            // __Host- can only be replaced by a header carrying the same constraints -- no Domain, Path=/, Secure -- so a
            // clearing cookie that omits any of them leaves the dead id in the jar. Asserted in -054's test rather than
            // trusted from the spec.
            http.Response.Headers.Append("Set-Cookie",
                $"{SessionCookieName}=; Max-Age=0; Secure; HttpOnly; SameSite=Lax; Path=/");
            return Results.NoContent();
        });

        return endpoints;
    }

    /// <summary>
    /// Nullable members, deliberately: with <c>required</c> non-nullable ones, <c>{}</c> fails in the JSON binder and the
    /// framework answers with a problem document that carries <b>no</b> <c>code</c> member — an envelope that looks
    /// machine-readable but isn't, which is precisely what DECISION-m3-backend-api-004 exists to prevent. Missing values
    /// are therefore a validation finding this handler controls.
    /// </summary>
    public sealed record LoginRequest(string? Email, string? Password);
}
