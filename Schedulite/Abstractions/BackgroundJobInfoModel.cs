namespace Schedulite.Abstractions;

/// <summary>Describes a provider schedule and its current state in the runtime scheduler.</summary>
public class BackgroundJobInfoModel
{
    /// <summary>Gets or sets the runtime ID, or <see langword="null"/> for the default runtime.</summary>
    public required string? ScheduliteInstanceId { get; set; }

    /// <summary>Gets or sets the schedule ID, unique within its runtime.</summary>
    public required string ScheduleId { get; set; }

    /// <summary>Gets or sets the registered job ID for this schedule.</summary>
    public required string JobId { get; set; }

    /// <summary>Gets or sets the subject ID passed to executions created by this schedule.</summary>
    public required string? SubjectId { get; set; }

    /// <summary>Gets or sets the provider-defined schedule rule.</summary>
    public required string ScheduleRule { get; set; }

    /// <summary>Gets or sets whether the provider currently enables this schedule.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether this schedule ID is absent from the runtime scheduler's active schedule set.</summary>
    public bool NotQueued { get; set; }

    /// <summary>Gets or sets the next occurrence currently held by the runtime scheduler, or <see langword="null"/> when it is not queued.</summary>
    public DateTimeOffset? NextExecution { get; set; }
}
