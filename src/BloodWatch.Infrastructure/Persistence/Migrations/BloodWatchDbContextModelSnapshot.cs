using System;
using BloodWatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BloodWatchDbContext))]
partial class BloodWatchDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.13")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.CurrentReserveEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<DateTime>("CapturedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("captured_at_utc");

            b.Property<string>("MetricKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("metric_key");

            b.Property<DateOnly?>("ReferenceDate")
                .HasColumnType("date")
                .HasColumnName("reference_date");

            b.Property<Guid>("RegionId")
                .HasColumnType("uuid")
                .HasColumnName("region_id");

            b.Property<string>("Severity")
                .HasColumnType("text")
                .HasColumnName("severity");

            b.Property<Guid>("SourceId")
                .HasColumnType("uuid")
                .HasColumnName("source_id");

            b.Property<string>("Unit")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("unit");

            b.Property<DateTime>("UpdatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at_utc");

            b.Property<decimal>("Value")
                .HasPrecision(12, 2)
                .HasColumnType("numeric(12,2)")
                .HasColumnName("value");

            b.HasKey("Id");

            b.HasIndex("RegionId");

            b.HasIndex("SourceId", "CapturedAtUtc");

            b.HasIndex("SourceId", "RegionId", "MetricKey")
                .IsUnique();

            b.ToTable("current_reserves", (string)null);
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.DeliveryEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<Guid>("EventId")
                .HasColumnType("uuid")
                .HasColumnName("event_id");

            b.Property<string>("LastError")
                .HasColumnType("text")
                .HasColumnName("last_error");

            b.Property<DateTime?>("SentAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("sent_at_utc");

            b.Property<string>("Status")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("status");

            b.Property<Guid>("SubscriptionId")
                .HasColumnType("uuid")
                .HasColumnName("subscription_id");

            b.HasKey("Id");

            b.HasIndex("SubscriptionId");

            b.HasIndex("EventId", "Status");

            b.ToTable("deliveries", (string)null);
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.EventEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<string>("MetricKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("metric_key");

            b.Property<string>("PayloadJson")
                .IsRequired()
                .HasColumnType("jsonb")
                .HasColumnName("payload_json");

            b.Property<Guid?>("RegionId")
                .HasColumnType("uuid")
                .HasColumnName("region_id");

            b.Property<string>("RuleKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("rule_key");

            b.Property<Guid>("CurrentReserveId")
                .HasColumnType("uuid")
                .HasColumnName("current_reserve_id");

            b.Property<Guid>("SourceId")
                .HasColumnType("uuid")
                .HasColumnName("source_id");

            b.HasKey("Id");

            b.HasIndex("RegionId");

            b.HasIndex("CurrentReserveId");

            b.HasIndex("SourceId", "CreatedAtUtc");

            b.ToTable("events", (string)null);
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.RegionEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<string>("DisplayName")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("display_name");

            b.Property<string>("Key")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("key");

            b.Property<Guid>("SourceId")
                .HasColumnType("uuid")
                .HasColumnName("source_id");

            b.HasKey("Id");

            b.HasIndex("SourceId", "Key")
                .IsUnique();

            b.ToTable("regions", (string)null);
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<string>("AdapterKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("adapter_key");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<DateTime?>("LastPolledAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_polled_at_utc");

            b.Property<string>("Name")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("name");

            b.HasKey("Id");

            b.HasIndex("AdapterKey")
                .IsUnique();

            b.ToTable("sources", (string)null);

            b.HasData(
                new
                {
                    Id = new Guid("51abf65a-c68a-4bc7-b9f2-3f8f3a9bb2b1"),
                    AdapterKey = "pt-transparencia-sns",
                    CreatedAtUtc = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
                    LastPolledAtUtc = (DateTime?)null,
                    Name = "Portugal SNS Transparency"
                });
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.SubscriptionEntity", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at_utc");

            b.Property<DateTime?>("DisabledAtUtc")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("disabled_at_utc");

            b.Property<bool>("IsEnabled")
                .HasColumnType("boolean")
                .HasColumnName("is_enabled");

            b.Property<string>("RegionFilter")
                .HasColumnType("text")
                .HasColumnName("region_filter");

            b.Property<Guid>("SourceId")
                .HasColumnType("uuid")
                .HasColumnName("source_id");

            b.Property<string>("Target")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("target");

            b.Property<string>("TypeKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("type_key");

            b.HasKey("Id");

            b.HasIndex("SourceId", "IsEnabled");

            b.ToTable("subscriptions", (string)null);
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.DeliveryEntity", b =>
        {
            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.EventEntity", "Event")
                .WithMany("Deliveries")
                .HasForeignKey("EventId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.SubscriptionEntity", "Subscription")
                .WithMany("Deliveries")
                .HasForeignKey("SubscriptionId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Event");

            b.Navigation("Subscription");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.CurrentReserveEntity", b =>
        {
            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.RegionEntity", "Region")
                .WithMany("CurrentReserves")
                .HasForeignKey("RegionId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", "Source")
                .WithMany("CurrentReserves")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Region");

            b.Navigation("Source");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.EventEntity", b =>
        {
            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.RegionEntity", "Region")
                .WithMany("Events")
                .HasForeignKey("RegionId")
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.CurrentReserveEntity", "CurrentReserve")
                .WithMany("Events")
                .HasForeignKey("CurrentReserveId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", "Source")
                .WithMany("Events")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Region");

            b.Navigation("CurrentReserve");

            b.Navigation("Source");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.RegionEntity", b =>
        {
            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", "Source")
                .WithMany("Regions")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Source");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.SubscriptionEntity", b =>
        {
            b.HasOne("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", "Source")
                .WithMany("Subscriptions")
                .HasForeignKey("SourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Source");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.EventEntity", b =>
        {
            b.Navigation("Deliveries");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.RegionEntity", b =>
        {
            b.Navigation("CurrentReserves");

            b.Navigation("Events");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.SourceEntity", b =>
        {
            b.Navigation("CurrentReserves");

            b.Navigation("Events");

            b.Navigation("Regions");

            b.Navigation("Subscriptions");
        });

        modelBuilder.Entity("BloodWatch.Infrastructure.Persistence.Entities.SubscriptionEntity", b =>
        {
            b.Navigation("Deliveries");
        });
#pragma warning restore 612, 618
    }
}
