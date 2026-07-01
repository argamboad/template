using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Template.Api.Services;

/// <summary>
/// Mints and reads the short-lived, <b>signed</b> MFA challenge issued after primary auth when a user
/// has MFA enabled (MFA-2, ADR-012). The token binds the user + the original provider + whether the
/// login was native, with an expiry — signed with Data Protection (keys in the DB), so an expired or
/// tampered challenge fails closed. It carries no secret; it only says "this user still owes a second
/// factor for this login".
/// </summary>
public interface IMfaChallengeService
{
    string Mint(Guid userId, string provider, bool native);
    bool TryRead(string challenge, out Guid userId, out string provider, out bool native);
}

public sealed class MfaChallengeService(IDataProtectionProvider dataProtection) : IMfaChallengeService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("Template.Mfa.Challenge.v1").ToTimeLimitedDataProtector();

    public string Mint(Guid userId, string provider, bool native) =>
        _protector.Protect($"{userId:D}|{provider}|{(native ? "1" : "0")}", Lifetime);

    public bool TryRead(string challenge, out Guid userId, out string provider, out bool native)
    {
        userId = default;
        provider = "";
        native = false;
        if (string.IsNullOrEmpty(challenge))
            return false;

        try
        {
            var parts = _protector.Unprotect(challenge).Split('|');
            if (parts.Length != 3 || !Guid.TryParse(parts[0], out userId))
                return false;
            provider = parts[1];
            native = parts[2] == "1";
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
