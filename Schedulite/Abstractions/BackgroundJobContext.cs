namespace Schedulite.Abstractions;

/// <summary>Provides the identity and trigger information for one background job execution.</summary>
public sealed record BackgroundJobContext
{
    /// <summary>Gets the unique identifier assigned to this execution.</summary>
    public required Guid ExecutionId { get; init; }
    /// <summary>Gets the registered identifier of the job being executed.</summary>
    public required string JobId { get; init; }
    /// <summary>Gets the identifier of the subject the job is operating on.</summary>
    public required string? SubjectId { get; init; }
    /// <summary>Gets the identifier of the recurring schedule that created this execution, or <see langword="null"/> for a manual trigger.</summary>
    public string? ScheduleId { get; init; }
    /// <summary>Gets whether this execution was created by a schedule or requested manually.</summary>
    public required BackgroundJobTrigger Trigger { get; init; }
}
