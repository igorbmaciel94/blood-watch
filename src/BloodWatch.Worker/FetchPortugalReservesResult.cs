namespace BloodWatch.Worker;

public sealed record FetchPortugalReservesResult(
    int InsertedCurrentReserves,
    int UpdatedCurrentReserves,
    int CarriedForwardCurrentReserves,
    DateTime PolledAtUtc);
