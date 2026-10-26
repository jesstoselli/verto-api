using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;

namespace Verto.Api.Tests.Sync;

public sealed class SyncPushTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SyncPushTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Push_ValidRecords_ReturnsIncreasingServerVersions()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);

        var result = await SyncTestData.PushAsync(
            client,
            SyncTestData.InboxItem(Guid.NewGuid(), "comprar pão"),
            SyncTestData.InboxItem(Guid.NewGuid(), "ligar pro dentista"));

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().HaveCount(2);
        result.Accepted[1].ServerVersion.Should().BeGreaterThan(result.Accepted[0].ServerVersion);
    }

    [Fact]
    public async Task Push_OlderThanStored_IsRejectedAsStale_AndKeepsTheNewerRecord()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        var newer = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "feito no outro celular", newer));

        var result = await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "versão velha", newer.AddMinutes(-5)));

        result.Accepted.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Error.Should().Be("stale");
        var pull = await SyncTestData.PullAsync(client, since: 0);
        pull.Changes.Single(c => c.Id == id).Payload.GetProperty("text").GetString().Should().Be("feito no outro celular");
    }

    [Fact]
    public async Task Push_SameOrNewerThanStored_Overwrites()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "primeira", at));

        var same = await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "mesmo instante", at));
        var later = await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "depois", at.AddMinutes(1)));

        same.Accepted.Should().ContainSingle();
        later.Accepted.Should().ContainSingle();
        var pull = await SyncTestData.PullAsync(client, since: 0);
        pull.Changes.Single(c => c.Id == id).Payload.GetProperty("text").GetString().Should().Be("depois");
    }

    [Fact]
    public async Task Push_StaleChange_DoesNotBlockTheRestOfTheBatch()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "nova", at));

        var result = await SyncTestData.PushAsync(
            client,
            SyncTestData.InboxItem(id, "velha", at.AddHours(-1)),
            SyncTestData.InboxItem(Guid.NewGuid(), "outra coisa"));

        result.Accepted.Should().ContainSingle();
        result.Rejected.Should().ContainSingle().Which.Error.Should().Be("stale");
    }

    [Fact]
    public async Task Push_InvalidRecords_AreRejectedWithoutBlockingValidOnes()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var validId = Guid.NewGuid();
        var unknownTypeId = Guid.NewGuid();
        var missingFieldId = Guid.NewGuid();
        var orphanStepId = Guid.NewGuid();

        var result = await SyncTestData.PushAsync(
            client,
            SyncTestData.InboxItem(validId, "ok"),
            new { entityType = "calendar", id = unknownTypeId, payload = new { }, clientUpdatedAt = DateTimeOffset.UtcNow },
            new { entityType = "task", id = missingFieldId, payload = new { title = "IR" }, clientUpdatedAt = DateTimeOffset.UtcNow },
            new { entityType = "step", id = orphanStepId, payload = new { order = 1, title = "abrir site", energy = "low" }, clientUpdatedAt = DateTimeOffset.UtcNow });

        result.Accepted.Select(a => a.Id).Should().Equal(validId);
        result.Rejected.Should().ContainEquivalentOf(new { Id = unknownTypeId, Error = "unknown_entity_type" });
        result.Rejected.Should().ContainEquivalentOf(new { Id = missingFieldId, Error = "missing_field:pinnedToToday" });
        result.Rejected.Should().ContainEquivalentOf(new { Id = orphanStepId, Error = "missing_field:taskId|routineId+plan" });
    }

    [Fact]
    public async Task Push_Category_IsAccepted_AndNeedsNameAndColor()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var workId = Guid.NewGuid();
        var nameless = Guid.NewGuid();

        var result = await SyncTestData.PushAsync(
            client,
            new { entityType = "category", id = workId, payload = new { name = "Trabalho", color = "lagoon" }, clientUpdatedAt = DateTimeOffset.UtcNow },
            new { entityType = "category", id = nameless, payload = new { color = "violet" }, clientUpdatedAt = DateTimeOffset.UtcNow });

        result.Accepted.Select(a => a.Id).Should().Equal(workId);
        result.Rejected.Should().ContainEquivalentOf(new { Id = nameless, Error = "missing_field:name" });
    }

    [Fact]
    public async Task Push_Tombstone_SkipsRequiredFieldCheck()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();

        var result = await SyncTestData.PushAsync(
            client,
            new { entityType = "task", id, payload = new { }, deletedAt = DateTimeOffset.UtcNow, clientUpdatedAt = DateTimeOffset.UtcNow });

        result.Accepted.Select(a => a.Id).Should().Equal(id);
    }

    [Fact]
    public async Task Push_OversizedPayload_IsRejectedWithoutBlockingTheBatch()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var bigId = Guid.NewGuid();
        var smallId = Guid.NewGuid();

        var result = await SyncTestData.PushAsync(
            client,
            SyncTestData.InboxItem(bigId, new string('x', 70 * 1024)),
            SyncTestData.InboxItem(smallId, "pequeno"));

        result.Accepted.Select(a => a.Id).Should().Equal(smallId);
        result.Rejected.Should().ContainEquivalentOf(new { Id = bigId, Error = "payload_too_large" });
    }

    [Fact]
    public async Task Push_EmptyBatch_Returns400()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);

        var response = await client.PostAsJsonAsync("/v1/sync/push", new { changes = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_MoreThan500Changes_Returns400()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var changes = Enumerable.Range(0, 501).Select(i => SyncTestData.InboxItem(Guid.NewGuid(), "item")).ToArray();

        var response = await client.PostAsJsonAsync("/v1/sync/push", new { changes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_MalformedJson_Returns400()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        using var body = new StringContent("{\"changes\": [{\"entityType\": \"inbox_it", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/v1/sync/push", UriKind.Relative), body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/sync/push",
            new { changes = new[] { SyncTestData.InboxItem(Guid.NewGuid(), "x") } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("a\\u0000b")]
    [InlineData("\\ud800")]
    public async Task Push_TextPostgresCannotStore_IsRejectedWithoutBlockingTheBatch(string escapedText)
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var badId = Guid.NewGuid();
        var goodId = Guid.NewGuid();
        var json = "{\"changes\":["
            + "{\"entityType\":\"inbox_item\",\"id\":\"" + badId + "\",\"clientUpdatedAt\":\"2026-10-15T12:00:00Z\","
            + "\"payload\":{\"text\":\"" + escapedText + "\",\"createdAt\":\"x\",\"source\":\"share\",\"state\":\"raw\"}},"
            + "{\"entityType\":\"inbox_item\",\"id\":\"" + goodId + "\",\"clientUpdatedAt\":\"2026-10-15T12:00:00Z\","
            + "\"payload\":{\"text\":\"ok\",\"createdAt\":\"x\",\"source\":\"app\",\"state\":\"raw\"}}]}";
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/v1/sync/push", UriKind.Relative), body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<Verto.Application.Dtos.Sync.PushResponse>();
        result!.Accepted.Select(a => a.Id).Should().Equal(goodId);
        result.Rejected.Should().ContainEquivalentOf(new { Id = badId, Error = "invalid_payload" });
    }
}
