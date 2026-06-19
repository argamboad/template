using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

/// <summary>JWT issuance/validation — every claim round-trips, including the new tenant_id.</summary>
public class JwtTokenServiceTests
{
    private static JwtTokenService Sut() => new(new TestJwtSettings(), NullLogger<JwtTokenService>.Instance);

    [Fact]
    public void IssueAndValidate_RoundTripsAllClaims()
    {
        var sut = Sut();
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        var jwt = sut.IssueAccessToken(userId, "u@example.com", "google", "Display", "Acme", "es", tenantId);
        var principal = sut.ValidateToken(jwt);

        Assert.NotNull(principal);
        Assert.Equal(userId.ToString(), principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("u@example.com", principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("google", principal.FindFirst("provider")?.Value);
        Assert.Equal("Display", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("Acme", principal.FindFirst("tenant_name")?.Value);
        Assert.Equal("es", principal.FindFirst("locale")?.Value);
        Assert.Equal(tenantId.ToString(), principal.FindFirst(JwtTokenService.TenantIdClaim)?.Value);
    }

    [Fact]
    public void ValidateToken_Garbage_ReturnsNull() =>
        Assert.Null(Sut().ValidateToken("not-a-jwt"));
}

/// <summary>Refresh-token rotation: a revoked (rotated-out) token no longer validates.</summary>
[Collection(PostgresCollection.Name)]
public class RefreshTokenServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task IssueThenValidate_ReturnsToken()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).RefreshTokenService();
        var userId = Guid.CreateVersion7();

        var issued = await sut.IssueRefreshTokenAsync(userId, "127.0.0.1", "google");
        var validated = await sut.ValidateRefreshTokenAsync(issued.RawToken);

        Assert.NotNull(validated);
        Assert.Equal(userId, validated!.UserId);
    }

    [Fact]
    public async Task RevokedToken_NoLongerValidates()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).RefreshTokenService();

        var issued = await sut.IssueRefreshTokenAsync(Guid.CreateVersion7(), "127.0.0.1", "google");
        await sut.RevokeRefreshTokenAsync(issued.Token.Id); // rotation revokes the used token

        Assert.Null(await sut.ValidateRefreshTokenAsync(issued.RawToken));
    }

    [Fact]
    public async Task ValidateRefreshToken_Garbage_ReturnsNull()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var sut = new ServiceHarness(db).RefreshTokenService();

        Assert.Null(await sut.ValidateRefreshTokenAsync("nope"));
    }
}
