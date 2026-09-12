using JobTracker.Api.Data;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Auth;

/// <summary>
/// Points the data-protection key manager at <see cref="PostgresKeyRing"/>.
///
/// <para>
/// This exists as its own class because the obvious one-liner does not work, and the way it does not work is the
/// reason to keep it visible. <c>builder.Services.AddSingleton&lt;IXmlRepository, PostgresKeyRing&gt;()</c> compiles,
/// starts clean, and changes nothing: <see cref="KeyManagementOptions"/> carries an <c>XmlRepository</c> property whose
/// default is an internal ephemeral store, and the key manager reads <em>that</em> — it never resolves
/// <c>IXmlRepository</c> from the container. <c>-074</c>'s tests answered <c>403</c> with the DI registration in place,
/// which is how this was found: the silent version of the fix is indistinguishable from the bug.
/// </para>
///
/// <para>
/// Configuring options is also the only place the scoped-vs-singleton boundary can be crossed honestly.
/// <see cref="PostgresKeyRing"/> must be a singleton, because DataProtection holds one repository for the life of the
/// process, while <see cref="JobTrackerDb"/> is scoped — so the repository cannot take a context and must take the
/// scope factory. An <see cref="IConfigureOptions{TOptions}"/> implementation is constructed by DI, which is where that
/// factory comes from without reaching for a service provider at registration time.
/// </para>
/// </summary>
public sealed class PostgresKeyRingConfiguration(IServiceScopeFactory scopeFactory)
    : IConfigureOptions<KeyManagementOptions>
{
    private readonly PostgresKeyRing ring = new(scopeFactory);

    public void Configure(KeyManagementOptions options) => options.XmlRepository = ring;
}
