using System.Xml.Linq;
using JobTracker.Api.Data;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Auth;

/// <summary>
/// The data-protection key ring, stored in the same Postgres database as the sessions it protects.
///
/// <para>
/// <b>Why this exists at all.</b> <c>AddDataProtection()</c> with no persistence hands each process an ephemeral ring.
/// <c>-063</c> turned that into a user-visible fault without intending to: the antiforgery token is
/// <c>Protect(sessionId)</c>, so a token minted by one process is unreadable by any other *and by the same application
/// after a restart*. Measured on a single instance against one database — log in, write (201), restart the process,
/// replay the same token: <c>403 antiforgery</c>, while the session behind it still answers <c>200</c>, because the
/// session is a row and the key was memory. Every deploy logged every user out of their writes while leaving them
/// looking signed in. <c>-074</c>'s test is that restart expressed as two hosts, which is the form CI can run.
/// </para>
///
/// <para>
/// <b>The trade, stated because it is a hand-rolled replacement for a shipped package.</b>
/// <c>Microsoft.AspNetCore.DataProtection.EntityFrameworkCore</c> does this and is not installed: AC-15's evidence
/// counts API packages as runtime dependencies, so adding one is an owner-visible change rather than row work. What
/// this class does <em>not</em> do is revocation — it implements <see cref="IXmlRepository"/> and not
/// <c>IXmlRevoker</c> (nor <c>IDeletableXmlRepository</c>, which is public and would allow it), so expired keys
/// accumulate as rows instead of being deleted. The growth is one row per key
/// lifetime (90 days by default), which is nothing at this scale; the honest consequence is that a very long-lived
/// deployment keeps unreadable keys in a table. Expiry itself is unaffected: the framework reads
/// <c>expDate</c>/<c>activationDate</c> from the key XML, not from anything here.
/// </para>
/// </summary>
public sealed class PostgresKeyRing(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    /// <summary>
    /// A scope per operation, and that is load-bearing rather than defensive: this repository is a singleton because
    /// DataProtection holds one for the process, while <see cref="JobTrackerDb"/> is scoped. A context captured into a
    /// singleton would be used across concurrent requests — EF's documented failure, thrown as
    /// <c>InvalidOperationException: cannot be used concurrently</c> at whichever user happened to collide, which is a
    /// worse shape for a key ring than for anything else in this app.
    /// </summary>
    private JobTrackerDb Db() => scopeFactory.CreateScope().ServiceProvider.GetRequiredService<JobTrackerDb>();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        // No try/catch around the query, deliberately. A missing or unreadable table must stop the process at the first
        // token rather than fall back to minting an ephemeral key, because an ephemeral fallback reproduces exactly the
        // defect this class exists to remove — and it reproduces it *quietly*, on whichever instance happened to read
        // first, days after the deploy that caused it. Loud is the only useful direction for a bug that otherwise
        // arrives as a 403 in someone else's request.
        using var db = Db();
        var rows = db.DataProtectionKeys.AsNoTracking().OrderBy(k => k.Id).ToList();

        var elements = new List<XElement>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                elements.Add(XElement.Parse(row.XmlData));
            }
            catch (Exception)
            {
                // One corrupt row is not permission to fail the whole ring: the other keys are still valid, and a ring
                // that cannot load *any* key mints a new one, which is a degradation to the pre--074 behaviour rather
                // than an outage. Skipping is the smaller failure. This is also the only place a key is dropped on the
                // floor, which is why it says so.
            }
        }

        return elements;
    }

    public void StoreElement(XElement xmlKey, string friendlyName)
    {
        using var db = Db();
        db.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            XmlData = xmlKey.ToString(SaveOptions.DisableFormatting),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }
}
