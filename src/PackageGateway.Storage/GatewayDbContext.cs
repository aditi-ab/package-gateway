using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PackageGateway.Domain;

namespace PackageGateway.Storage;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<LocalAdministrator> LocalAdministrators => Set<LocalAdministrator>();
    public DbSet<AdminIdentityProviderDocument> AdminIdentityProviders => Set<AdminIdentityProviderDocument>();
    public DbSet<EntraConnectionSettings> EntraConnectionSettings => Set<EntraConnectionSettings>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Upstream> Upstreams => Set<Upstream>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageVersion> PackageVersions => Set<PackageVersion>();
    public DbSet<PackageBlob> PackageBlobs => Set<PackageBlob>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<RepositoryPolicy> RepositoryPolicies => Set<RepositoryPolicy>();
    public DbSet<SecurityScan> SecurityScans => Set<SecurityScan>();
    public DbSet<SecurityFinding> SecurityFindings => Set<SecurityFinding>();
    public DbSet<PolicyRuleResult> PolicyRuleResults => Set<PolicyRuleResult>();
    public DbSet<PackageApproval> PackageApprovals => Set<PackageApproval>();
    public DbSet<PackageApprovalRuleResult> PackageApprovalRuleResults => Set<PackageApprovalRuleResult>();
    public DbSet<VulnerabilityCacheEntry> VulnerabilityCacheEntries => Set<VulnerabilityCacheEntry>();
    public DbSet<BackgroundJobState> BackgroundJobStates => Set<BackgroundJobState>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LocalAdministrator>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).HasMaxLength(100);
            b.Property(x => x.NormalizedUsername).HasMaxLength(100);
            b.Property(x => x.PasswordHash).HasMaxLength(1000);
            b.Property(x => x.Roles).HasMaxLength(1000);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.SecurityStamp).HasMaxLength(128);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => x.NormalizedUsername).IsUnique();
        });
        model.Entity<AdminIdentityProviderDocument>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(100);
        });
        model.Entity<EntraConnectionSettings>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Authority).HasMaxLength(1000);
            b.Property(x => x.Audience).HasMaxLength(500);
            b.Property(x => x.ClientId).HasMaxLength(100);
            b.Property(x => x.Scope).HasMaxLength(1000);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        });
        model.Entity<Repository>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Slug).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.PackageType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => x.Slug).IsUnique();
        });
        model.Entity<Upstream>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Url).HasMaxLength(2000);
            b.Property(x => x.PackageType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.HealthDetail).HasMaxLength(2000);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => new { x.RepositoryId, x.Priority });
            b.HasOne<Repository>().WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Package>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(300);
            b.Property(x => x.NormalizedName).HasMaxLength(300);
            b.Property(x => x.PackageType).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.RepositoryId, x.PackageType, x.NormalizedName }).IsUnique();
            b.HasOne<Repository>().WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PackageVersion>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Version).HasMaxLength(100);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ArtifactUrl).HasMaxLength(4000);
            b.Property(x => x.ExpectedSha256).HasMaxLength(64);
            b.Property(x => x.ExpectedIntegrity).HasMaxLength(1000);
            b.Property(x => x.SignatureStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.Property(x => x.License).HasMaxLength(500);
            b.Property(x => x.Author).HasMaxLength(500);
            b.Property(x => x.Publisher).HasMaxLength(500);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => new { x.PackageId, x.Version }).IsUnique();
            b.HasOne<Package>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Upstream>().WithMany().HasForeignKey(x => x.UpstreamId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PackageBlob>(b =>
        {
            b.HasKey(x => x.PackageVersionId);
            b.Property(x => x.Content).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.HasOne<PackageVersion>().WithOne().HasForeignKey<PackageBlob>(x => x.PackageVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Policy>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Type).HasMaxLength(100);
            b.Property(x => x.ConfigJson).HasMaxLength(32000);
            b.Property(x => x.PackageTypes).HasMaxLength(100);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => x.Name);
        });
        model.Entity<RepositoryPolicy>(b =>
        {
            b.HasKey(x => new { x.RepositoryId, x.PolicyId });
            b.HasOne<Repository>().WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SecurityScan>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ScannerVersion).HasMaxLength(100);
            b.Property(x => x.Result).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.PackageVersionId, x.StartedAt });
            b.HasOne<PackageVersion>().WithMany().HasForeignKey(x => x.PackageVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SecurityFinding>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(100);
            b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Title).HasMaxLength(500);
            b.Property(x => x.Source).HasMaxLength(200);
            b.Property(x => x.ExternalReference).HasMaxLength(2000);
            b.HasIndex(x => new { x.SecurityScanId, x.Severity });
            b.HasOne<SecurityScan>().WithMany().HasForeignKey(x => x.SecurityScanId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PolicyRuleResult>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Rule).HasMaxLength(100);
            b.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(x => x.PackageVersionId);
            b.HasOne<PackageVersion>().WithMany().HasForeignKey(x => x.PackageVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PackageApproval>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.CreatedBy).HasMaxLength(300);
            b.HasIndex(x => x.PackageVersionId);
            b.HasOne<PackageVersion>().WithMany().HasForeignKey(x => x.PackageVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PackageApprovalRuleResult>(b =>
        {
            b.HasKey(x => new { x.PackageApprovalId, x.PolicyRuleResultId });
            b.HasOne<PackageApproval>().WithMany().HasForeignKey(x => x.PackageApprovalId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PolicyRuleResult>().WithMany().HasForeignKey(x => x.PolicyRuleResultId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<VulnerabilityCacheEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(100);
            b.Property(x => x.PackageType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.NormalizedName).HasMaxLength(300);
            b.Property(x => x.Version).HasMaxLength(100);
            b.Property(x => x.PayloadJson).HasMaxLength(1_000_000);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => new { x.Provider, x.PackageType, x.NormalizedName, x.Version }).IsUnique();
            b.HasIndex(x => x.ExpiresAt);
        });
        model.Entity<BackgroundJobState>(b =>
        {
            b.HasKey(x => x.Name);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.LeaseOwner).HasMaxLength(200);
            b.Property(x => x.LastError).HasMaxLength(4000);
            b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            b.HasIndex(x => x.LeaseExpiresAt);
        });
        model.Entity<AuditEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Actor).HasMaxLength(300);
            b.Property(x => x.Action).HasMaxLength(100);
            b.Property(x => x.EntityType).HasMaxLength(100);
            b.Property(x => x.EntityId).HasMaxLength(200);
            b.HasIndex(x => x.Timestamp);
            b.HasIndex(x => new { x.EntityType, x.EntityId });
        });
        model.Entity<AccessToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.TokenId).HasMaxLength(32);
            b.Property(x => x.Verifier).HasMaxLength(128);
            b.Property(x => x.Owner).HasMaxLength(300);
            b.Property(x => x.Scopes).HasMaxLength(4000);
            b.Property(x => x.ExpiresAt);
            b.HasIndex(x => x.TokenId).IsUnique();
        });
        if (Database.IsSqlite())
        {
            var required = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
            var optional = new ValueConverter<DateTimeOffset?, long?>(
                value => value == null ? null : value.Value.UtcTicks,
                value => value == null ? null : new DateTimeOffset(value.Value, TimeSpan.Zero));
            foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(required);
                else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(optional);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardAuditImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAuditImmutability()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit events are immutable.");
    }
}
