using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


// Enable controllers
builder.Services.AddControllers();

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
