using JobTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Auth;

/// <summary>
/// The authentication gate (spec §2.1, <c>BEHAVIOR-055</c>): <b>every <c>/api/applications*</c> request needs a valid
/// session, and <c>/api/auth/login</c> does not.</c>
///
/// ## Why a path rule in middleware instead of <c>RequireAuthorization()</c> on each endpoint
///
/// Three reasons, and the first one is the ladder's own history:
///
/// 1. <b>The stream.</b> <c>-046</c> added an SSE endpoint to a catalog that already existed, and AC-1 names the failure
///    mode: *an attribute typo on one endpoint is invisible to every other test.* A prefix rule covers the route that
///    exists and the route that gets added next; a per-endpoint declaration has to be remembered, by the person who is
///    by definition not thinking about auth.
/// 2. <b>No framework auth stack needed.</b> <c>RequireAuthorization()</c> throws at startup without a registered
///    authentication scheme, and registering one means pulling in Identity's handler plumbing that
///    <c>DECISION-m4-auth-002</c> rejected for this project.
/// 3. <b>One place to read.</b> "Is this route public?" is answerable from this file. It is not answerable from N call
///    sites that each claim to have opted in.
///
/// The cost, stated: this is a <b>prefix</b> match, so a future <c>/api/applications-public</c> would be gated by
/// accident, and a route spelled <c>/Api/Applications</c> would not be. Both are visible in a diff; the second half of
/// that risk is why the check is <c>StartsWithSegments</c> (segment-wise, case-insensitive as routing is) rather than
/// <c>string.StartsWith</c>.
/// </summary>
public static class SessionGate
{
    /// <summary>The data surface: one prefix, including <c>/events</c>, which is the point.</summary>
    private static readonly PathString DataPrefix = new("/api/applications");

    /// <summary>
    /// The auth surface, minus the one entry point. <b>Added while executing <c>-054</c>, because <c>-055</c> shipped the
    /// prefix below and no more:</b> spec §4.3's Auth column marks <c>POST /api/auth/logout</c> and
    /// <c>GET /api/auth/session</c> as <i>authorized</i>, and <c>-055</c>'s own row says "every <i>data</i> route" — so the
    /// gate satisfied its behaviour exactly while under-covering the contract it came from. A logout reachable anonymously
    /// is a way to probe whether an endpoint exists, and it is the one gated route whose request an attacker can send with
    /// nobody's cookie in it.
    /// </summary>
    private static readonly PathString AuthPrefix = new("/api/auth");

    /// <summary>Spec §2.1's single exception: login is how a caller obtains the thing this gate demands.</summary>
    private static readonly PathString LoginPath = new("/api/auth/login");

    private static bool IsProtected(PathString path) =>
        path.StartsWithSegments(DataPrefix)
        || (path.StartsWithSegments(AuthPrefix) && !path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase));

    public static IApplicationBuilder UseSessionGate(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            // CORS preflight never carries cookies — a browser cannot attach them to an OPTIONS — so gating it would
            // break every cross-origin write with a 401 the client reads as "server down". UseCors short-circuits real
            // preflights before this middleware, and this line is the belt for the ones it does not (no Origin header).
            if (HttpMethods.IsOptions(http.Request.Method))
            {
                await next();
                return;
            }

            if (!IsProtected(http.Request.Path))
            {
                await next();
                return;
            }

            // Guid.TryParse first: the cookie value is attacker-controlled text on every request, and handing it to
            // Npgsql unparsed is how a gate becomes a 500 generator -- gap 12's class, at the widest input surface
            // M4 has. A malformed value is simply not a session.
            var token = http.Request.Cookies[AuthCatalog.SessionCookieName];
            if (!string.IsNullOrWhiteSpace(token) && Guid.TryParse(token, out var sessionId))
            {
                var db = http.RequestServices.GetRequiredService<JobTrackerDb>();
                // UtcNow rather than TimeProvider: -066 is the behaviour that needs an injectable clock, and it is the
                // one that will decide whether this seam is acceptable in production. Standing up a seam a third of
                // the way down the ladder and leaving one caller unswept is the half-done version of the same idea.
                var now = DateTime.UtcNow;
                var valid = await db.Sessions.AnyAsync(
                    s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now,
                    http.RequestAborted);

                if (valid)
                {
                    await next();
                    return;
                }
            }

            // Same envelope the handlers emit -- Problems.Unauthorized is reused rather than a hand-written 401, so the
            // `code` member cannot drift between the two ways of refusing. Rejected before the handler runs, so
            // `UseStatusCodePages` is never involved and the body is ours.
            http.Response.StatusCode = StatusCodes.Status200OK; // reset by IResult.ExecuteAsync below
            await Problems.Unauthorized(http.Request.Path.Value ?? DataPrefix.Value!).ExecuteAsync(http);
        });
}
