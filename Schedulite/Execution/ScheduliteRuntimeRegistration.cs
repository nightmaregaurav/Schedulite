namespace Schedulite.Execution;

/// <summary>Connects a runtime identity to its registered jobs for manager discovery.</summary>
internal sealed record ScheduliteRuntimeRegistration(string? InstanceId, BackgroundJobRegistry JobRegistry);
