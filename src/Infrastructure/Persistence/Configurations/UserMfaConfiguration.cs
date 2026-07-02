using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Core.Entities;

namespace Template.Infrastructure.Persistence.Configurations;

public class UserMfaConfiguration : IEntityTypeConfiguration<UserMfa>
{
    public void Configure(EntityTypeBuilder<UserMfa> m)
    {
        m.HasKey(x => x.Id);
        m.Property(x => x.EncryptedSecret).IsRequired();
        // One MFA row per user.
        m.HasIndex(x => x.UserId).IsUnique();
    }
}
