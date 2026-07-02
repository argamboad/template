using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Core.Entities;

namespace Template.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> t)
    {
        t.HasKey(x => x.Id);
        t.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}
