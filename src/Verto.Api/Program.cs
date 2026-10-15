using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Verto.Api.Endpoints;
using Verto.Infrastructure;
using Verto.Infrastructure.Auth;
using Verto.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Matches nginx's client_max_body_size: a full push (500 x 64 KB payloads) exceeds Kestrel's 30 MB default.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 40 * 1024 * 1024);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, configuredJwt) =>
    {
        var jwtOpts = configuredJwt.Value;

        // Keeps RFC 7519 claim names (sub, email) instead of the legacy WS-Federation mapping.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOpts.Issuer,
            ValidAudience = jwtOpts.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Each Argon2id check allocates 64 MB; capping concurrency keeps a login burst
    // from exhausting the VPS that also runs BillFolder.
    options.AddConcurrencyLimiter(AuthEndpoints.RateLimitPolicy, limiter =>
    {
        limiter.PermitLimit = 2;
        limiter.QueueLimit = 4;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();

// Single-instance deploy: migrating on boot is simpler than a separate migration job.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapSyncEndpoints();

await app.RunAsync();

public partial class Program;
