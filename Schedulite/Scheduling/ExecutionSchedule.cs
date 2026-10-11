using Schedulite.Abstractions;

namespace Schedulite.Scheduling
{
    /// <summary>Represents an execution schedule that contains a background job schedule and the next execution timestamp.</summary>
    internal sealed class ExecutionSchedule(BackgroundJobSchedule schedule, DateTimeOffset nextExecution)
    {
        public BackgroundJobSchedule Schedule { get; } = schedule;
        public DateTimeOffset NextExecution { get; set; } = nextExecution;
    }
}
