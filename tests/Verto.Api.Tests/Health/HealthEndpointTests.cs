using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;

namespace Verto.Api.Tests.Health;

public sealed class HealthEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public HealthEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_WithDatabaseUp_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
    }
}
