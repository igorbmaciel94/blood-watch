namespace BloodWatch.Worker;

public sealed record FetchPortugalReservesResult(
    int InsertedCurrentReserves,
    int UpdatedCurrentReserves,
    int CarriedForwardCurrentReserves,
    int UpsertedInstitutions,
    int UpsertedSessions,
    int GeneratedEvents,
    int DispatchCandidates,
    int SentDeliveries,
    long IngestDurationMs,
    long RulesDurationMs,
    long DispatchDurationMs,
    DateTime PolledAtUtc);
