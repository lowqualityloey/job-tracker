using System.Diagnostics;
using JobTracker.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobTracker.Api.Tests;

/// <summary>
/// T-07 / R-4.4 — the health probe's bound: its configuration contract, and the promise that a database
/// which never answers is answered <em>for</em> within the window.
///
/// ## Why the bound is unit-tested and not driven through HTTP
///
/// The spec's exit criterion is "a test that a CanConnectAsync which never returns yields unhealthy within the
/// bound" — and an end-to-end version of that cannot run in this suite: the boot path's
/// <c>db.Database.Migrate()</c> (Program.cs) consumes the same connection string before any request is served,
/// so a host pointed at a hanging datasource dies in startup, not in the handler. The seam exists precisely so
/// the one contract the ALB trusts can be tested against a probe that never returns, without a database at all.
/// The HTTP wiring (key read, false→503 mapping) is covered by <c>HealthEndpointTests</c> for the healthy side
/// and by the infra plan's manual smoke for the down-DB side, as that file's header already states.
///
/// ## Why no shipped appsettings carries Health:ProbeTimeoutSeconds
///
/// Same argument <c>EventStreamKeepAliveTests</c> makes for <c>Sse:KeepAliveSeconds</c>: the default derives from
/// §4.3's proxy ceilings, and a key in <c>appsettings.json</c> would make the code's default dead and untested.
/// Only test hosts set it, and they set it through the product's own constant.
/// </summary>
public sealed class HealthProbeBoundTests
{
    private static IConfiguration ConfigWith(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                value is null ? [] : new Dictionary<string, string?> { [HealthProbe.ProbeTimeoutConfigKey] = value })
            .Build();

    [Fact]
    public void The_default_bound_is_five_seconds_and_stays_under_the_ALBs_patience()
    {
        // The derivation this pins: §4.3's target-group timeout is 10 s ([verify-at-apply]); the application must
        // pronounce itself unhealthy inside half of the proxy's patience, so the logged verdict wins the race
        // the silent one would otherwise have.
        Assert.Equal(TimeSpan.FromSeconds(5), HealthProbe.DefaultProbeTimeout);
        Assert.True(HealthProbe.DefaultProbeTimeout < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void An_absent_key_falls_back_to_the_default_bound() =>
        Assert.Equal(HealthProbe.DefaultProbeTimeout, HealthProbe.ProbeTimeout(ConfigWith(null)));

    [Fact]
    public void A_zero_bound_falls_back_because_obeying_it_would_empty_the_target_group()
    {
        // The guard's real failure mode: bound 0 means every probe answers unhealthy immediately, and the ALB
        // dutifully drains a fleet that is entirely fine. One typo must not be able to do that.
        Assert.Equal(HealthProbe.DefaultProbeTimeout, HealthProbe.ProbeTimeout(ConfigWith("0")));
    }

    [Fact]
    public void A_negative_bound_falls_back_to_the_default() =>
        Assert.Equal(HealthProbe.DefaultProbeTimeout, HealthProbe.ProbeTimeout(ConfigWith("-3")));

    [Fact]
    public void An_unparseable_bound_falls_back_to_the_default() =>
        Assert.Equal(HealthProbe.DefaultProbeTimeout, HealthProbe.ProbeTimeout(ConfigWith("five")));

    [Fact]
    public void A_positive_bound_is_obeyed_including_sub_second_values()
    {
        // Sub-second parse is load-bearing for the tests below, which compress the window the same way
        // EventStreamKeepAliveTests compresses 20 s to 50 ms: the shipped default is asserted by the test
        // above, so nothing here trusts a guess about what the knob can carry.
        Assert.Equal(TimeSpan.FromMilliseconds(250), HealthProbe.ProbeTimeout(ConfigWith("0.25")));
        Assert.Equal(TimeSpan.FromSeconds(8), HealthProbe.ProbeTimeout(ConfigWith("8")));
    }

    // The bound itself. Each test self-limits at two seconds so an unbounded ProbeAsync *fails* this suite
    // rather than hanging it — the failure a red commit must produce is a verdict, not a stall.

    private const int TestBudgetMs = 2000;
    private static readonly TimeSpan TestBudget = TimeSpan.FromMilliseconds(TestBudgetMs);

    /// <summary>
    /// Call ProbeAsync under a ceiling of its own: an unbounded implementation fails here with a message
    /// instead of hanging the suite. That self-limit is the test's skeleton key — the behaviour under test is
    /// "answers within the bound", and a helper that could itself hang forever would prove nothing about it.
    /// </summary>
    private static async Task<(bool Healthy, TimeSpan Elapsed)> TimedProbe(
        Func<CancellationToken, Task<bool>> canConnect, TimeSpan bound, CancellationToken requestAborted = default)
    {
        var sw = Stopwatch.StartNew();
        var probeCall = HealthProbe.ProbeAsync(canConnect, bound, requestAborted);
        var winner = await Task.WhenAny(probeCall, Task.Delay(TestBudget));
        Assert.True(winner == probeCall, "ProbeAsync did not answer within the test budget - the bound is not being kept");
        return (await probeCall, sw.Elapsed);
    }

    [Fact]
    public async Task A_probe_that_never_returns_is_answered_unhealthy_within_the_bound()
    {
        // R-4.4's exit criterion, word for word: a CanConnectAsync which never returns yields unhealthy within
        // the bound. The probe ignores its token entirely - the stuck-in-native-IO case the token alone cannot
        // bound - so only racing it against its own ceiling can answer this.
        var (healthy, elapsed) = await TimedProbe(
            _ => new TaskCompletionSource<bool>().Task, TimeSpan.FromMilliseconds(50));

        Assert.False(healthy);
        Assert.True(elapsed < TestBudget, $"answered after {elapsed}, i.e. late rather than bounded");
    }

    [Fact]
    public async Task A_probe_that_obeys_its_token_and_times_out_also_reads_unhealthy_not_as_a_failure()
    {
        // The other half of the promise: when the callee DOES honour cancellation, the bound firing is a
        // verdict (unhealthy), not an exception - otherwise the timeout path becomes a 500 through
        // UseExceptionHandler and the ALB logs a crash instead of a health check.
        var (healthy, _) = await TimedProbe(
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return true; // unreachable: the bound must fire first
            },
            TimeSpan.FromMilliseconds(50));

        Assert.False(healthy);
    }

    [Fact]
    public async Task A_probe_that_answers_is_passed_through_unchanged_in_both_directions()
    {
        var (up, _) = await TimedProbe(_ => Task.FromResult(true), TimeSpan.FromSeconds(5));
        var (down, _) = await TimedProbe(_ => Task.FromResult(false), TimeSpan.FromSeconds(5));

        Assert.True(up);
        Assert.False(down);
    }

    [Fact]
    public async Task A_disconnected_request_is_propagated_not_converted_to_unhealthy()
    {
        // requestAborted and the bound look identical from inside a cancelled token - the distinction is which
        // source fired. A client that hung up gets its cancellation propagated (the framework aborts the
        // response), not a 503 dressed up as a health verdict for a response nobody is reading.
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HealthProbe.ProbeAsync(
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            TimeSpan.FromSeconds(30),
            aborted.Token));
    }
}
