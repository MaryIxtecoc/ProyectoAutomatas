using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class UserBiometricConfiguration : IEntityTypeConfiguration<UserBiometric>
{
    public void Configure(EntityTypeBuilder<UserBiometric> builder)
    {
        builder.ToTable("user_biometrics");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.FaceDescriptor)
            .HasColumnName("face_descriptor")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithOne(x => x.Biometric)
            .HasForeignKey<UserBiometric>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}