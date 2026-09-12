namespace JobTracker.Api.Data;

/// <summary>
/// One data-protection key, as persisted between processes. EF maps it to <c>data_protection_keys</c>.
/// </summary>
/// <remarks>
/// <para>
/// The shape is deliberately close to what <c>Microsoft.AspNetCore.DataProtection.EntityFrameworkCore</c> ships, and
/// deliberately is not that package: AC-15's ratified evidence counts the API's <c>PackageReference</c> entries
/// alongside the frontend's runtime dependencies, so adding a NuGet package to fix this would move the number a
/// verified acceptance criterion reports. That is a decision to bring to the owner, not one to take inside a row about
/// a defect. So the storage here is ~60 lines against a table, and the trade is stated in <c>PostgresKeyRing</c>.
/// </para>
/// <para>
/// <see cref="XmlData"/> holds a single <c>&lt;key&gt;</c> element as exported by the framework — including its own
/// <c>expDate</c>/<c>activationDate</c> attributes and the <c>&lt;pct&gt;</c>/<c>&lt;enc&gt;</c> material. Those live
/// inside the XML on purpose: expiry is decided by the framework when it reads the ring, not by a column this
/// application would have to keep in step with a policy it does not own.
/// </para>
/// </remarks>
public sealed class DataProtectionKey
{
    public int Id { get; set; }

    /// <summary>
    /// The framework's name for the key, e.g. <c>key-928c90a3-…</c>. Stored so a human reading the table can tell which
    /// key the ring considered active, and so <c>StoreElement</c> has something idempotent to write. Not unique: a
    /// duplicate under load is a nuisance to a reviewer, not a correctness fault, and enforcing uniqueness here would
    /// make first-boot contention between two instances a failure mode where today it is a redundant row.
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;

    public string XmlData { get; set; } = string.Empty;

    /// <summary>When <em>this process</em> wrote the row. The key's own dates are in <see cref="XmlData"/>.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
