namespace Schedulite.Abstractions;

public record BackgroundJobSchedule
{
    public required string ScheduleId { get; init; }
    public required string JobId { get; init; }
    public required string SubjectId { get; init; }
    public required string ScheduleRule { get; init; }
    public bool Enabled { get; init; } = true;
}
