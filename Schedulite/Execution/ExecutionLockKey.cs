namespace Schedulite.Execution;

/// <summary>Execution lock key for a job and optional subject. Used to limit concurrent executions of the same job or subject based on defined rule.</summary>
/// <param name="JobId">The job ID.</param>
/// <param name="SubjectId">The subject ID, or null if the concurrency rule is per-job.</param>
internal readonly record struct ExecutionLockKey(string JobId, string? SubjectId);
