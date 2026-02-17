namespace BloodWatch.Worker;

public sealed class FetchPortugalReservesOptions
{
    public const string SectionName = "BloodWatch:Worker:FetchPortugalReserves";

    public int IntervalMinutes { get; set; } = 10;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, 24 * 60));
    }
}
