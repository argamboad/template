namespace Template.Api.Services;

/// <summary>
/// Hashes tokens for secure storage.
/// Separated from generation to allow algorithm substitution.
/// </summary>
public interface ITokenHasher
{
    string HashToken(string token);
}
