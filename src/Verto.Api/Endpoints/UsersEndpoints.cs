using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Verto.Api.Extensions;
using Verto.Application.Abstractions.Persistence;
using Verto.Application.Dtos.Auth;

namespace Verto.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/me", async (ClaimsPrincipal principal, IApplicationDbContext db, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserDto(u.Id, u.Email, u.DisplayName))
                .SingleOrDefaultAsync(ct);

            return user is null ? Results.Unauthorized() : Results.Ok(user);
        })
        .WithTags("Users")
        .RequireAuthorization();

        return app;
    }
}
