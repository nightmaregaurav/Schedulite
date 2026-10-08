namespace Schedulite.Example.Features.BackgroundJobManagement;

public class BackgroundJobModel
{
    public required string ScheduleId { get; set; }
    public required string JobId { get; set; }
    public required string? SubjectId { get; set; }
    public required string ScheduleRule { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset? NextExecution { get; set; }
}
