using Schedulite.Abstractions;

namespace Schedulite.Execution;

internal sealed record BackgroundJobExecutionRequest(
    Guid ExecutionId,
    string JobId,
    string SubjectId,
    string? ScheduleId,
    BackgroundJobTrigger Trigger
);
