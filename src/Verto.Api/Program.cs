using Microsoft.EntityFrameworkCore;
using Verto.Infrastructure;
using Verto.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Single-instance deploy: migrating on boot is simpler than a separate migration job.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/v1/health", async (ApplicationDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return Results.Ok(new
    {
        status = canConnect ? "ok" : "degraded",
        version = "0.1.0",
        timestamp = DateTime.UtcNow,
    });
});

await app.RunAsync();

public partial class Program;
