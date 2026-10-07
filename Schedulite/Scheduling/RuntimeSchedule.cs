using Schedulite.Abstractions;

namespace Schedulite.Scheduling;

/// <summary>Tracks a schedule and its next calculated occurrence while the scheduler runs.</summary>
internal sealed class RuntimeSchedule
{
    /// <summary>Gets the schedule represented by this runtime entry.</summary>
    public required BackgroundJobSchedule Schedule { get; init; }
    /// <summary>Gets or sets the next occurrence calculated for this schedule.</summary>
    public required DateTimeOffset NextExecution { get; set; }
}
