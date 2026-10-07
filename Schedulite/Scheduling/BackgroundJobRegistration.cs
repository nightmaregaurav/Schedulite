namespace Schedulite.Scheduling;

/// <summary>Associates a registered job identifier with its implementation type.</summary>
internal sealed record BackgroundJobRegistration(string Id, Type JobType);
