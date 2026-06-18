using System.Security.Cryptography;
using System.Text;

namespace Template.Api.Services;

/// <summary>
/// Hashes tokens using SHA256.
/// </summary>
public class TokenHasher : ITokenHasher
{
    public string HashToken(string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }
}
