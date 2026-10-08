namespace Schedulite.Abstractions;

/// <summary>Describes a recurring background job schedule supplied by the application.</summary>
public record BackgroundJobSchedule
{
    /// <summary>Gets the stable, unique identifier of this schedule.</summary>
    public required string ScheduleId { get; init; }
    /// <summary>Gets the registered identifier of the job to run.</summary>
    public required string JobId { get; init; }
    /// <summary>Gets the identifier of the subject passed to the job.</summary>
    public required string? SubjectId { get; init; }
    /// <summary>Gets the application-defined rule interpreted by the schedule provider.</summary>
    public required string ScheduleRule { get; init; }
    /// <summary>Gets whether this schedule should be considered for execution. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; init; } = true;
}
