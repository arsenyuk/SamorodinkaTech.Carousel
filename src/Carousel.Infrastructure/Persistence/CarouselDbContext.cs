using Carousel.Domain;
using Carousel.Domain.Entities;
using Carousel.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Carousel.Infrastructure.Persistence;

public class CarouselDbContext : DbContext
{
    public CarouselDbContext(DbContextOptions<CarouselDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PlatformAccount> PlatformAccounts => Set<PlatformAccount>();
    public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
    public DbSet<PublicationTarget> PublicationTargets => Set<PublicationTarget>();
    public DbSet<PublicationDelivery> PublicationDeliveries => Set<PublicationDelivery>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanLimit> PlanLimits => Set<PlanLimit>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<BotConversationState> BotConversationStates => Set<BotConversationState>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CarouselTemplate> CarouselTemplates => Set<CarouselTemplate>();
    public DbSet<BusinessConnection> BusinessConnections => Set<BusinessConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.Property(x => x.TelegramUsername).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Owner)
                .WithMany(x => x.OwnedTenants)
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantMember>(e =>
        {
            e.ToTable("tenant_members");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(x => x.Members).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(x => x.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Publication>(e =>
        {
            e.ToTable("publications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.NoAds).HasDefaultValue(false);
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany(x => x.Publications).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CarouselTemplate>(e =>
        {
            e.ToTable("carousel_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.DefaultStylesJson).HasColumnType("jsonb");
            // Seed codes unique globally; user codes unique per-tenant.
            e.HasIndex(x => x.Code).IsUnique().HasFilter("\"TenantId\" IS NULL");
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BackgroundMediaAsset).WithMany().HasForeignKey(x => x.BackgroundMediaAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Card>(e =>
        {
            e.ToTable("cards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(CardStyleRules.MaxTextLength);
            e.Property(x => x.Styles)
                .HasColumnName("styles_json")
                .HasColumnType("jsonb")
                .HasConversion(new CardStyleArrayConverter())
                .Metadata.SetValueComparer(new CardStyleArrayComparer());
            e.HasIndex(x => new { x.PublicationId, x.Order });
            e.HasOne(x => x.Publication).WithMany(x => x.Cards).HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.ToTable("media_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Content).HasColumnType("bytea");
            e.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<PlatformAccount>(e =>
        {
            e.ToTable("platform_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Platform, x.ExternalId }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlatformCredential>(e =>
        {
            e.ToTable("platform_credentials");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(100).IsRequired();
            e.Property(x => x.EncryptedPayload).HasColumnType("bytea").IsRequired();
            e.HasOne(x => x.PlatformAccount).WithMany(x => x.Credentials).HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicationTarget>(e =>
        {
            e.ToTable("publication_targets");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PublicationId);
            e.HasOne(x => x.Publication).WithMany(x => x.Targets).HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PublicationDelivery>(e =>
        {
            e.ToTable("publication_deliveries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PublicationTargetId, x.Attempt }).IsUnique();
            e.Property(x => x.ExternalPostId).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(4000);
            e.HasOne(x => x.PublicationTarget).WithMany(x => x.Deliveries).HasForeignKey(x => x.PublicationTargetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<PlanLimit>(e =>
        {
            e.ToTable("plan_limits");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PlanId, x.LimitKey }).IsUnique();
            e.HasOne(x => x.Plan).WithMany(x => x.Limits).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillingEvent>(e =>
        {
            e.ToTable("billing_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Payment).WithMany(x => x.Events).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageCounter>(e =>
        {
            e.ToTable("usage_counters");
            e.HasKey(x => x.Id);
            e.Property(x => x.Period).HasMaxLength(7).IsRequired();
            e.Property(x => x.Metric).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Period, x.Metric }).IsUnique();
        });

        modelBuilder.Entity<BotConversationState>(e =>
        {
            e.ToTable("bot_conversation_states");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<BusinessConnection>(e =>
        {
            e.ToTable("business_connections");
            e.HasKey(x => x.Id);
            e.Property(x => x.ConnectionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Username).HasMaxLength(64);
            e.HasIndex(x => x.ConnectionId).IsUnique();
            e.HasIndex(x => x.TelegramUserId);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(200).IsRequired();
            e.Property(x => x.Login).HasMaxLength(100);
            e.Property(x => x.UserIp).HasMaxLength(45);
            e.Property(x => x.Result).HasMaxLength(20);
            e.HasIndex(x => x.TenantId);
        });
    }
}
