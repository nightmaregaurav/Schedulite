using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;
using Schedulite.Configuration;
using Schedulite.DependencyInjection;
using Schedulite.Example.BackgroundJobs;
using Schedulite.Example.BackgroundJobs.Providers;
using Schedulite.Example.BackgroundJobs.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Schedulite", LogLevel.Debug);
builder.Services.AddControllers();
builder.Services.AddSchedulite<ExampleScheduleProvider>(
    new ScheduliteOptions {
        MaxConcurrency = 2,
        ExecutionQueueCapacity = 16
    }
);
builder.Services.AddScheduliteJob<SayHiJob>("say-hi");
builder.Services.AddScheduliteJob<SkellyJob>("skelly");
builder.Services.AddScheduliteJob<SendDigestJob>("send-digest");

builder.Services.AddSchedulite<MaintenanceScheduleProvider>("maintenance", new ScheduliteOptions
{
    MaxConcurrency = 1,
    ExecutionQueueCapacity = 8
});
builder.Services.AddScheduliteJob<CleanupExpiredDataJob>("maintenance", "cleanup-expired-data");

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
