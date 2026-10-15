using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Verto.Application.Dtos.Auth;

namespace Verto.Api.Tests.Infrastructure;

public static class AuthTestClient
{
    public const string Password = "strong-pass-123";

    public static string NewEmail() =>
        "user-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";

    public static async Task<AuthResponse> SignupAsync(HttpClient client, string? email = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup",
            new SignupRequest(email ?? NewEmail(), Password, "Tester"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(ApiWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();
        var auth = await SignupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }
}
