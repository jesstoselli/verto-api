using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Verto.Application.Abstractions.Auth;
using Verto.Application.Abstractions.Persistence;
using Verto.Application.UseCases.Auth;
using Verto.Application.Validators.Auth;
using Verto.Infrastructure.Auth;
using Verto.Infrastructure.Persistence;

namespace Verto.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Resolved lazily from the container so test hosts can inject the
        // connection string after Program.cs has started building services.
        services.AddSingleton(serviceProvider =>
        {
            var connectionString = serviceProvider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("Postgres");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
            }

            return new NpgsqlDataSourceBuilder(connectionString).Build();
        });
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<AuthService>();

        services.AddValidatorsFromAssemblyContaining<SignupRequestValidator>();

        return services;
    }
}
