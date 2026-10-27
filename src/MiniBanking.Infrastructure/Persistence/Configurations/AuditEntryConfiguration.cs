using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Audit;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.TargetId).HasMaxLength(64);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        // No foreign keys on purpose: the trail must survive whatever happens to the rows it mentions.
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => e.ActorId);
        builder.HasIndex(e => e.CorrelationId);
    }
}
