using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>The provisioning URI + secret returned once when enrollment begins (MFA-1, ADR-012).</summary>
public sealed record MfaEnrollment(string ProvisioningUri, string Secret);

public enum MfaConfirmResult { Enabled, InvalidCode, NotEnrolled }
public enum MfaDisableResult { Disabled, InvalidCode, NotEnabled }

/// <summary>
/// Authenticator-app TOTP MFA (MFA-1, ADR-012). The secret is stored <b>encrypted</b> (Data
/// Protection) and only ever leaves as the enrollment provisioning URI; recovery codes are
/// <b>hashed + single-use</b> (<see cref="ITokenHasher"/>). Enable/disable both require a valid code
/// (prove possession). <see cref="VerifyAsync"/> is the check the login step-up (MFA-2) calls.
/// </summary>
public interface IMfaService
{
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Starts enrollment (not yet enabled). Null if the user doesn't exist.</summary>
    Task<MfaEnrollment?> BeginEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Confirms enrollment with a code → enables MFA and returns fresh one-time recovery codes.</summary>
    Task<(MfaConfirmResult Result, IReadOnlyList<string> RecoveryCodes)> ConfirmEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    /// <summary>Disables MFA (requires a valid code) and wipes the secret + recovery codes.</summary>
    Task<MfaDisableResult> DisableAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    /// <summary>True if the code is a valid TOTP or an unused recovery code (which it then consumes).</summary>
    Task<bool> VerifyAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}

public sealed class MfaService(
    IRepository<UserMfa> mfa,
    IRepository<MfaRecoveryCode> recoveryCodes,
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    ITokenGenerator tokenGenerator,
    ITokenHasher hasher,
    TimeProvider clock) : IMfaService
{
    private const string Issuer = "Template"; // rebrandable — appears in the authenticator app
    private const int RecoveryCodeCount = 10;
    private static readonly VerificationWindow Window = new(previous: 1, future: 1); // ±1 step for clock skew
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Template.Mfa.Secret.v1");

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await mfa.Query().AnyAsync(m => m.UserId == userId && m.Enabled, cancellationToken);

    public async Task<MfaEnrollment?> BeginEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return null;

        // Fresh start: clear any prior (unconfirmed or superseded) state for this user.
        await recoveryCodes.Query().Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await mfa.Query().Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        await mfa.AddAsync(new UserMfa
        {
            UserId = userId,
            EncryptedSecret = _protector.Protect(secret),
            Enabled = false,
            EnrolledAt = clock.GetUtcNow(),
        }, cancellationToken);
        await mfa.SaveChangesAsync(cancellationToken);

        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        var issuer = Uri.EscapeDataString(Issuer);
        var uri = $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&digits=6&period=30";
        return new MfaEnrollment(uri, secret);
    }

    public async Task<(MfaConfirmResult, IReadOnlyList<string>)> ConfirmEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var record = await mfa.Query().FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (record is null)
            return (MfaConfirmResult.NotEnrolled, []);
        if (!TryVerifyTotp(record, code, out _)) // enrollment proof — not a login, so no anti-replay step recorded
            return (MfaConfirmResult.InvalidCode, []);

        record.Enabled = true;
        mfa.Update(record);

        // Issue a fresh recovery-code set (raw shown once; only hashes persisted).
        await recoveryCodes.Query().Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var raw = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var codeRaw = tokenGenerator.GenerateToken();
            raw.Add(codeRaw);
            await recoveryCodes.AddAsync(new MfaRecoveryCode { UserId = userId, CodeHash = hasher.HashToken(codeRaw) }, cancellationToken);
        }
        await mfa.SaveChangesAsync(cancellationToken);
        return (MfaConfirmResult.Enabled, raw);
    }

    public async Task<MfaDisableResult> DisableAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var enabled = await mfa.Query().AnyAsync(m => m.UserId == userId && m.Enabled, cancellationToken);
        if (!enabled)
            return MfaDisableResult.NotEnabled;
        if (!await VerifyAsync(userId, code, cancellationToken))
            return MfaDisableResult.InvalidCode;

        await recoveryCodes.Query().Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await mfa.Query().Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return MfaDisableResult.Disabled;
    }

    public async Task<bool> VerifyAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var record = await mfa.Query().FirstOrDefaultAsync(m => m.UserId == userId && m.Enabled, cancellationToken);
        if (record is null)
            return false;

        if (TryVerifyTotp(record, code, out var timeStep))
        {
            // Anti-replay (v2 audit LOGIC-S1): a TOTP is valid for a ~90s window (±1 step), so the same
            // code must not mint more than one session. Reject a step already accepted (or an older one),
            // and record the accepted step so the next replay within the window fails.
            if (record.LastVerifiedTimeStep is { } last && timeStep <= last)
                return false;

            record.LastVerifiedTimeStep = timeStep;
            mfa.Update(record);
            await mfa.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Otherwise try a single-use recovery code.
        var hash = hasher.HashToken(code.Trim());
        var recovery = await recoveryCodes.Query()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null, cancellationToken);
        if (recovery is null)
            return false;

        recovery.UsedAt = clock.GetUtcNow();
        recoveryCodes.Update(recovery);
        await recoveryCodes.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>True if <paramref name="code"/> is a valid TOTP; <paramref name="timeStep"/> is the matched step.</summary>
    private bool TryVerifyTotp(UserMfa record, string code, out long timeStep)
    {
        timeStep = 0;
        string secret;
        try { secret = _protector.Unprotect(record.EncryptedSecret); }
        catch (CryptographicException) { return false; }

        return new Totp(Base32Encoding.ToBytes(secret)).VerifyTotp(code.Trim(), out timeStep, Window);
    }
}
