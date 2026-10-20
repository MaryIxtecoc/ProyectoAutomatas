using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class ProfileChangeRequestConfiguration : IEntityTypeConfiguration<ProfileChangeRequest>
{
    public void Configure(EntityTypeBuilder<ProfileChangeRequest> builder)
    {
        builder.ToTable("profile_change_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.RequestedEmail).HasColumnName("requested_email");
        builder.Property(x => x.RequestedBirthDate).HasColumnName("requested_birth_date");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId);

        builder.HasOne(x => x.Reviewer)
            .WithMany()
            .HasForeignKey(x => x.ReviewedBy);

        builder.HasIndex(x => new { x.UserId, x.Status });
    }
}
