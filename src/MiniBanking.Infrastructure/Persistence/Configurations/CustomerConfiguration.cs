using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(Customer.FullNameMaxLength).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(c => c.Email).IsUnique();
    }
}
