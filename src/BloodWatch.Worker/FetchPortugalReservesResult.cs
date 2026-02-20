namespace BloodWatch.Worker;

public sealed record FetchPortugalReservesResult(
    int InsertedCurrentReserves,
    int UpdatedCurrentReserves,
    int CarriedForwardCurrentReserves,
    int UpsertedInstitutions,
    int UpsertedSessions,
    DateTime PolledAtUtc);
