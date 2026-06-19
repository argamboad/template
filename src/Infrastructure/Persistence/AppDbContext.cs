using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;

namespace Template.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();

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
            i.HasIndex(x => x.TokenHash);
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
            r.HasIndex(x => x.TokenHash);
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
    }
}
