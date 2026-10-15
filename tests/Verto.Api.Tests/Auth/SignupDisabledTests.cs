using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;
using Verto.Application.Dtos.Auth;

namespace Verto.Api.Tests.Auth;

public sealed class SignupDisabledTests
{
    [Fact]
    public async Task Signup_WhenDisabled_Returns403()
    {
        var factory = ApiWebApplicationFactory.Create(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Auth:AllowSignup"] = "false" });

        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(
                "/v1/auth/signup",
                new SignupRequest(AuthTestClient.NewEmail(), AuthTestClient.Password, "Tester"));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}
