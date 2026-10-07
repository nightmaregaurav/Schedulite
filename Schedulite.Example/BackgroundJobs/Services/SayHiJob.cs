using System.Text.Json;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

public sealed class SayHiJob(TimeProvider timeProvider) : IBackgroundJob
{
    public string JobName => "Say Hi";
    public string JobDescription => "Says hi to the console and waits for 5 seconds to simulate work, then says bye.";

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{timeProvider.GetUtcNow():O}] Hi from the {nameof(SayHiJob)} background job! context: {JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        Console.WriteLine($"[{timeProvider.GetUtcNow():O}] Bye from the {nameof(SayHiJob)} background job.");
    }
}
