using AutomatasFinal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<EntityDictionary> EntityDictionary => Set<EntityDictionary>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserBiometric> UserBiometrics => Set<UserBiometric>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<CredentialDesign> CredentialDesigns => Set<CredentialDesign>();
    public DbSet<ProfileChangeRequest> ProfileChangeRequests => Set<ProfileChangeRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        modelBuilder.Entity<EntityDictionary>(entity =>
        {
            entity.ToTable("entity_dictionary");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.Value)
                .HasColumnName("value")
                .IsRequired();

            entity.Property(x => x.NormalizedValue)
                .HasColumnName("normalized_value")
                .IsRequired();

            entity.Property(x => x.CanonicalValue)
                .HasColumnName("canonical_value");

            entity.Property(x => x.EntityType)
                .HasColumnName("entity_type")
                .IsRequired();

            entity.Property(x => x.Language)
                .HasColumnName("language");

            entity.Property(x => x.Source)
                .HasColumnName("source");

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
        });
    }
}