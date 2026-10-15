using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;
using Verto.Application.Dtos.Auth;

namespace Verto.Api.Tests.Auth;

public sealed class AuthRateLimitTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthRateLimitTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_BurstOfParallelAttempts_IsShedWith429()
    {
        using var client = _factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        await AuthTestClient.SignupAsync(client, email);

        var attempts = Enumerable.Range(0, 20).Select(_ =>
            client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(email, "wrong-password-1")));
        var responses = await Task.WhenAll(attempts);

        responses.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.TooManyRequests);
        responses.Select(r => r.StatusCode).Should().OnlyContain(
            s => s == HttpStatusCode.TooManyRequests || s == HttpStatusCode.Unauthorized);
    }
}
