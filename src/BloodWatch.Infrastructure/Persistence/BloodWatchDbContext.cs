using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Infrastructure.Persistence;

public sealed class BloodWatchDbContext(DbContextOptions<BloodWatchDbContext> options) : DbContext(options)
{
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<RegionEntity> Regions => Set<RegionEntity>();
    public DbSet<CurrentReserveEntity> CurrentReserves => Set<CurrentReserveEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<SubscriptionNotificationStateEntity> SubscriptionNotificationStates => Set<SubscriptionNotificationStateEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SourceEntity>(entity =>
        {
            entity.ToTable("sources");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AdapterKey).HasColumnName("adapter_key").IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.LastPolledAtUtc).HasColumnName("last_polled_at_utc");
            entity.HasIndex(x => x.AdapterKey).IsUnique();
            entity.HasData(new SourceEntity
            {
                Id = SeedData.PortugalSourceId,
                AdapterKey = "pt-transparencia-sns",
                Name = "Portugal SNS Transparency",
                CreatedAtUtc = SeedData.SeedCreatedAtUtc,
                LastPolledAtUtc = null,
            });
        });

        modelBuilder.Entity<RegionEntity>(entity =>
        {
            entity.ToTable("regions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.Key).HasColumnName("key").IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.HasIndex(x => new { x.SourceId, x.Key }).IsUnique();
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Regions)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CurrentReserveEntity>(entity =>
        {
            entity.ToTable("current_reserves");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.RegionId).HasColumnName("region_id").IsRequired();
            entity.Property(x => x.MetricKey).HasColumnName("metric_key").IsRequired();
            entity.Property(x => x.Value).HasColumnName("value").HasPrecision(12, 2).IsRequired();
            entity.Property(x => x.Unit).HasColumnName("unit").IsRequired();
            entity.Property(x => x.Severity).HasColumnName("severity");
            entity.Property(x => x.ReferenceDate).HasColumnName("reference_date");
            entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => new { x.SourceId, x.RegionId, x.MetricKey }).IsUnique();
            entity.HasIndex(x => new { x.SourceId, x.CapturedAtUtc });
            entity.HasOne(x => x.Source)
                .WithMany(x => x.CurrentReserves)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Region)
                .WithMany(x => x.CurrentReserves)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubscriptionEntity>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.TypeKey).HasColumnName("type_key").IsRequired();
            entity.Property(x => x.Target).HasColumnName("target").IsRequired();
            entity.Property(x => x.RegionFilter).HasColumnName("region_filter").IsRequired();
            entity.Property(x => x.MetricFilter).HasColumnName("metric_filter").IsRequired();
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.DisabledAtUtc).HasColumnName("disabled_at_utc");
            entity.HasIndex(x => new { x.SourceId, x.IsEnabled });
            entity.HasIndex(x => new { x.SourceId, x.RegionFilter, x.MetricFilter, x.IsEnabled });
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionNotificationStateEntity>(entity =>
        {
            entity.ToTable("subscription_notification_states");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
            entity.Property(x => x.IsLowOpen).HasColumnName("is_low_open").HasDefaultValue(false).IsRequired();
            entity.Property(x => x.LastLowNotifiedAtUtc).HasColumnName("last_low_notified_at_utc");
            entity.Property(x => x.LastLowNotifiedBucket).HasColumnName("last_low_notified_bucket");
            entity.Property(x => x.LastLowNotifiedUnits).HasColumnName("last_low_notified_units").HasPrecision(12, 2);
            entity.Property(x => x.LastRecoveryNotifiedAtUtc).HasColumnName("last_recovery_notified_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => x.UpdatedAtUtc);
            entity.HasOne(x => x.Subscription)
                .WithOne(x => x.NotificationState)
                .HasForeignKey<SubscriptionNotificationStateEntity>(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.CurrentReserveId).HasColumnName("current_reserve_id").IsRequired();
            entity.Property(x => x.RegionId).HasColumnName("region_id");
            entity.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired();
            entity.Property(x => x.MetricKey).HasColumnName("metric_key").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.HasIndex(x => x.CurrentReserveId);
            entity.HasIndex(x => new { x.SourceId, x.CreatedAtUtc });
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CurrentReserve)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.CurrentReserveId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Region)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryEntity>(entity =>
        {
            entity.ToTable("deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
            entity.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc");
            entity.HasIndex(x => new { x.EventId, x.Status });
            entity.HasIndex(x => new { x.EventId, x.SubscriptionId }).IsUnique();
            entity.HasOne(x => x.Event)
                .WithMany(x => x.Deliveries)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Subscription)
                .WithMany(x => x.Deliveries)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
