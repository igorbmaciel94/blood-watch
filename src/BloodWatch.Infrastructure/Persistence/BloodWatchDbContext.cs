using BloodWatch.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Infrastructure.Persistence;

public sealed class BloodWatchDbContext(DbContextOptions<BloodWatchDbContext> options) : DbContext(options)
{
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<RegionEntity> Regions => Set<RegionEntity>();
    public DbSet<CurrentReserveEntity> CurrentReserves => Set<CurrentReserveEntity>();
    public DbSet<ReserveHistoryObservationEntity> ReserveHistoryObservations => Set<ReserveHistoryObservationEntity>();
    public DbSet<DonationCenterEntity> DonationCenters => Set<DonationCenterEntity>();
    public DbSet<CollectionSessionEntity> CollectionSessions => Set<CollectionSessionEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
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
                AdapterKey = "pt-dador-ipst",
                Name = "Portugal Dador/IPST",
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
            entity.Property(x => x.StatusKey).HasColumnName("status_key").IsRequired();
            entity.Property(x => x.StatusLabel).HasColumnName("status_label").IsRequired();
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

        modelBuilder.Entity<ReserveHistoryObservationEntity>(entity =>
        {
            entity.ToTable("reserve_history_observations");
            entity.HasKey(x => new { x.SourceId, x.RegionId, x.MetricKey, x.ReferenceDate, x.StatusKey });
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.RegionId).HasColumnName("region_id").IsRequired();
            entity.Property(x => x.MetricKey).HasColumnName("metric_key").IsRequired();
            entity.Property(x => x.StatusKey).HasColumnName("status_key").IsRequired();
            entity.Property(x => x.StatusRank).HasColumnName("status_rank").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.ReferenceDate).HasColumnName("reference_date").IsRequired();
            entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
            entity.HasIndex(x => new { x.SourceId, x.MetricKey, x.ReferenceDate });
            entity.HasIndex(x => new { x.SourceId, x.ReferenceDate });
            entity.HasIndex(x => new { x.SourceId, x.RegionId, x.MetricKey, x.CapturedAtUtc })
                .IsDescending(false, false, false, true);
            entity.HasIndex(x => new { x.SourceId, x.StatusRank, x.ReferenceDate });
            entity.HasOne(x => x.Source)
                .WithMany(x => x.ReserveHistoryObservations)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Region)
                .WithMany(x => x.ReserveHistoryObservations)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DonationCenterEntity>(entity =>
        {
            entity.ToTable("donation_centers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.RegionId).HasColumnName("region_id").IsRequired();
            entity.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            entity.Property(x => x.InstitutionCode).HasColumnName("institution_code").IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.DistrictCode).HasColumnName("district_code");
            entity.Property(x => x.DistrictName).HasColumnName("district_name");
            entity.Property(x => x.MunicipalityCode).HasColumnName("municipality_code");
            entity.Property(x => x.MunicipalityName).HasColumnName("municipality_name");
            entity.Property(x => x.Address).HasColumnName("address");
            entity.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
            entity.Property(x => x.PlusCode).HasColumnName("plus_code");
            entity.Property(x => x.Schedule).HasColumnName("schedule");
            entity.Property(x => x.Phone).HasColumnName("phone");
            entity.Property(x => x.MobilePhone).HasColumnName("mobile_phone");
            entity.Property(x => x.Email).HasColumnName("email");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.SourceId, x.InstitutionCode }).IsUnique();
            entity.HasIndex(x => new { x.SourceId, x.RegionId });
            entity.HasOne(x => x.Source)
                .WithMany(x => x.DonationCenters)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Region)
                .WithMany(x => x.DonationCenters)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CollectionSessionEntity>(entity =>
        {
            entity.ToTable("collection_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.RegionId).HasColumnName("region_id").IsRequired();
            entity.Property(x => x.DonationCenterId).HasColumnName("donation_center_id");
            entity.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            entity.Property(x => x.InstitutionCode).HasColumnName("institution_code").IsRequired();
            entity.Property(x => x.InstitutionName).HasColumnName("institution_name").IsRequired();
            entity.Property(x => x.DistrictCode).HasColumnName("district_code");
            entity.Property(x => x.DistrictName).HasColumnName("district_name");
            entity.Property(x => x.MunicipalityCode).HasColumnName("municipality_code");
            entity.Property(x => x.MunicipalityName).HasColumnName("municipality_name");
            entity.Property(x => x.Location).HasColumnName("location");
            entity.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
            entity.Property(x => x.SessionDate).HasColumnName("session_date");
            entity.Property(x => x.SessionHours).HasColumnName("session_hours");
            entity.Property(x => x.AccessCode).HasColumnName("access_code");
            entity.Property(x => x.StateCode).HasColumnName("state_code");
            entity.Property(x => x.SessionTypeCode).HasColumnName("session_type_code");
            entity.Property(x => x.SessionTypeName).HasColumnName("session_type_name");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.SourceId, x.SessionDate });
            entity.HasIndex(x => x.DonationCenterId);
            entity.HasOne(x => x.Source)
                .WithMany(x => x.CollectionSessions)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Region)
                .WithMany(x => x.CollectionSessions)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DonationCenter)
                .WithMany(x => x.CollectionSessions)
                .HasForeignKey(x => x.DonationCenterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SubscriptionEntity>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(x => x.TypeKey).HasColumnName("type_key").IsRequired();
            entity.Property(x => x.Target).HasColumnName("target").IsRequired();
            entity.Property(x => x.ScopeType).HasColumnName("scope_type").IsRequired();
            entity.Property(x => x.RegionFilter).HasColumnName("region_filter");
            entity.Property(x => x.InstitutionId).HasColumnName("institution_id");
            entity.Property(x => x.MetricFilter).HasColumnName("metric_filter").IsRequired();
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.DisabledAtUtc).HasColumnName("disabled_at_utc");
            entity.HasIndex(x => new { x.SourceId, x.IsEnabled });
            entity.HasIndex(x => new { x.SourceId, x.ScopeType, x.RegionFilter, x.InstitutionId, x.MetricFilter, x.IsEnabled });
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Institution)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Restrict);
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
