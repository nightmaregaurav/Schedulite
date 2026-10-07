using Scalar.AspNetCore;
using Schedulite.DependencyInjection;
using Schedulite.Example.BackgroundJobs;
using Schedulite.Example.BackgroundJobs.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


// Enable controllers
builder.Services.AddControllers();
builder.Services.AddSchedulite<ExampleScheduleProvider>(options =>
{
    options.MaxConcurrency = 2;
    options.ExecutionQueueCapacity = 16;
});
builder.Services.AddScheduliteJob<SayHiJob>("say-hi");
builder.Services.AddScheduliteJob<SkellyJob>("skelly");

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Build the application
var app = builder.Build();

// Map OpenAPI and Scalar API reference in development environment
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
// Enable authorization middleware
app.UseAuthorization();
// Map controller routes
app.MapControllers();
// Run the application
app.Run();
