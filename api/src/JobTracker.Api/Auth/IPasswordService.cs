namespace JobTracker.Api.Auth;

/// <summary>
/// Hashes and verifies passwords. The seam <c>BEHAVIOR-047</c> was written against.
///
/// Split out from <see cref="PasswordService"/> after a Green-phase rewrite deleted this declaration while rewriting that
/// file — which is worth recording as the small trap it is: an interface and its implementation sharing a file makes
/// "replace the implementation" silently replace the contract too, and the resulting <c>CS0246</c> points at the
/// *declaration site*, not at the file that vanished. The error named a column, and the column was the answer.
/// </summary>
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string storedHash, string candidate);
}
