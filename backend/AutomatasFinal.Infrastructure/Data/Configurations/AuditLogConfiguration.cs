using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.TargetUserId)
            .HasColumnName("target_user_id");

        builder.Property(x => x.Success)
            .HasColumnName("success");

        builder.Property(x => x.Action)
            .HasColumnName("action")
            .IsRequired();

        builder.Property(x => x.Details)
            .HasColumnName("details")
            .HasColumnType("jsonb");

        builder.Property(x => x.IpAddress)
            .HasColumnName("ip_address")
            .HasColumnType("inet");

        builder.Property(x => x.UserAgent)
            .HasColumnName("user_agent");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.TargetUserId);
        builder.HasIndex(x => x.CreatedAt);
    }
}