using System.Security.Cryptography;
using Template.Api.Configuration;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

public enum OtpStatus { Success, Invalid, Expired, TooManyAttempts }

public record OtpResult(OtpStatus Status, User? User);

/// <summary>
/// Issues and redeems passwordless credentials: magic-link tokens (long random
/// values delivered as a URL) and OTP codes (short numeric values). Credentials
/// are single-use, time-limited, and stored only as hashes. The account is
/// resolved/created at redemption — issuing never creates an account, so a typo'd
/// or probed email leaves no trace.
/// </summary>
public interface IPasswordlessService
{
    /// <summary>Creates a magic-link token for the email and returns the raw value for the URL.</summary>
    Task<string> IssueMagicLinkTokenAsync(string email);

    /// <summary>Validates and consumes a magic-link token, returning the account, or null if invalid.</summary>
    Task<User?> RedeemMagicLinkAsync(string email, string token);

    /// <summary>Creates an OTP code for the email and returns the raw code to be emailed.</summary>
    Task<string> IssueOtpAsync(string email);

    /// <summary>Validates and consumes an OTP code, tracking attempts and locking out on abuse.</summary>
    Task<OtpResult> RedeemOtpAsync(string email, string code);
}

public class PasswordlessService(
    ILoginTokenRepository repository,
    IUserService userService,
    ITokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IPasswordlessSettings settings) : IPasswordlessService
{
    public async Task<string> IssueMagicLinkTokenAsync(string email)
    {
        email = Normalize(email);
        await repository.InvalidateActiveAsync(email, LoginTokenPurpose.MagicLink);

        var raw = tokenGenerator.GenerateToken();
        await repository.AddAsync(NewToken(email, LoginTokenPurpose.MagicLink, raw, settings.MagicLinkLifespanMinutes));
        return raw;
    }

    public async Task<User?> RedeemMagicLinkAsync(string email, string token)
    {
        email = Normalize(email);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var hash = tokenHasher.HashToken(token);
        var record = await repository.GetActiveByHashAsync(email, LoginTokenPurpose.MagicLink, hash);
        if (record is null)
            return null;

        record.ConsumedAt = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(record);

        return await userService.GetOrCreateByEmailAsync(email);
    }

    public async Task<string> IssueOtpAsync(string email)
    {
        email = Normalize(email);
        await repository.InvalidateActiveAsync(email, LoginTokenPurpose.Otp);

        var code = GenerateNumericCode(settings.OtpLength);
        await repository.AddAsync(NewToken(email, LoginTokenPurpose.Otp, code, settings.OtpLifespanMinutes));
        return code;
    }

    public async Task<OtpResult> RedeemOtpAsync(string email, string code)
    {
        email = Normalize(email);
        if (string.IsNullOrWhiteSpace(code))
            return new OtpResult(OtpStatus.Invalid, null);

        var record = await repository.GetLatestActiveAsync(email, LoginTokenPurpose.Otp);
        if (record is null)
            return new OtpResult(OtpStatus.Expired, null); // none active → expired or never issued

        // Constant-time-ish comparison via hash equality.
        if (tokenHasher.HashToken(code) == record.CodeHash)
        {
            record.ConsumedAt = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(record);
            var user = await userService.GetOrCreateByEmailAsync(email);
            return new OtpResult(OtpStatus.Success, user);
        }

        // Wrong code — count the attempt and lock the code out after the limit.
        record.AttemptCount++;
        if (record.AttemptCount >= settings.OtpMaxAttempts)
        {
            record.ConsumedAt = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(record);
            return new OtpResult(OtpStatus.TooManyAttempts, null);
        }

        await repository.UpdateAsync(record);
        return new OtpResult(OtpStatus.Invalid, null);
    }

    private LoginToken NewToken(string email, string purpose, string raw, int lifespanMinutes) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = email,
        CodeHash = tokenHasher.HashToken(raw),
        Purpose = purpose,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(lifespanMinutes),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string GenerateNumericCode(int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(digits);
    }
}
