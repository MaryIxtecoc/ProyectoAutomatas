using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class AnalysisJobConfiguration : IEntityTypeConfiguration<AnalysisJob>
{
    public void Configure(EntityTypeBuilder<AnalysisJob> builder)
    {
        builder.ToTable("analysis_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .IsRequired();

        builder.Property(x => x.FileStoragePath)
            .HasColumnName("file_storage_path")
            .IsRequired();

        builder.Property(x => x.Language)
            .HasColumnName("language")
            .HasColumnType("analysis_language")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("analysis_status")
            .IsRequired();

        builder.Property(x => x.ReportRequested)
            .HasColumnName("report_requested")
            .IsRequired();

        builder.Property(x => x.ReportPdfPath)
            .HasColumnName("report_pdf_path");

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(x => x.UploadedAt)
            .HasColumnName("uploaded_at")
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.AnalysisJobs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
    }
}
