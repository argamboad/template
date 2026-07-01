using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OtpNet;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Mfa;

/// <summary>
/// MFA-2 (ADR-012): the login step-up. When MFA is off, a session is issued directly; when on, primary
/// auth yields a challenge (no session) that is only redeemable with a valid TOTP/recovery code, and
/// the original native flag is preserved. Postgres-backed (real session issuance).
/// </summary>
[Collection(PostgresCollection.Name)]
public class MfaLoginServiceTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task NoMfa_IssuesSessionDirectly()
    {
        await using var db = Fixture.CreateContext();
        var (login, _, user) = await BuildWithUserAsync(db);

        var (session, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);

        Assert.Null(challenge);
        Assert.NotNull(session);
    }

    [Fact]
    public async Task MfaEnabled_ReturnsChallenge_NotSession()
    {
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        await EnableMfaAsync(mfa, user.Id);

        var (session, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);

        Assert.NotNull(challenge);
        Assert.Null(session);
    }

    [Fact]
    public async Task VerifyChallenge_ValidCode_IssuesSession()
    {
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        var secret = await EnableMfaAsync(mfa, user.Id);
        var (_, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);

        var outcome = await login.VerifyChallengeAsync(challenge!, CurrentCode(secret), "127.0.0.1");

        Assert.NotNull(outcome);
        Assert.False(outcome!.Native);
        Assert.NotNull(outcome.Session);
    }

    [Fact]
    public async Task VerifyChallenge_WrongCode_ReturnsNull()
    {
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        await EnableMfaAsync(mfa, user.Id);
        var (_, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);

        Assert.Null(await login.VerifyChallengeAsync(challenge!, "000000", "127.0.0.1"));
    }

    [Fact]
    public async Task VerifyChallenge_TamperedChallenge_ReturnsNull()
    {
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        var secret = await EnableMfaAsync(mfa, user.Id);
        var (_, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);

        Assert.Null(await login.VerifyChallengeAsync("AA" + challenge![2..], CurrentCode(secret), "127.0.0.1"));
    }

    [Fact]
    public async Task VerifyChallenge_ReplayedChallengeAndCode_IsRejectedOnSecondUse()
    {
        // v2 audit LOGIC-S1: one captured {challenge, code} must mint at most one session — the challenge
        // is single-use and the TOTP timestep is anti-replayed.
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        var secret = await EnableMfaAsync(mfa, user.Id);
        var (_, challenge) = await login.CompleteOrChallengeAsync(user, "otp", "127.0.0.1", native: false);
        var code = CurrentCode(secret);

        var first = await login.VerifyChallengeAsync(challenge!, code, "127.0.0.1");
        var replay = await login.VerifyChallengeAsync(challenge!, code, "127.0.0.1");

        Assert.NotNull(first);   // first redemption issues a session
        Assert.Null(replay);     // identical replay refused
    }

    [Fact]
    public async Task VerifyChallenge_PreservesNativeFlag()
    {
        await using var db = Fixture.CreateContext();
        var (login, mfa, user) = await BuildWithUserAsync(db);
        var secret = await EnableMfaAsync(mfa, user.Id);
        var (_, challenge) = await login.CompleteOrChallengeAsync(user, "google", "127.0.0.1", native: true);

        var outcome = await login.VerifyChallengeAsync(challenge!, CurrentCode(secret), "127.0.0.1");

        Assert.NotNull(outcome);
        Assert.True(outcome!.Native);
    }

    // --- helpers ---

    private async Task<(MfaLoginService login, MfaService mfa, User user)> BuildWithUserAsync(AppDbContext db)
    {
        var mfa = new MfaService(
            new EfRepository<UserMfa>(db), new EfRepository<MfaRecoveryCode>(db), new UserRepository(db),
            new EphemeralDataProtectionProvider(), new TokenGenerator(), new TokenHasher(), TimeProvider.System);
        var challenges = new MfaChallengeService(new EphemeralDataProtectionProvider(), new MemoryCache(new MemoryCacheOptions()));
        var harness = new ServiceHarness(db);
        var login = new MfaLoginService(mfa, challenges, harness.SessionService(), harness.UserService());

        var user = await SeedUserAsync(db);
        return (login, mfa, user);
    }

    private static async Task<string> EnableMfaAsync(MfaService mfa, Guid userId)
    {
        var secret = (await mfa.BeginEnrollmentAsync(userId))!.Secret;
        await mfa.ConfirmEnrollmentAsync(userId, CurrentCode(secret));
        return secret;
    }

    private static string CurrentCode(string base32Secret) =>
        new Totp(Base32Encoding.ToBytes(base32Secret)).ComputeTotp();

    private async Task<User> SeedUserAsync(AppDbContext db)
    {
        var tenantId = Guid.CreateVersion7();
        var user = new User { Id = Guid.CreateVersion7(), Email = $"u-{Guid.NewGuid():N}@x.com" };
        db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "T" });
        db.Set<User>().Add(user);
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = user.Id, Role = TenantRoles.Owner });
        await db.SaveChangesAsync();
        return user;
    }
}
