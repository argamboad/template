using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

/// <summary>
/// Passwordless credential lifecycle: magic-link single-use + expiry, and OTP success,
/// wrong-code rejection, attempt lockout, and expiry.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PasswordlessServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task MagicLink_IssueThenRedeem_SignsInAndIsSingleUse()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).PasswordlessService();

        var token = await sut.IssueMagicLinkTokenAsync("ml@example.com");

        var user = await sut.RedeemMagicLinkAsync("ml@example.com", token);
        Assert.NotNull(user);
        Assert.True(user!.EmailVerified);

        // Second use is rejected — the token was consumed.
        Assert.Null(await sut.RedeemMagicLinkAsync("ml@example.com", token));
    }

    [Fact]
    public async Task MagicLink_Expired_IsRejected()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).PasswordlessService();

        var token = await sut.IssueMagicLinkTokenAsync("exp@example.com");
        await ExpireTokensAsync(db, "exp@example.com");

        Assert.Null(await sut.RedeemMagicLinkAsync("exp@example.com", token));
    }

    [Fact]
    public async Task Otp_CorrectCode_Succeeds()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).PasswordlessService();

        var code = await sut.IssueOtpAsync("otp@example.com");
        var result = await sut.RedeemOtpAsync("otp@example.com", code);

        Assert.Equal(OtpStatus.Success, result.Status);
        Assert.NotNull(result.User);
    }

    [Fact]
    public async Task Otp_WrongCode_LocksOutAfterMaxAttempts()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var settings = new TestPasswordlessSettings { OtpMaxAttempts = 3 };
        var sut = new ServiceHarness(db).PasswordlessService(settings);

        var code = await sut.IssueOtpAsync("lock@example.com");
        var wrong = WrongVariant(code);

        Assert.Equal(OtpStatus.Invalid, (await sut.RedeemOtpAsync("lock@example.com", wrong)).Status);
        Assert.Equal(OtpStatus.Invalid, (await sut.RedeemOtpAsync("lock@example.com", wrong)).Status);
        Assert.Equal(OtpStatus.TooManyAttempts, (await sut.RedeemOtpAsync("lock@example.com", wrong)).Status);

        // Locked out: even the correct code no longer works (the record was consumed).
        Assert.Equal(OtpStatus.Expired, (await sut.RedeemOtpAsync("lock@example.com", code)).Status);
    }

    [Fact]
    public async Task Otp_Expired_ReturnsExpired()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).PasswordlessService();

        var code = await sut.IssueOtpAsync("otpexp@example.com");
        await ExpireTokensAsync(db, "otpexp@example.com");

        Assert.Equal(OtpStatus.Expired, (await sut.RedeemOtpAsync("otpexp@example.com", code)).Status);
    }

    // Drive the stored token's expiry into the past — the "active" repo queries filter
    // on ExpiresAt in SQL, so the redeem then sees nothing active.
    private static async Task ExpireTokensAsync(AppDbContext db, string email)
    {
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.LoginTokens.Where(t => t.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, past));
    }

    // A code guaranteed to differ from the issued one (flips the first digit).
    private static string WrongVariant(string code) =>
        (code[0] == '0' ? '1' : '0') + code[1..];
}
