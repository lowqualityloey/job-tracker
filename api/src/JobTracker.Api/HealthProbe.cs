using System.Globalization;

namespace JobTracker.Api;

/// <summary>
/// R-4.4 — the bounded database probe behind <c>GET /api/health</c>.
///
/// ## Why the bound exists at all
///
/// The ALB target group trusts this endpoint to say "unhealthy" (spec §4.2, D-M5-7), and the one failure it was
/// silent about is a database whose packets are <em>dropped</em> rather than refused: §1.2 measured a refused
/// connection answering in 0.09 s, but a black-holed route leaves <c>CanConnectAsync</c> thinking inside Npgsql's
/// connect timeout while the proxy's own 10 s wait expires into a recorded unhealthy target with no application
/// log line. The spec's fix (2) is "treat 'still thinking at 5 s' as unhealthy" — measured here, in code, rather
/// than trusted to a connection-string keyword whose scope Npgsql's documentation has read both ways.
///
/// ## Why the bound is a race, not just a token
///
/// Handing the probe a cancelling <see cref="CancellationToken"/> bounds the probe only if the probe obeys it —
/// and the exact path this task exists for is a callee stuck in native I/O that cannot obey. So
/// <see cref="ProbeAsync"/> races the probe against its own ceiling and treats losing as unhealthy regardless:
/// the bound is a promise this method makes, not a hope about what <c>CanConnectAsync</c> will do with a token.
/// </summary>
public static class HealthProbe
{
    /// <summary>
    /// R-4.4's probe window, in seconds, read from configuration — the same seam rule T-06 established for
    /// <c>Sse:KeepAliveSeconds</c>: the name is the deployment knob's contract, tests set it through this symbol
    /// rather than restating the string, and it deliberately appears in no shipped appsettings (the default
    /// derives from §4.3's proxy ceilings; a key in <c>appsettings.json</c> would make the default dead code).
    /// </summary>
    public const string ProbeTimeoutConfigKey = "Health:ProbeTimeoutSeconds";

    /// <summary>
    /// Five seconds, derived rather than chosen: it must answer unhealthy inside the ALB's target-group timeout
    /// (10 s, spec §4.3, <c>[verify-at-apply]</c>) with margin, so the application's own verdict — which is
    /// logged and has a body — beats the proxy's silent judgement of it.
    /// </summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The probe window for this configuration. The guard is the point, and its blast radius is larger than
    /// T-06's: a non-positive or unparseable value falls back to <see cref="DefaultProbeTimeout"/> rather than
    /// being obeyed, because a bound of <c>0</c> means every probe answers unhealthy immediately, which means one
    /// operator typo in one environment variable empties the target group and the load balancer has no healthy
    /// target left to route to. Absent and unusable are therefore the same answer, and the answer is the
    /// documented default.
    /// </summary>
    public static TimeSpan ProbeTimeout(IConfiguration config) =>
        double.TryParse(config[ProbeTimeoutConfigKey], CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultProbeTimeout;

    /// <summary>
    /// Ask <paramref name="canConnect"/> whether the database is reachable, and answer <c>false</c> — unhealthy —
    /// if it is still thinking when <paramref name="bound"/> elapses. A request abort is propagated, not masked:
    /// a client that hung up gets the framework's cancellation, not a 503 it will never read.
    /// </summary>
    /// <param name="canConnect">
    /// The probe to run, handed a token that fires at the bound. In production this is
    /// <c>ct => db.Database.CanConnectAsync(ct)</c>; tests hand it fakes, which is the whole reason the
    /// endpoint's logic moved here from the route lambda.
    /// </param>
    /// <param name="bound">The ceiling <see cref="ProbeAsync"/> guarantees, read through <see cref="ProbeTimeout"/>.</param>
    /// <param name="requestAborted">The request's own cancellation, honoured distinctly from the bound.</param>
    public static async Task<bool> ProbeAsync(
        Func<CancellationToken, Task<bool>> canConnect, TimeSpan bound, CancellationToken requestAborted)
    {
        // Seam commit, deliberately today's behaviour: unbounded. R-4.4's failing tests drive the race out of
        // the next commit; shipping the signature first keeps Red and Green separately provable.
        return await canConnect(requestAborted);
    }
}
