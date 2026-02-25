namespace BloodWatch.Worker;

public sealed class FetchPortugalReservesOptions
{
    public const string SectionName = "BloodWatch:Worker:FetchPortugalReserves";
    private const int MaxIntervalMinutes = 7 * 24 * 60;

    public int IntervalMinutes { get; set; } = 10;
    public int ReminderIntervalHours { get; set; } = 72;

    public TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, MaxIntervalMinutes));
    }

    public TimeSpan GetReminderInterval()
    {
        return TimeSpan.FromHours(Math.Clamp(ReminderIntervalHours, 1, 24 * 60));
    }
}
