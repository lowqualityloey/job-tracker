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
}
