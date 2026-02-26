using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BloodWatch.Adapters.Portugal;
using BloodWatch.Core.Contracts;
using BloodWatch.Core.Models;
using BloodWatch.Infrastructure.Persistence;
using BloodWatch.Infrastructure.Persistence.Entities;
using BloodWatch.Worker.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace BloodWatch.Worker;

public sealed class FetchPortugalReservesJob(
    IEnumerable<IDataSourceAdapter> adapters,
    IDadorPtClient dadorPtClient,
    DadorInstitutionsMapper institutionsMapper,
    DadorSessionsMapper sessionsMapper,
    IEnumerable<IRule> rules,
    BloodWatchDbContext dbContext,
    DispatchEngine dispatchEngine,
    ILogger<FetchPortugalReservesJob> logger)
{
    private const string StatusPresenceRuleKey = "reserve-status-presence.v1";
    private const string StatusAlertSignal = "status-alert";
    private const string StatusPresenceTransitionKind = "non-normal-presence";

    private readonly IReadOnlyCollection<IDataSourceAdapter> _adapters = adapters.ToArray();
    private readonly IDadorPtClient _dadorPtClient = dadorPtClient;
    private readonly DadorInstitutionsMapper _institutionsMapper = institutionsMapper;
    private readonly DadorSessionsMapper _sessionsMapper = sessionsMapper;
    private readonly IReadOnlyCollection<IRule> _rules = rules.ToArray();
    private readonly BloodWatchDbContext _dbContext = dbContext;
    private readonly DispatchEngine _dispatchEngine = dispatchEngine;
    private readonly ILogger<FetchPortugalReservesJob> _logger = logger;

    public async Task<FetchPortugalReservesResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var ingestStopwatch = Stopwatch.StartNew();

        var adapter = _adapters.FirstOrDefault(candidate => candidate.AdapterKey == PortugalAdapter.DefaultAdapterKey)
            ?? throw new InvalidOperationException($"No adapter registered for {PortugalAdapter.DefaultAdapterKey}.");

        var snapshot = await adapter.FetchLatestAsync(cancellationToken);

        using var institutionsPayload = await _dadorPtClient.GetInstitutionsPayloadAsync(cancellationToken);
        var institutions = _institutionsMapper.Map(institutionsPayload.RootElement);

        using var sessionsPayload = await _dadorPtClient.GetSessionsPayloadAsync(cancellationToken);
        var sessions = _sessionsMapper.Map(sessionsPayload.RootElement);

        var polledAtUtc = DateTime.UtcNow;
        var source = await EnsureSourceAsync(snapshot.Source, polledAtUtc, cancellationToken);
        source.LastPolledAtUtc = polledAtUtc;

        var regionRefs = CollectRegions(snapshot, institutions, sessions);
        await EnsureRegionsAsync(source.Id, regionRefs, polledAtUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var regionRows = await _dbContext.Regions
            .Where(region => region.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var regionIdByKey = regionRows.ToDictionary(region => region.Key, region => region.Id, StringComparer.Ordinal);
        var regionById = regionRows.ToDictionary(
            region => region.Id,
            region => new RegionRef(region.Key, region.DisplayName));

        var incomingByKey = BuildIncomingReserves(snapshot, regionIdByKey);
        var effectiveReferenceDate = snapshot.ReferenceDate ?? DateOnly.FromDateTime(snapshot.CapturedAtUtc);

        var existingRows = await _dbContext.CurrentReserves
            .Where(entry => entry.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var existingByKey = existingRows.ToDictionary(
            entry => new CurrentReserveKey(entry.RegionId, entry.MetricKey),
            entry => entry);

        var previousRows = existingRows
            .Select(entry => new SnapshotReserveRow(
                entry.RegionId,
                entry.MetricKey,
                entry.StatusKey,
                entry.StatusLabel))
            .ToArray();

        Snapshot? previousSnapshot = previousRows.Length == 0
            ? null
            : BuildSnapshot(snapshot.Source, snapshot.CapturedAtUtc, snapshot.ReferenceDate, previousRows, regionById, snapshot.SourceUpdatedAtUtc);

        var insertedCount = 0;
        var updatedCount = 0;
        var matchedExistingCount = 0;
        var pendingHistory = new List<PendingHistoryObservation>();

        var currentByKey = new Dictionary<CurrentReserveKey, CurrentReserveEntity>(existingByKey);

        foreach (var incoming in incomingByKey.Values)
        {
            if (existingByKey.TryGetValue(incoming.Key, out var existing))
            {
                matchedExistingCount++;

                var previousStatusKey = ReserveStatusCatalog.NormalizeKey(existing.StatusKey);
                var hasReferenceDateChanged = existing.ReferenceDate != effectiveReferenceDate;
                var hasStatusChanged = !string.Equals(previousStatusKey, incoming.StatusKey, StringComparison.Ordinal);

                existing.StatusKey = incoming.StatusKey;
                existing.StatusLabel = incoming.StatusLabel;
                existing.ReferenceDate = effectiveReferenceDate;
                existing.CapturedAtUtc = snapshot.CapturedAtUtc;
                existing.UpdatedAtUtc = polledAtUtc;
                updatedCount++;

                if (hasReferenceDateChanged || hasStatusChanged)
                {
                    pendingHistory.Add(new PendingHistoryObservation(
                        source.Id,
                        incoming.Key.RegionId,
                        incoming.Key.MetricKey,
                        incoming.StatusKey,
                        (short)ReserveStatusCatalog.GetRank(incoming.StatusKey),
                        effectiveReferenceDate,
                        snapshot.CapturedAtUtc));
                }

                continue;
            }

            var entity = new CurrentReserveEntity
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                RegionId = incoming.Key.RegionId,
                MetricKey = incoming.Key.MetricKey,
                StatusKey = incoming.StatusKey,
                StatusLabel = incoming.StatusLabel,
                ReferenceDate = effectiveReferenceDate,
                CapturedAtUtc = snapshot.CapturedAtUtc,
                UpdatedAtUtc = polledAtUtc,
            };

            _dbContext.CurrentReserves.Add(entity);
            currentByKey[incoming.Key] = entity;

            pendingHistory.Add(new PendingHistoryObservation(
                source.Id,
                incoming.Key.RegionId,
                incoming.Key.MetricKey,
                incoming.StatusKey,
                (short)ReserveStatusCatalog.GetRank(incoming.StatusKey),
                effectiveReferenceDate,
                snapshot.CapturedAtUtc));

            insertedCount++;
        }

        await PersistReserveHistoryObservationsAsync(pendingHistory, cancellationToken);

        var carriedForwardCount = Math.Max(0, existingByKey.Count - matchedExistingCount);

        var upsertedInstitutions = await UpsertDonationCentersAsync(
            source.Id,
            institutions,
            regionIdByKey,
            polledAtUtc,
            cancellationToken);

        var centersByCode = await _dbContext.DonationCenters
            .Where(center => center.SourceId == source.Id)
            .ToDictionaryAsync(center => center.InstitutionCode, center => center.Id, StringComparer.Ordinal, cancellationToken);

        var upsertedSessions = await UpsertCollectionSessionsAsync(
            source.Id,
            sessions,
            regionIdByKey,
            centersByCode,
            polledAtUtc,
            cancellationToken);

        var currentRows = currentByKey.Values
            .Select(entry => new SnapshotReserveRow(
                entry.RegionId,
                entry.MetricKey,
                entry.StatusKey,
                entry.StatusLabel))
            .ToArray();

        var currentSnapshot = BuildSnapshot(
            snapshot.Source,
            snapshot.CapturedAtUtc,
            effectiveReferenceDate,
            currentRows,
            regionById,
            snapshot.SourceUpdatedAtUtc);

        ingestStopwatch.Stop();
        _logger.LogInformation(
            "Ingest stage completed in {DurationMs}ms. InsertedCurrentReserves: {InsertedCurrentReserves}; UpdatedCurrentReserves: {UpdatedCurrentReserves}; CarriedForwardCurrentReserves: {CarriedForwardCurrentReserves}; UpsertedInstitutions: {UpsertedInstitutions}; UpsertedSessions: {UpsertedSessions}.",
            ingestStopwatch.ElapsedMilliseconds,
            insertedCount,
            updatedCount,
            carriedForwardCount,
            upsertedInstitutions,
            upsertedSessions);

        var rulesStopwatch = Stopwatch.StartNew();
        var transitionEvents = await EvaluateRulesAsync(previousSnapshot, currentSnapshot, cancellationToken);
        var statusPresenceEvents = CreateStatusPresenceEvents(currentSnapshot, polledAtUtc);
        var generatedEvents = transitionEvents
            .Concat(statusPresenceEvents)
            .ToArray();
        rulesStopwatch.Stop();

        _logger.LogInformation(
            "Rules stage completed in {DurationMs}ms. TransitionEvents: {TransitionEvents}; StatusPresenceEvents: {StatusPresenceEvents}; GeneratedEvents: {GeneratedEvents}.",
            rulesStopwatch.ElapsedMilliseconds,
            transitionEvents.Count,
            statusPresenceEvents.Count,
            generatedEvents.Length);

        var dispatchStopwatch = Stopwatch.StartNew();
        var insertedEvents = await PersistEventsAsync(
            source.Id,
            generatedEvents,
            regionIdByKey,
            currentByKey,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var statusPresenceDispatchEvents = await LoadStatusPresenceDispatchEventsAsync(
            source.Id,
            currentByKey,
            cancellationToken);

        var dispatchEvents = insertedEvents
            .Concat(statusPresenceDispatchEvents)
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToArray();

        var sentCount = 0;
        if (dispatchEvents.Length > 0)
        {
            sentCount = await _dispatchEngine.DispatchAsync(dispatchEvents, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        dispatchStopwatch.Stop();
        _logger.LogInformation(
            "Dispatch stage completed in {DurationMs}ms. DispatchCandidates: {DispatchCandidates}; SentDeliveries: {SentDeliveries}.",
            dispatchStopwatch.ElapsedMilliseconds,
            dispatchEvents.Length,
            sentCount);

        return new FetchPortugalReservesResult(
            insertedCount,
            updatedCount,
            carriedForwardCount,
            upsertedInstitutions,
            upsertedSessions,
            generatedEvents.Length,
            dispatchEvents.Length,
            sentCount,
            ingestStopwatch.ElapsedMilliseconds,
            rulesStopwatch.ElapsedMilliseconds,
            dispatchStopwatch.ElapsedMilliseconds,
            polledAtUtc);
    }

    private static IReadOnlyCollection<RegionRef> CollectRegions(
        Snapshot snapshot,
        IReadOnlyCollection<DadorInstitutionRecord> institutions,
        IReadOnlyCollection<DadorSessionRecord> sessions)
    {
        var result = new Dictionary<string, RegionRef>(StringComparer.Ordinal);

        foreach (var item in snapshot.Items)
        {
            result[item.Region.Key] = item.Region;
        }

        foreach (var institution in institutions)
        {
            result[institution.RegionKey] = new RegionRef(institution.RegionKey, institution.RegionName);
        }

        foreach (var session in sessions)
        {
            result[session.RegionKey] = new RegionRef(session.RegionKey, session.RegionName);
        }

        return result.Values.ToArray();
    }

    private static IReadOnlyCollection<Event> CreateStatusPresenceEvents(Snapshot snapshot, DateTime createdAtUtc)
    {
        var events = new List<Event>();
        foreach (var item in snapshot.Items)
        {
            var currentStatusKey = ReserveStatusCatalog.NormalizeKey(item.StatusKey);
            if (ReserveStatusCatalog.IsNormal(currentStatusKey))
            {
                continue;
            }

            var currentStatusLabel = ReserveStatusCatalog.GetLabel(currentStatusKey);
            var payloadJson = JsonSerializer.Serialize(new
            {
                source = snapshot.Source.AdapterKey,
                region = item.Region.Key,
                metric = item.Metric.Key,
                signal = StatusAlertSignal,
                transitionKind = StatusPresenceTransitionKind,
                previousStatusKey = currentStatusKey,
                previousStatusLabel = currentStatusLabel,
                currentStatusKey,
                currentStatusLabel,
                capturedAtUtc = (DateTime?)null,
                referenceDate = snapshot.ReferenceDate,
            });

            events.Add(new Event(
                StatusPresenceRuleKey,
                snapshot.Source,
                item.Metric,
                item.Region,
                createdAtUtc,
                payloadJson));
        }

        return events;
    }

    private async Task<IReadOnlyCollection<EventEntity>> LoadStatusPresenceDispatchEventsAsync(
        Guid sourceId,
        IReadOnlyDictionary<CurrentReserveKey, CurrentReserveEntity> currentByKey,
        CancellationToken cancellationToken)
    {
        var nonNormalByReserveId = currentByKey.Values
            .Where(reserve => !ReserveStatusCatalog.IsNormal(reserve.StatusKey))
            .ToDictionary(reserve => reserve.Id, reserve => ReserveStatusCatalog.NormalizeKey(reserve.StatusKey));

        if (nonNormalByReserveId.Count == 0)
        {
            return [];
        }

        var reserveIds = nonNormalByReserveId.Keys.ToArray();

        var candidates = await _dbContext.Events
            .AsNoTracking()
            .Where(eventEntity =>
                eventEntity.SourceId == sourceId
                && eventEntity.RuleKey == StatusPresenceRuleKey
                && reserveIds.Contains(eventEntity.CurrentReserveId))
            .OrderByDescending(eventEntity => eventEntity.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var activeEvents = candidates
            .GroupBy(eventEntity => eventEntity.CurrentReserveId)
            .Select(group =>
            {
                var expectedStatusKey = nonNormalByReserveId[group.Key];
                return group.FirstOrDefault(eventEntity =>
                    string.Equals(
                        TryReadStringFromPayload(eventEntity.PayloadJson, "currentStatusKey"),
                        expectedStatusKey,
                        StringComparison.Ordinal))
                    ?? group.First();
            })
            .ToArray();

        return activeEvents;
    }

    private async Task<SourceEntity> EnsureSourceAsync(SourceRef sourceRef, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var source = await _dbContext.Sources
            .SingleOrDefaultAsync(entry => entry.AdapterKey == sourceRef.AdapterKey, cancellationToken);

        if (source is not null)
        {
            source.Name = sourceRef.Name;
            return source;
        }

        source = new SourceEntity
        {
            Id = Guid.NewGuid(),
            AdapterKey = sourceRef.AdapterKey,
            Name = sourceRef.Name,
            CreatedAtUtc = nowUtc,
            LastPolledAtUtc = nowUtc,
        };

        _dbContext.Sources.Add(source);

        return source;
    }

    private async Task EnsureRegionsAsync(
        Guid sourceId,
        IEnumerable<RegionRef> regions,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var desiredRegionsByKey = regions
            .GroupBy(region => region.Key, StringComparer.Ordinal)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.First().DisplayName,
                StringComparer.Ordinal);

        if (desiredRegionsByKey.Count == 0)
        {
            return;
        }

        var regionKeys = desiredRegionsByKey.Keys.ToArray();
        var existingRegions = await _dbContext.Regions
            .Where(region => region.SourceId == sourceId && regionKeys.Contains(region.Key))
            .ToListAsync(cancellationToken);

        var existingByKey = existingRegions.ToDictionary(region => region.Key, region => region, StringComparer.Ordinal);

        var hasNewRegion = false;
        foreach (var desiredRegion in desiredRegionsByKey)
        {
            if (existingByKey.TryGetValue(desiredRegion.Key, out var existingRegion))
            {
                existingRegion.DisplayName = desiredRegion.Value;
                continue;
            }

            var entity = new RegionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                Key = desiredRegion.Key,
                DisplayName = desiredRegion.Value,
                CreatedAtUtc = nowUtc,
            };

            _dbContext.Regions.Add(entity);
            existingByKey[desiredRegion.Key] = entity;
            hasNewRegion = true;
        }

        if (hasNewRegion)
        {
            _logger.LogInformation(
                "Detected {NewRegions} new regions for source {SourceId}.",
                desiredRegionsByKey.Count - existingRegions.Count,
                sourceId);
        }
    }

    private async Task<int> UpsertDonationCentersAsync(
        Guid sourceId,
        IReadOnlyCollection<DadorInstitutionRecord> institutions,
        IReadOnlyDictionary<string, Guid> regionIdByKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (institutions.Count == 0)
        {
            return 0;
        }

        var externalIds = institutions.Select(entry => entry.ExternalId).ToArray();
        var existingRows = await _dbContext.DonationCenters
            .Where(entry => entry.SourceId == sourceId && externalIds.Contains(entry.ExternalId))
            .ToListAsync(cancellationToken);

        var existingByExternalId = existingRows.ToDictionary(entry => entry.ExternalId, entry => entry, StringComparer.Ordinal);

        var upsertedCount = 0;
        foreach (var institution in institutions)
        {
            if (!regionIdByKey.TryGetValue(institution.RegionKey, out var regionId))
            {
                continue;
            }

            if (existingByExternalId.TryGetValue(institution.ExternalId, out var existing))
            {
                existing.RegionId = regionId;
                existing.InstitutionCode = institution.InstitutionCode;
                existing.Name = institution.Name;
                existing.DistrictCode = institution.DistrictCode;
                existing.DistrictName = institution.DistrictName;
                existing.MunicipalityCode = institution.MunicipalityCode;
                existing.MunicipalityName = institution.MunicipalityName;
                existing.Address = institution.Address;
                existing.Latitude = institution.Latitude;
                existing.Longitude = institution.Longitude;
                existing.PlusCode = institution.PlusCode;
                existing.Schedule = institution.Schedule;
                existing.Phone = institution.Phone;
                existing.MobilePhone = institution.MobilePhone;
                existing.Email = institution.Email;
                existing.UpdatedAtUtc = nowUtc;
                upsertedCount++;
                continue;
            }

            _dbContext.DonationCenters.Add(new DonationCenterEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                RegionId = regionId,
                ExternalId = institution.ExternalId,
                InstitutionCode = institution.InstitutionCode,
                Name = institution.Name,
                DistrictCode = institution.DistrictCode,
                DistrictName = institution.DistrictName,
                MunicipalityCode = institution.MunicipalityCode,
                MunicipalityName = institution.MunicipalityName,
                Address = institution.Address,
                Latitude = institution.Latitude,
                Longitude = institution.Longitude,
                PlusCode = institution.PlusCode,
                Schedule = institution.Schedule,
                Phone = institution.Phone,
                MobilePhone = institution.MobilePhone,
                Email = institution.Email,
                UpdatedAtUtc = nowUtc,
            });

            upsertedCount++;
        }

        return upsertedCount;
    }

    private async Task<int> UpsertCollectionSessionsAsync(
        Guid sourceId,
        IReadOnlyCollection<DadorSessionRecord> sessions,
        IReadOnlyDictionary<string, Guid> regionIdByKey,
        IReadOnlyDictionary<string, Guid> centerIdByInstitutionCode,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return 0;
        }

        var externalIds = sessions.Select(entry => entry.ExternalId).ToArray();
        var existingRows = await _dbContext.CollectionSessions
            .Where(entry => entry.SourceId == sourceId && externalIds.Contains(entry.ExternalId))
            .ToListAsync(cancellationToken);

        var existingByExternalId = existingRows.ToDictionary(entry => entry.ExternalId, entry => entry, StringComparer.Ordinal);

        var upsertedCount = 0;
        foreach (var session in sessions)
        {
            if (!regionIdByKey.TryGetValue(session.RegionKey, out var regionId))
            {
                continue;
            }

            var donationCenterId = centerIdByInstitutionCode.TryGetValue(session.InstitutionCode, out var knownCenterId)
                ? knownCenterId
                : (Guid?)null;

            if (existingByExternalId.TryGetValue(session.ExternalId, out var existing))
            {
                existing.RegionId = regionId;
                existing.DonationCenterId = donationCenterId;
                existing.InstitutionCode = session.InstitutionCode;
                existing.InstitutionName = session.InstitutionName;
                existing.DistrictCode = session.DistrictCode;
                existing.DistrictName = session.DistrictName;
                existing.MunicipalityCode = session.MunicipalityCode;
                existing.MunicipalityName = session.MunicipalityName;
                existing.Location = session.Location;
                existing.Latitude = session.Latitude;
                existing.Longitude = session.Longitude;
                existing.SessionDate = session.SessionDate;
                existing.SessionHours = session.SessionHours;
                existing.AccessCode = session.AccessCode;
                existing.StateCode = session.StateCode;
                existing.SessionTypeCode = session.SessionTypeCode;
                existing.SessionTypeName = session.SessionTypeName;
                existing.UpdatedAtUtc = nowUtc;
                upsertedCount++;
                continue;
            }

            _dbContext.CollectionSessions.Add(new CollectionSessionEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                RegionId = regionId,
                DonationCenterId = donationCenterId,
                ExternalId = session.ExternalId,
                InstitutionCode = session.InstitutionCode,
                InstitutionName = session.InstitutionName,
                DistrictCode = session.DistrictCode,
                DistrictName = session.DistrictName,
                MunicipalityCode = session.MunicipalityCode,
                MunicipalityName = session.MunicipalityName,
                Location = session.Location,
                Latitude = session.Latitude,
                Longitude = session.Longitude,
                SessionDate = session.SessionDate,
                SessionHours = session.SessionHours,
                AccessCode = session.AccessCode,
                StateCode = session.StateCode,
                SessionTypeCode = session.SessionTypeCode,
                SessionTypeName = session.SessionTypeName,
                UpdatedAtUtc = nowUtc,
            });

            upsertedCount++;
        }

        return upsertedCount;
    }

    private async Task PersistReserveHistoryObservationsAsync(
        IReadOnlyCollection<PendingHistoryObservation> pendingHistory,
        CancellationToken cancellationToken)
    {
        if (pendingHistory.Count == 0)
        {
            return;
        }

        var uniquePending = pendingHistory
            .GroupBy(entry => new HistoryObservationKey(
                entry.SourceId,
                entry.RegionId,
                entry.MetricKey,
                entry.ReferenceDate,
                entry.StatusKey))
            .Select(group => group.First())
            .ToArray();

        if (uniquePending.Length == 0)
        {
            return;
        }

        var candidateSourceIds = uniquePending.Select(entry => entry.SourceId).Distinct().ToArray();
        var candidateRegionIds = uniquePending.Select(entry => entry.RegionId).Distinct().ToArray();
        var candidateMetricKeys = uniquePending.Select(entry => entry.MetricKey).Distinct(StringComparer.Ordinal).ToArray();
        var candidateReferenceDates = uniquePending.Select(entry => entry.ReferenceDate).Distinct().ToArray();
        var candidateStatusKeys = uniquePending.Select(entry => entry.StatusKey).Distinct(StringComparer.Ordinal).ToArray();

        var existingKeys = await _dbContext.ReserveHistoryObservations
            .AsNoTracking()
            .Where(entry =>
                candidateSourceIds.Contains(entry.SourceId)
                && candidateRegionIds.Contains(entry.RegionId)
                && candidateMetricKeys.Contains(entry.MetricKey)
                && candidateReferenceDates.Contains(entry.ReferenceDate)
                && candidateStatusKeys.Contains(entry.StatusKey))
            .Select(entry => new HistoryObservationKey(
                entry.SourceId,
                entry.RegionId,
                entry.MetricKey,
                entry.ReferenceDate,
                entry.StatusKey))
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet();
        foreach (var pending in uniquePending)
        {
            var key = new HistoryObservationKey(
                pending.SourceId,
                pending.RegionId,
                pending.MetricKey,
                pending.ReferenceDate,
                pending.StatusKey);

            if (existingKeySet.Contains(key))
            {
                continue;
            }

            _dbContext.ReserveHistoryObservations.Add(new ReserveHistoryObservationEntity
            {
                SourceId = pending.SourceId,
                RegionId = pending.RegionId,
                MetricKey = pending.MetricKey,
                StatusKey = pending.StatusKey,
                StatusRank = pending.StatusRank,
                ReferenceDate = pending.ReferenceDate,
                CapturedAtUtc = pending.CapturedAtUtc,
            });
        }
    }

    private async Task<IReadOnlyCollection<Event>> EvaluateRulesAsync(
        Snapshot? previousSnapshot,
        Snapshot currentSnapshot,
        CancellationToken cancellationToken)
    {
        if (_rules.Count == 0)
        {
            return [];
        }

        var events = new List<Event>();
        foreach (var rule in _rules)
        {
            var ruleEvents = await rule.EvaluateAsync(previousSnapshot, currentSnapshot, cancellationToken);
            if (ruleEvents.Count == 0)
            {
                continue;
            }

            events.AddRange(ruleEvents);
        }

        return events;
    }

    private async Task<IReadOnlyCollection<EventEntity>> PersistEventsAsync(
        Guid sourceId,
        IReadOnlyCollection<Event> generatedEvents,
        IReadOnlyDictionary<string, Guid> regionIdByKey,
        IReadOnlyDictionary<CurrentReserveKey, CurrentReserveEntity> currentByKey,
        CancellationToken cancellationToken)
    {
        if (generatedEvents.Count == 0)
        {
            return [];
        }

        var pending = new List<PendingEvent>();
        foreach (var generatedEvent in generatedEvents)
        {
            if (generatedEvent.Region is null)
            {
                continue;
            }

            if (!regionIdByKey.TryGetValue(generatedEvent.Region.Key, out var regionId))
            {
                continue;
            }

            var reserveKey = new CurrentReserveKey(regionId, generatedEvent.Metric.Key);
            if (!currentByKey.TryGetValue(reserveKey, out var reserve))
            {
                continue;
            }

            var idempotencyKey = ComputeIdempotencyKey(generatedEvent);
            pending.Add(new PendingEvent(generatedEvent, idempotencyKey, regionId, reserve.Id));
        }

        if (pending.Count == 0)
        {
            return [];
        }

        var uniquePending = pending
            .GroupBy(entry => entry.IdempotencyKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var candidateKeys = uniquePending
            .Select(entry => entry.IdempotencyKey)
            .ToArray();

        var existingKeys = await _dbContext.Events
            .AsNoTracking()
            .Where(entry => candidateKeys.Contains(entry.IdempotencyKey))
            .Select(entry => entry.IdempotencyKey)
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        var insertedEvents = new List<EventEntity>();
        foreach (var pendingEvent in uniquePending)
        {
            if (existingKeySet.Contains(pendingEvent.IdempotencyKey))
            {
                continue;
            }

            var entity = new EventEntity
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                CurrentReserveId = pendingEvent.CurrentReserveId,
                RegionId = pendingEvent.RegionId,
                RuleKey = pendingEvent.Event.RuleKey,
                MetricKey = pendingEvent.Event.Metric.Key,
                IdempotencyKey = pendingEvent.IdempotencyKey,
                PayloadJson = pendingEvent.Event.PayloadJson,
                CreatedAtUtc = pendingEvent.Event.CreatedAtUtc,
            };

            _dbContext.Events.Add(entity);
            insertedEvents.Add(entity);
        }

        return insertedEvents;
    }

    private static Snapshot BuildSnapshot(
        SourceRef source,
        DateTime capturedAtUtc,
        DateOnly? referenceDate,
        IEnumerable<SnapshotReserveRow> rows,
        IReadOnlyDictionary<Guid, RegionRef> regionById,
        DateTime? sourceUpdatedAtUtc)
    {
        var items = rows
            .Select(row =>
            {
                if (!regionById.TryGetValue(row.RegionId, out var region))
                {
                    return null;
                }

                return new SnapshotItem(
                    new Metric(row.MetricKey, row.MetricKey),
                    region,
                    row.StatusKey,
                    row.StatusLabel,
                    Value: null,
                    Unit: null);
            })
            .Where(item => item is not null)
            .Cast<SnapshotItem>()
            .OrderBy(item => item.Region.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Metric.Key, StringComparer.Ordinal)
            .ToArray();

        return new Snapshot(source, capturedAtUtc, referenceDate, items, sourceUpdatedAtUtc);
    }

    private static string ComputeIdempotencyKey(Event @event)
    {
        var idempotencySeed = ExtractIdempotencySeed(@event.PayloadJson);
        var builder = new StringBuilder();
        builder
            .Append(@event.RuleKey).Append('|')
            .Append(@event.Source.AdapterKey).Append('|')
            .Append(@event.Region?.Key ?? "global").Append('|')
            .Append(@event.Metric.Key).Append('|')
            .Append(idempotencySeed.Signal).Append('|')
            .Append(idempotencySeed.TransitionKind).Append('|')
            .Append(idempotencySeed.CurrentStatusKey).Append('|')
            .Append(idempotencySeed.CapturedAtUtc ?? "none");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? TryReadStringFromPayload(string payloadJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return TryReadString(root, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IdempotencySeed ExtractIdempotencySeed(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var signal = TryReadString(root, "signal") ?? "unknown";
            var transitionKind = TryReadString(root, "transitionKind") ?? "unknown";
            var currentStatusKey = TryReadString(root, "currentStatusKey") ?? "unknown";
            var capturedAtUtc = TryReadString(root, "capturedAtUtc");

            return new IdempotencySeed(
                Signal: signal,
                TransitionKind: transitionKind,
                CurrentStatusKey: currentStatusKey,
                CapturedAtUtc: capturedAtUtc);
        }
        catch (JsonException)
        {
            return new IdempotencySeed(
                Signal: "unknown",
                TransitionKind: "unknown",
                CurrentStatusKey: "unknown",
                CapturedAtUtc: null);
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static Dictionary<CurrentReserveKey, IncomingCurrentReserve> BuildIncomingReserves(
        Snapshot snapshot,
        IReadOnlyDictionary<string, Guid> regionsByKey)
    {
        return snapshot.Items
            .Select(item =>
            {
                if (!regionsByKey.TryGetValue(item.Region.Key, out var regionId))
                {
                    return null;
                }

                return new IncomingCurrentReserve(
                    new CurrentReserveKey(regionId, item.Metric.Key),
                    ReserveStatusCatalog.NormalizeKey(item.StatusKey),
                    string.IsNullOrWhiteSpace(item.StatusLabel)
                        ? ReserveStatusCatalog.GetLabel(item.StatusKey)
                        : item.StatusLabel);
            })
            .Where(item => item is not null)
            .Cast<IncomingCurrentReserve>()
            .GroupBy(item => item.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => ReserveStatusCatalog.GetRank(item.StatusKey))
                    .First());
    }

    private sealed record PendingEvent(Event Event, string IdempotencyKey, Guid RegionId, Guid CurrentReserveId);

    private sealed record HistoryObservationKey(
        Guid SourceId,
        Guid RegionId,
        string MetricKey,
        DateOnly ReferenceDate,
        string StatusKey);

    private sealed record PendingHistoryObservation(
        Guid SourceId,
        Guid RegionId,
        string MetricKey,
        string StatusKey,
        short StatusRank,
        DateOnly ReferenceDate,
        DateTime CapturedAtUtc);

    private sealed record IdempotencySeed(
        string Signal,
        string TransitionKind,
        string CurrentStatusKey,
        string? CapturedAtUtc);

    private sealed record CurrentReserveKey(Guid RegionId, string MetricKey);

    private sealed record IncomingCurrentReserve(
        CurrentReserveKey Key,
        string StatusKey,
        string StatusLabel);

    private sealed record SnapshotReserveRow(
        Guid RegionId,
        string MetricKey,
        string StatusKey,
        string StatusLabel);
}
