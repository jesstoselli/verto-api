using System.Net;
using FluentAssertions;
using Verto.Api.Tests.Infrastructure;

namespace Verto.Api.Tests.Sync;

public sealed class SyncPullTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SyncPullTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Pull_FromZero_ReturnsPushedRecordsWithPayload()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "regar as plantas"));

        var pull = await SyncTestData.PullAsync(client, since: 0);

        pull.HasMore.Should().BeFalse();
        pull.Changes.Should().ContainSingle();
        pull.Changes[0].Id.Should().Be(id);
        pull.Changes[0].EntityType.Should().Be("inbox_item");
        pull.Changes[0].Payload.GetProperty("text").GetString().Should().Be("regar as plantas");
        pull.Cursor.Should().Be(pull.Changes[0].ServerVersion);
    }

    [Fact]
    public async Task Pull_WithCursor_ReturnsOnlyLaterChanges()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(Guid.NewGuid(), "antes"));
        var first = await SyncTestData.PullAsync(client, since: 0);
        var laterId = Guid.NewGuid();
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(laterId, "depois"));

        var second = await SyncTestData.PullAsync(client, since: first.Cursor);

        second.Changes.Select(c => c.Id).Should().Equal(laterId);
    }

    [Fact]
    public async Task Pull_EmptyPage_KeepsCursor()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);

        var pull = await SyncTestData.PullAsync(client, since: 42);

        pull.Changes.Should().BeEmpty();
        pull.Cursor.Should().Be(42);
        pull.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Pull_WithLimit_PaginatesWithHasMore()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        await SyncTestData.PushAsync(
            client,
            SyncTestData.InboxItem(Guid.NewGuid(), "1"),
            SyncTestData.InboxItem(Guid.NewGuid(), "2"),
            SyncTestData.InboxItem(Guid.NewGuid(), "3"));

        var page1 = await SyncTestData.PullAsync(client, since: 0, limit: 2);
        var page2 = await SyncTestData.PullAsync(client, since: page1.Cursor, limit: 2);

        page1.Changes.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page2.Changes.Should().ContainSingle();
        page2.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Pull_IncludesTombstones()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "vai sumir"));
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "vai sumir", deletedAt: DateTimeOffset.UtcNow));

        var pull = await SyncTestData.PullAsync(client, since: 0);

        pull.Changes.Should().ContainSingle();
        pull.Changes[0].DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Pull_SameIdPushedTwice_ArrivalOrderWins_EvenWithOlderClientClock()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "relógio certo", clientUpdatedAt: now));
        await SyncTestData.PushAsync(client, SyncTestData.InboxItem(id, "relógio atrasado", clientUpdatedAt: now.AddDays(-1)));

        var pull = await SyncTestData.PullAsync(client, since: 0);

        pull.Changes.Should().ContainSingle();
        pull.Changes[0].Payload.GetProperty("text").GetString().Should().Be("relógio atrasado");
    }

    [Fact]
    public async Task Pull_SameIdFromAnotherUser_IsIsolated()
    {
        using var alice = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        using var bob = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        var sharedId = Guid.NewGuid();
        await SyncTestData.PushAsync(alice, SyncTestData.InboxItem(sharedId, "da alice"));
        await SyncTestData.PushAsync(bob, SyncTestData.InboxItem(sharedId, "do bob"));

        var alicePull = await SyncTestData.PullAsync(alice, since: 0);
        var bobPull = await SyncTestData.PullAsync(bob, since: 0);

        alicePull.Changes.Should().ContainSingle().Which.Payload.GetProperty("text").GetString().Should().Be("da alice");
        bobPull.Changes.Should().ContainSingle().Which.Payload.GetProperty("text").GetString().Should().Be("do bob");
    }

    [Fact]
    public async Task Pull_AfterConcurrentPushes_ReturnsEveryChangeInVersionOrder()
    {
        using var deviceA = await AuthTestClient.CreateAuthorizedClientAsync(_factory);
        using var deviceB = _factory.CreateClient();
        deviceB.DefaultRequestHeaders.Authorization = deviceA.DefaultRequestHeaders.Authorization;

        var pushes = Enumerable.Range(0, 20).Select(i =>
            SyncTestData.PushAsync(i % 2 == 0 ? deviceA : deviceB, SyncTestData.InboxItem(Guid.NewGuid(), "item " + i)));
        await Task.WhenAll(pushes);

        var pull = await SyncTestData.PullAsync(deviceA, since: 0, limit: 1000);

        pull.Changes.Should().HaveCount(20);
        pull.Changes.Select(c => c.ServerVersion).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Pull_NegativeSince_Returns400()
    {
        using var client = await AuthTestClient.CreateAuthorizedClientAsync(_factory);

        var response = await client.GetAsync(new Uri("/v1/sync/pull?since=-1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
