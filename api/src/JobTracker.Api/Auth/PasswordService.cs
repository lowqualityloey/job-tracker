namespace JobTracker.Api.Auth;

/// <summary>
/// Hashes and verifies passwords. <c>BEHAVIOR-047</c>.
///
/// **This is the Red-phase seam: every member throws.** It is not a design placeholder — it is the shape the test needed
/// to compile against so that the failure is "no implementation exists" rather than "no such type", which is a compile
/// error and tells us nothing about behaviour. Green replaces these bodies and nothing else.
/// </summary>
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string storedHash, string candidate);
}

/// <inheritdoc />
public sealed class PasswordService : IPasswordService
{
    public string Hash(string password) =>
        throw new NotSupportedException("BEHAVIOR-047 Red: no implementation yet.");

    public bool Verify(string storedHash, string candidate) =>
        throw new NotSupportedException("BEHAVIOR-047 Red: no implementation yet.");
}
