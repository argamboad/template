using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Billing;
using Template.Infrastructure.Billing;

namespace Template.Api.Tests.Billing;

/// <summary>
/// BILLING-2 checkout orchestration (ADR-006): plan validation + provider invocation. Pure unit test
/// (no DB) via <see cref="FakeBillingProvider"/> — the owner gate is a controller concern, covered by
/// <see cref="BillingControllerTests"/>.
/// </summary>
public class BillingServiceTests
{
    [Fact]
    public async Task ValidPaidPlan_CreatesSession_CarryingTenantAndPlan()
    {
        var fake = new FakeBillingProvider();
        var service = new BillingService(fake, new TestAppSettings());
        var tenantId = Guid.CreateVersion7();

        var result = await service.CreateCheckoutAsync(tenantId, PlanKeys.Pro);

        Assert.Equal(CheckoutOutcome.Created, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Url));
        var request = Assert.Single(fake.Requests);
        Assert.Equal(tenantId, request.TenantId);
        Assert.Equal(PlanKeys.Pro, request.PlanKey);
    }

    [Theory]
    [InlineData(PlanKeys.Free)] // Free isn't purchasable
    [InlineData("gold")]        // unknown plan
    [InlineData(null)]
    public async Task UnknownOrFreePlan_IsRejected_NoSession(string? planKey)
    {
        var fake = new FakeBillingProvider();
        var service = new BillingService(fake, new TestAppSettings());

        var result = await service.CreateCheckoutAsync(Guid.CreateVersion7(), planKey);

        Assert.Equal(CheckoutOutcome.InvalidPlan, result.Outcome);
        Assert.Empty(fake.Requests);
    }
}
