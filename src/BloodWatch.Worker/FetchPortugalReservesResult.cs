namespace BloodWatch.Worker;

public sealed record FetchPortugalReservesResult(
    int InsertedSnapshots,
    int SkippedDuplicates,
    int InsertedItems);
