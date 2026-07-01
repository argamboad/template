using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Template.Core.Abstractions;
using Template.Core.Entities;

namespace Template.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Tenant the global query filter scopes to. <see cref="Guid.Empty"/> when there is
    /// no current tenant — it matches no real (UUIDv7) row, so unauthenticated/tenant-less
    /// callers see no tenant-scoped data (fail closed).
    /// </summary>
    public Guid CurrentTenantId => currentTenant.TenantId ?? Guid.Empty;

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<UserMfa> UserMfa => Set<UserMfa>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // Transactional outbox — reliable, atomic side effects (ADR-007). Platform infra, not
    // ITenantScoped, so it is outside the global tenant query filter.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Inbox dedup ledger — idempotent inbound (webhook) deliveries (ADR-007). Platform infra,
    // not ITenantScoped.
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    // Billing subscription projection (ADR-006). ITenantScoped, so the global query filter scopes
    // it to the current tenant automatically.
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    // Append-only tenant audit trail (ADR-008). ITenantScoped (auto-filtered per tenant); writes are
    // append-only via AuditAppendOnlyInterceptor.
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // 🗑️ DELETE-ME: sample feature set (remove with the Features/Notes slice).
    public DbSet<Note> Notes => Set<Note>();

    // Tenant isolation is structural in BOTH directions: the global query filter (below)
    // scopes reads, and this interceptor scopes writes — stamping the current tenant onto
    // new ITenantScoped rows and refusing foreign-tenant writes. Registered here (not only
    // in DI) so every context — including ones constructed directly in tests — enforces it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(TenantStampingInterceptor.Instance, AuditAppendOnlyInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(t =>
        {
            t.HasKey(x => x.Id);
            t.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        builder.Entity<TenantMembership>(m =>
        {
            m.HasKey(x => x.Id);
            m.Property(x => x.Role).HasMaxLength(32).IsRequired();
            // One tenant per user at a time.
            m.HasIndex(x => x.UserId).IsUnique();
            m.HasIndex(x => x.TenantId);
            m.HasOne<Tenant>()
             .WithMany()
             .HasForeignKey(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            m.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TenantInvitation>(i =>
        {
            i.HasKey(x => x.Id);
            i.Property(x => x.InvitedEmail).HasMaxLength(256).IsRequired();
            i.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            i.Property(x => x.Status).HasMaxLength(32).IsRequired();
            i.HasOne<Tenant>()
             .WithMany()
             .HasForeignKey(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            // Unique: a hash identifies exactly one invitation (single-row credential lookup).
            i.HasIndex(x => x.TokenHash).IsUnique();
            i.HasIndex(x => new { x.TenantId, x.Status });
            // Ignore computed properties — derived, never stored.
            i.Ignore(x => x.IsExpired);
            i.Ignore(x => x.IsValid);
        });

        builder.Entity<User>(u =>
        {
            u.HasKey(x => x.Id);
            u.Property(x => x.Email).HasMaxLength(256).IsRequired();
            u.Property(x => x.DisplayName).HasMaxLength(256);
            u.Property(x => x.Locale).HasMaxLength(10);
            u.HasIndex(x => x.Email).IsUnique();
            u.HasMany(x => x.Logins)
             .WithOne(x => x.User)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserLogin>(l =>
        {
            l.HasKey(x => x.Id);
            l.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            l.Property(x => x.ProviderUserId).HasMaxLength(256).IsRequired();
            l.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            l.HasIndex(x => x.UserId);
        });

        builder.Entity<RefreshToken>(r =>
        {
            r.HasKey(x => x.Id);
            r.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            r.Property(x => x.IssuedFromIp).HasMaxLength(64).IsRequired();
            r.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            // Unique: a hash identifies exactly one refresh token (single-row credential lookup).
            r.HasIndex(x => x.TokenHash).IsUnique();
            r.HasIndex(x => x.UserId);
        });

        builder.Entity<LoginToken>(t =>
        {
            t.HasKey(x => x.Id);
            t.Property(x => x.Email).HasMaxLength(256).IsRequired();
            t.Property(x => x.CodeHash).HasMaxLength(256).IsRequired();
            t.Property(x => x.Purpose).HasMaxLength(32).IsRequired();
            t.HasIndex(x => new { x.Email, x.Purpose });
            // Derived, never stored.
            t.Ignore(x => x.IsConsumed);
            t.Ignore(x => x.IsExpired);
            t.Ignore(x => x.IsValid);
        });

        builder.Entity<UserMfa>(m =>
        {
            m.HasKey(x => x.Id);
            m.Property(x => x.EncryptedSecret).IsRequired();
            // One MFA row per user.
            m.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<MfaRecoveryCode>(c =>
        {
            c.HasKey(x => x.Id);
            c.Property(x => x.CodeHash).HasMaxLength(256).IsRequired();
            c.HasIndex(x => x.UserId);
        });

        builder.Entity<Notification>(n =>
        {
            n.HasKey(x => x.Id);
            n.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            n.Property(x => x.Title).HasMaxLength(256).IsRequired();
            n.Property(x => x.Metadata).HasColumnType("jsonb");
            // Per-user feed: list newest-first + unread counts.
            n.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        builder.Entity<NotificationPreference>(p =>
        {
            p.HasKey(x => x.Id);
            p.HasIndex(x => x.UserId).IsUnique(); // one preferences row per user
        });

        builder.Entity<OutboxMessage>(o =>
        {
            o.HasKey(x => x.Id);
            o.Property(x => x.Type).HasMaxLength(128).IsRequired();
            o.Property(x => x.Status).HasMaxLength(16).IsRequired();
            o.Property(x => x.Payload).IsRequired();
            o.Property(x => x.LastError).HasMaxLength(1000);
            // Drives the dispatcher claim query: pending + due, oldest first.
            o.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });

        builder.Entity<InboxMessage>(i =>
        {
            i.HasKey(x => x.Id);
            i.Property(x => x.Source).HasMaxLength(64).IsRequired();
            i.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
            // Dedup arbiter: one row per (source, key). The unique index is the ON CONFLICT target
            // that makes EfInbox.TryClaimAsync race-free.
            i.HasIndex(x => new { x.Source, x.IdempotencyKey }).IsUnique();
        });

        builder.Entity<Subscription>(s =>
        {
            s.HasKey(x => x.Id);
            s.Property(x => x.PlanKey).HasMaxLength(64).IsRequired();
            s.Property(x => x.Status).HasMaxLength(32).IsRequired();
            s.Property(x => x.StripeCustomerId).HasMaxLength(256);
            s.Property(x => x.StripeSubscriptionId).HasMaxLength(256);
            // At most one subscription per tenant.
            s.HasIndex(x => x.TenantId).IsUnique();
        });

        builder.Entity<AuditEvent>(a =>
        {
            a.HasKey(x => x.Id);
            a.Property(x => x.Action).HasMaxLength(128).IsRequired();
            a.Property(x => x.EntityType).HasMaxLength(128);
            a.Property(x => x.EntityId).HasMaxLength(256);
            a.Property(x => x.Metadata).HasColumnType("jsonb");
            // Read pattern: a tenant's trail, newest first.
            a.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        // 🗑️ DELETE-ME: sample feature (remove with the Features/Notes slice). Implements
        // ITenantScoped, so the global query filter below covers it automatically.
        builder.Entity<Note>(n =>
        {
            n.HasKey(x => x.Id);
            n.Property(x => x.Title).HasMaxLength(200).IsRequired();
            n.HasIndex(x => x.TenantId);
        });

        // Tenant isolation as a structural guarantee: every ITenantScoped entity is
        // filtered to CurrentTenantId by default, so feature/domain queries can't forget
        // to scope. Genuinely cross-tenant or pre-auth lookups opt out with
        // IgnoreQueryFilters(). This is a query-time filter only — no schema change.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
                ApplyTenantFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [builder]);
        }
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantScoped
        => builder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
