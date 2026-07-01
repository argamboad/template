using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Notify;

/// <summary>
/// NOTIFY-2 (ADR-013): NotifyAsync fans out to in-app + email per the user's preferences (default both
/// on); the email copy goes through <see cref="IEmailSender"/> (the outbox-backed sender in prod). A
/// capturing email sender lets the test assert which channels fired. Postgres-backed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class NotificationFanOutTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Default_FansOutToBothChannels()
    {
        await using var db = Fixture.CreateContext();
        var email = new CapturingEmailSender();
        var service = NewService(db, email);
        var userId = await SeedUserAsync(db);

        await service.NotifyAsync(userId, "k", "Hello", "body");
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Set<Notification>().CountAsync(n => n.UserId == userId));
        Assert.Single(email.Sent);
        Assert.Equal("Hello", email.Sent[0].Subject);
    }

    [Fact]
    public async Task EmailOff_CreatesInApp_SendsNoEmail()
    {
        await using var db = Fixture.CreateContext();
        var email = new CapturingEmailSender();
        var service = NewService(db, email);
        var userId = await SeedUserAsync(db);

        await service.SetPreferencesAsync(userId, inApp: true, email: false);
        await service.NotifyAsync(userId, "k", "t", "b");
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Set<Notification>().CountAsync(n => n.UserId == userId));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task InAppOff_SendsEmail_NoInApp()
    {
        await using var db = Fixture.CreateContext();
        var email = new CapturingEmailSender();
        var service = NewService(db, email);
        var userId = await SeedUserAsync(db);

        await service.SetPreferencesAsync(userId, inApp: false, email: true);
        await service.NotifyAsync(userId, "k", "t", "b");
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Set<Notification>().CountAsync(n => n.UserId == userId));
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Preferences_DefaultOn_WhenNeverSet()
    {
        await using var db = Fixture.CreateContext();
        var service = NewService(db);
        var prefs = await service.GetPreferencesAsync(Guid.CreateVersion7());
        Assert.True(prefs.InApp);
        Assert.True(prefs.Email);
    }

    [Fact]
    public async Task SetPreferences_Persists_AndIsUpsertable()
    {
        await using var db = Fixture.CreateContext();
        var service = NewService(db);
        var userId = await SeedUserAsync(db);

        await service.SetPreferencesAsync(userId, inApp: false, email: false);
        var first = await service.GetPreferencesAsync(userId);
        Assert.False(first.InApp);
        Assert.False(first.Email);

        await service.SetPreferencesAsync(userId, inApp: true, email: false); // update existing row
        var second = await service.GetPreferencesAsync(userId);
        Assert.True(second.InApp);
        Assert.False(second.Email);
        Assert.Equal(1, await db.Set<NotificationPreference>().CountAsync(p => p.UserId == userId)); // one row
    }

    // --- helpers ---

    private static NotificationService NewService(AppDbContext db, IEmailSender? email = null) =>
        new(new EfRepository<Notification>(db), new EfRepository<NotificationPreference>(db),
            new UserRepository(db), email ?? new CapturingEmailSender(), TimeProvider.System);

    private async Task<Guid> SeedUserAsync(AppDbContext db)
    {
        var user = new User { Id = Guid.CreateVersion7(), Email = $"u-{Guid.NewGuid():N}@x.com" };
        db.Set<User>().Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}

internal sealed class CapturingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string htmlBody,
        IReadOnlyList<EmailInlineImage>? inlineImages = null, CancellationToken cancellationToken = default)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
