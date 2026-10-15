using Verto.Application.Dtos.Auth;
using Verto.Application.UseCases.Auth;

namespace Verto.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/auth").WithTags("Auth");

        group.MapPost("/signup", async (SignupRequest request, AuthService auth, CancellationToken ct) =>
            HttpResults.ToHttpResult(await auth.SignupAsync(request, ct)));

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
            HttpResults.ToHttpResult(await auth.LoginAsync(request, ct)));

        group.MapPost("/refresh", async (RefreshTokenRequest request, AuthService auth, CancellationToken ct) =>
            HttpResults.ToHttpResult(await auth.RefreshAsync(request, ct)));

        group.MapPost("/logout", async (LogoutRequest request, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LogoutAsync(request, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.ToHttpResult(result);
        });

        return app;
    }
}
