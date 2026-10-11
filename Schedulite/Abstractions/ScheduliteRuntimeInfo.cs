namespace Schedulite.Abstractions;

/// <summary>Describes the jobs and configured schedules for one Schedulite runtime.</summary>
public sealed record ScheduliteRuntimeInfo
{
    /// <summary>Gets the runtime ID, or <see langword="null"/> for the default runtime.</summary>
    public required string? InstanceId { get; init; }

    /// <summary>Gets the IDs of all jobs registered in this runtime, including manual-only jobs.</summary>
    public required IReadOnlyCollection<string> JobIds { get; init; }

    /// <summary>Gets the schedules currently returned by this runtime's schedule provider.</summary>
    public required IReadOnlyCollection<BackgroundJobInfoModel> Schedules { get; init; }
}
