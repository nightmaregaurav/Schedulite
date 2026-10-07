namespace Schedulite.Example.Features.BackgroundJobManagement;

public class BackgroundJobModel
{
    public string ScheduleId { get; set; }
    public string JobId { get; set; }
    public string SubjectId { get; set; }
    public string ScheduleRule { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset? NextExecution { get; set; }
}
