using System.Text;
using System.Text.Json;
using Verto.Application.Dtos.Sync;

namespace Verto.Application.UseCases.Sync;

/// <summary>
/// Format-only validation. Field values are the client's business; the server
/// only guarantees each stored record has the shape the client will read back.
/// </summary>
public static class SyncEntityCatalog
{
    public const int MaxPayloadBytes = 64 * 1024;

    private static readonly Dictionary<string, string[]> RequiredFields = new(StringComparer.Ordinal)
    {
        ["inbox_item"] = ["text", "createdAt", "source", "state"],
        ["task"] = ["title", "pinnedToToday"],
        ["step"] = ["order", "title", "energy"],
        ["anchor"] = ["name", "windowStart", "windowEnd"],
        ["anchor_event"] = ["anchorId", "day", "triggeredAt", "source"],
        ["routine"] = ["name", "anchorId", "weekdays"],
        ["routine_log"] = ["routineId", "day", "outcome"],
        ["day_energy"] = ["day", "level", "setAt"],
    };

    public static string? Validate(SyncChangeIn change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (string.IsNullOrEmpty(change.EntityType) || !RequiredFields.TryGetValue(change.EntityType, out var required))
        {
            return "unknown_entity_type";
        }

        if (change.Id == Guid.Empty)
        {
            return "invalid_id";
        }

        if (change.ClientUpdatedAt == default)
        {
            return "missing_client_updated_at";
        }

        if (change.Payload.ValueKind != JsonValueKind.Object)
        {
            return "payload_must_be_object";
        }

        if (Encoding.UTF8.GetByteCount(change.Payload.GetRawText()) > MaxPayloadBytes)
        {
            return "payload_too_large";
        }

        // Tombstones may be sent with a minimal payload.
        if (change.DeletedAt is not null)
        {
            return null;
        }

        foreach (var field in required)
        {
            if (!change.Payload.TryGetProperty(field, out _))
            {
                return "missing_field:" + field;
            }
        }

        if (string.Equals(change.EntityType, "step", StringComparison.Ordinal) && !HasStepOwner(change.Payload))
        {
            return "missing_field:taskId|routineId+plan";
        }

        return null;
    }

    private static bool HasStepOwner(JsonElement payload)
    {
        return payload.TryGetProperty("taskId", out _)
            || (payload.TryGetProperty("routineId", out _) && payload.TryGetProperty("plan", out _));
    }
}
