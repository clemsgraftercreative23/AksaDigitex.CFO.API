namespace MyBackend.Application.Options;

public sealed class OverdueEmailScheduleOptions
{
    public const string SectionName = "OverdueEmailSchedule";

    public bool Enabled { get; set; } = true;
    public int HourLocal { get; set; } = 8;
    public int MinuteLocal { get; set; } = 0;
    public string TimeZoneId { get; set; } = "Asia/Jakarta";
    public List<string> Recipients { get; set; } = [];
}
