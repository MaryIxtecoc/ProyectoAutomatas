using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class CredentialDesignConfiguration : IEntityTypeConfiguration<CredentialDesign>
{
    public void Configure(EntityTypeBuilder<CredentialDesign> builder)
    {
        builder.ToTable("credential_designs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Template).HasColumnName("template").IsRequired();
        builder.Property(x => x.BackgroundColor).HasColumnName("background_color");
        builder.Property(x => x.AccentColor).HasColumnName("accent_color");
        builder.Property(x => x.TextColor).HasColumnName("text_color");
        builder.Property(x => x.PhotoStyle).HasColumnName("photo_style");
        builder.Property(x => x.QrStyle).HasColumnName("qr_style");
        builder.Property(x => x.Decorations).HasColumnName("decorations").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId);

        builder.HasIndex(x => x.UserId);
    }
}
