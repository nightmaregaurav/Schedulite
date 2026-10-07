namespace Schedulite;

public interface IBackgroundJob
{
    string JobName { get; }
    string JobDescription { get; }
    Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken);
}
