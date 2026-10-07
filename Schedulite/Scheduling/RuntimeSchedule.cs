using Schedulite.Abstractions;

namespace Schedulite.Scheduling;

internal sealed class RuntimeSchedule
{
    public required BackgroundJobSchedule Schedule { get; init; }
    public required DateTimeOffset NextExecution { get; set; }
}
