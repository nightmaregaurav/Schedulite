namespace Schedulite.Abstractions;

public sealed record BackgroundJobContext
{
    public required Guid ExecutionId { get; init; }
    public required string JobId { get; init; }
    public required string SubjectId { get; init; }
    public string? ScheduleId { get; init; }
    public required BackgroundJobTrigger Trigger { get; init; }
}
