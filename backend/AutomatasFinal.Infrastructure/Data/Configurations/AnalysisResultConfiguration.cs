using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomatasFinal.Infrastructure.Data.Configurations;

public class AnalysisResultConfiguration : IEntityTypeConfiguration<AnalysisResult>
{
    public void Configure(EntityTypeBuilder<AnalysisResult> builder)
    {
        builder.ToTable("analysis_results");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.JobId)
            .HasColumnName("job_id")
            .IsRequired();

        builder.Property(x => x.TotalWordCount)
            .HasColumnName("total_word_count")
            .IsRequired();

        builder.Property(x => x.MostFrequentWords)
            .HasColumnName("most_frequent_words")
            .HasColumnType("jsonb");

        builder.Property(x => x.LeastFrequentWords)
            .HasColumnName("least_frequent_words")
            .HasColumnType("jsonb");

        builder.Property(x => x.PersonalPronouns)
            .HasColumnName("personal_pronouns")
            .HasColumnType("jsonb");

        builder.Property(x => x.PersonNames)
            .HasColumnName("person_names")
            .HasColumnType("jsonb");

        builder.Property(x => x.RootNouns)
            .HasColumnName("root_nouns")
            .HasColumnType("jsonb");

        builder.Property(x => x.RootVerbs)
            .HasColumnName("root_verbs")
            .HasColumnType("jsonb");

        builder.Property(x => x.OtherClassifications)
            .HasColumnName("other_classifications")
            .HasColumnType("jsonb");

        builder.Property(x => x.FullResult)
            .HasColumnName("full_result")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne(x => x.Job)
            .WithMany(x => x.Results)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.JobId);
    }
}