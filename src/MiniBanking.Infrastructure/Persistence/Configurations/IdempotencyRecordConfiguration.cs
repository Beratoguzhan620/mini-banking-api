using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // Keys are scoped to a customer: two customers may pick the same key without colliding.
        builder.HasKey(r => new { r.CustomerId, r.Key });
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(r => r.ResponseContentType).HasMaxLength(100);
        builder.Property(r => r.ResponseLocation).HasMaxLength(200);
        builder.Property(r => r.ResponseBody).HasColumnType("text");

        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.CreatedAt);
    }
}
