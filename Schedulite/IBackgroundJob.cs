namespace Schedulite;

public interface IBackgroundJob
{
    public string JobName { get; }
    public string JobDescription { get; }
    public Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken);
}
