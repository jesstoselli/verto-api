using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;
using Verto.Application.Dtos.Auth;

namespace Verto.Api.Tests.Auth;

public sealed class AuthEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Signup_ThenLogin_ReturnsTokensForSameUser()
    {
        using var client = _factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        var signup = await AuthTestClient.SignupAsync(client, email);

        var response = await client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(email, AuthTestClient.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<AuthResponse>();
        login!.User.Id.Should().Be(signup.User.Id);
        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Signup_DuplicateEmail_Returns409()
    {
        using var client = _factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        await AuthTestClient.SignupAsync(client, email);

        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup",
            new SignupRequest(email, AuthTestClient.Password, "Tester"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        using var client = _factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        await AuthTestClient.SignupAsync(client, email);

        var response = await client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(email, "wrong-password-1"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsCurrentUser()
    {
        using var client = _factory.CreateClient();
        var signup = await AuthTestClient.SignupAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signup.AccessToken);

        var me = await client.GetFromJsonAsync<UserDto>(new Uri("/v1/me", UriKind.Relative));

        me!.Id.Should().Be(signup.User.Id);
        me.Email.Should().Be(signup.User.Email);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenIsRejected()
    {
        using var client = _factory.CreateClient();
        var signup = await AuthTestClient.SignupAsync(client);

        var first = await client.PostAsJsonAsync("/v1/auth/refresh", new RefreshTokenRequest(signup.RefreshToken));
        var reused = await client.PostAsJsonAsync("/v1/auth/refresh", new RefreshTokenRequest(signup.RefreshToken));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await first.Content.ReadFromJsonAsync<AuthResponse>();
        rotated!.RefreshToken.Should().NotBe(signup.RefreshToken);
        reused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithPredecessorToken_RevokesSuccessor()
    {
        using var client = _factory.CreateClient();
        var signup = await AuthTestClient.SignupAsync(client);
        var refreshed = await client.PostAsJsonAsync("/v1/auth/refresh", new RefreshTokenRequest(signup.RefreshToken));
        var successor = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.RefreshToken;

        var logout = await client.PostAsJsonAsync("/v1/auth/logout", new LogoutRequest(signup.RefreshToken));
        var afterLogout = await client.PostAsJsonAsync("/v1/auth/refresh", new RefreshTokenRequest(successor));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
