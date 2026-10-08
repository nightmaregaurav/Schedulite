namespace Schedulite.Abstractions;

/// <summary>Defines which executions of a registered background job may run at the same time.</summary>
public enum ExecutionConcurrencyScope
{
    /// <summary>Executions may overlap regardless of job or subject.</summary>
    Unrestricted,

    /// <summary>Only one execution of a job may run at a time, regardless of subject.</summary>
    PerJob,

    /// <summary>Only one execution for a job and subject pair may run at a time.</summary>
    PerSubject
}
