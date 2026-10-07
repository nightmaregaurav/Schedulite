using Schedulite.Abstractions;

namespace Schedulite.Execution;

/// <summary>Carries the identifiers and trigger metadata needed to execute one job.</summary>
internal sealed record BackgroundJobExecutionRequest(
    Guid ExecutionId,
    string JobId,
    string SubjectId,
    string? ScheduleId,
    BackgroundJobTrigger Trigger
);
