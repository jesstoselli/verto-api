using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Verto.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly IReadOnlyDictionary<string, string?> TestConfiguration =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Jwt:Issuer"] = "verto-tests",
            ["Jwt:Audience"] = "verto-tests",
            ["Jwt:Key"] = "integration-tests-only-key-with-at-least-32-characters",
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "90",
            ["Auth:AllowSignup"] = "true",
            // Exercises the same startup migration path production uses.
            ["Database:MigrateOnStartup"] = "true",
        };

    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine")
        .WithAutoRemove(true)
        .WithLabel("com.verto.test-suite", "api-integration")
        .Build();

    public ApiWebApplicationFactory()
        : this(new Dictionary<string, string?>(StringComparer.Ordinal))
    {
    }

    private ApiWebApplicationFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        _configurationOverrides = configurationOverrides;
    }

    public static ApiWebApplicationFactory Create(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        return new ApiWebApplicationFactory(configurationOverrides);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Production so minimal APIs answer malformed bodies with 400 instead of throwing.
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>(TestConfiguration, StringComparer.Ordinal)
            {
                ["ConnectionStrings:Postgres"] = _database.GetConnectionString(),
            };

            foreach (var item in _configurationOverrides)
            {
                settings[item.Key] = item.Value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        return base.CreateHost(builder);
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // Building the host runs Program.cs, which applies migrations.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await _database.DisposeAsync();
        }
    }
}
