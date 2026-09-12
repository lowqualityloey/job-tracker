namespace JobTracker.Api.Data;

/// <summary>
/// A server-side session (<c>DECISION-m4-auth-002</c>). Spec §3: <c>sessions(id, user_id, created_at, expires_at,
/// revoked_at)</c>.
///
/// <b>The id is the credential.</b> The cookie carries this <see cref="Id"/> verbatim, so possession of the value <i>is</i>
/// the authentication — which is why it is a fresh <c>Guid</c> per login rather than a sequential key, and why
/// <c>BEHAVIOR-052</c> regenerates it at login instead of reusing a pre-login value (session fixation).
///
/// <b>Revocation lives here, not in the cookie.</b> <c>DELETE</c> from the client's jar is a courtesy;
/// <see cref="RevokedAt"/> is what makes a stolen cookie stop working, and it is the entire reason the spec chose server
/// -side sessions over JWT. A revocation that only clears a header would be theatre, and <c>-054</c> checks this column
/// through <c>psql</c> rather than inferring it from a response.
/// </summary>
public sealed class Session
{
    public required Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
