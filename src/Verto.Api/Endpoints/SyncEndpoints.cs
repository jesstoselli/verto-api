using System.Security.Claims;
using Verto.Api.Extensions;
using Verto.Application.Dtos.Sync;
using Verto.Application.UseCases.Sync;

namespace Verto.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/sync").WithTags("Sync").RequireAuthorization();

        group.MapPost("/push", async (
            PushRequest request,
            ClaimsPrincipal principal,
            SyncService sync,
            CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
            {
                return Results.Unauthorized();
            }

            return HttpResults.ToHttpResult(await sync.PushAsync(userId, request, ct));
        });

        group.MapGet("/pull", async (
            long? since,
            int? limit,
            ClaimsPrincipal principal,
            SyncService sync,
            CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
            {
                return Results.Unauthorized();
            }

            return HttpResults.ToHttpResult(await sync.PullAsync(userId, since ?? 0, limit, ct));
        });

        return app;
    }
}
