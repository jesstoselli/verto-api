using System.Net.Http.Json;
using Verto.Application.Dtos.Sync;

namespace Verto.Api.Tests.Sync;

public static class SyncTestData
{
    public static object InboxItem(
        Guid id,
        string text,
        DateTimeOffset? clientUpdatedAt = null,
        DateTimeOffset? deletedAt = null)
    {
        return new
        {
            entityType = "inbox_item",
            id,
            payload = new { text, createdAt = "2026-10-15T12:00:00Z", source = "app", state = "raw" },
            deletedAt,
            clientUpdatedAt = clientUpdatedAt ?? DateTimeOffset.UtcNow,
        };
    }

    public static async Task<PushResponse> PushAsync(HttpClient client, params object[] changes)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync("/v1/sync/push", new { changes });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PushResponse>())!;
    }
}
