using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FaliNamIgrac;

public enum NotificationDestination
{
    GameDetails,
    Chat,
    RatePlayers,
}

public sealed record NotificationTarget(NotificationDestination Destination, string Id);

public sealed record NotificationIntent(string DeliveryId, NotificationTarget Target);

public static class NotificationPayload
{
    // Only recognized route inputs survive. No URLs, participant claims or sender identities are trusted.
    public static bool TryParse(
        IReadOnlyDictionary<string, string> data,
        string? deliveryId,
        out NotificationIntent? intent
    )
    {
        intent = null;
        if (
            data.Count > 64
            || data.Any(p => p.Key.Length > 128 || p.Value is null || p.Value.Length > 4096)
        )
            return false;
        string Get(string key) => data.TryGetValue(key, out var value) ? value.Trim() : "";
        var type = Get("type").ToLowerInvariant();
        var argumentType = Get("arg_type").ToLowerInvariant();
        NotificationTarget? target = null;
        if (
            argumentType
            is "new_appointment"
                or "status_changed"
                or "game_reminder"
                or "new_player_applied"
        )
        {
            if (type.Length != 0 || !GameId(Get("arg_gameId"), out var id))
                return false;
            target = new(NotificationDestination.GameDetails, id);
        }
        else if (argumentType.Length != 0)
            return false;
        else if (type == "message")
        {
            var id = Get("chatId");
            if (
                id.Length is < 1 or > 128
                || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            )
                return false;
            target = new(NotificationDestination.Chat, id);
        }
        else if (type is "rating_reminder" or "rate_players" or "game_details")
        {
            if (!GameId(Get("gameId"), out var id))
                return false;
            target = new(
                type == "game_details"
                    ? NotificationDestination.GameDetails
                    : NotificationDestination.RatePlayers,
                id
            );
        }
        if (target is null)
            return false;
        deliveryId = string.IsNullOrWhiteSpace(deliveryId)
            ? Get("google.message_id")
            : deliveryId.Trim();
        if (deliveryId.Length == 0)
            deliveryId = Get("gcm.message_id");
        if (deliveryId.Length > 256 || deliveryId.Any(char.IsControl))
            return false;
        // Older local notifications have no transport ID: stable route identity conservatively dedupes them.
        if (deliveryId.Length == 0)
            deliveryId =
                "route:"
                + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{target.Destination}:{target.Id}"))
                );
        intent = new(deliveryId, target);
        return true;
    }

    public static bool TryParseUri(string value, out NotificationIntent? intent)
    {
        intent = null;
        if (value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        if (
            uri.Scheme != "https"
            || uri.Host != "app.falinamigrac.com"
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
        )
            return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var gameId = parts switch
        {
            ["game", var valueId] => valueId,
            ["home", "game", var valueId] => valueId,
            _ => "",
        };
        if (!GameId(gameId, out var id))
            return false;
        intent = new(
            "link:" + Guid.NewGuid().ToString("N"),
            new(NotificationDestination.GameDetails, id)
        );
        return true;
    }

    public static string ToLocalPayload(NotificationIntent intent)
    {
        var type = intent.Target.Destination switch
        {
            NotificationDestination.Chat => "message",
            NotificationDestination.GameDetails => "game_details",
            NotificationDestination.RatePlayers => "rate_players",
            _ => throw new ArgumentOutOfRangeException(nameof(intent)),
        };
        return JsonSerializer.Serialize(
            new NotificationLocalPayload(
                type,
                type == "message" ? null : intent.Target.Id,
                type == "message" ? intent.Target.Id : null,
                intent.DeliveryId
            ),
            NotificationJsonContext.Default.NotificationLocalPayload
        );
    }

    public static bool TryParseLocal(string? json, out NotificationIntent? intent)
    {
        intent = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (
                    field.Value.ValueKind != JsonValueKind.String
                    || !data.TryAdd(field.Name, field.Value.GetString()!)
                )
                    return false;
            }
            return TryParse(data, data.GetValueOrDefault("deliveryId"), out intent);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool GameId(string value, out string normalized)
    {
        normalized = "";
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            return false;
        normalized = id.ToString("D");
        return true;
    }
}

public enum ForegroundNotificationAction
{
    Ignore,
    ShowLocalNotification,
    ChatHandledByRealtime,
}

public static class NotificationForegroundPolicy
{
    public static ForegroundNotificationAction Decide(
        NotificationIntent? intent,
        bool authenticated,
        string? activeChatId
    )
    {
        if (!authenticated || intent is null)
            return ForegroundNotificationAction.Ignore;
        if (intent.Target.Destination != NotificationDestination.Chat)
            return ForegroundNotificationAction.ShowLocalNotification;
        // Flutter suppresses ALL foreground chat pushes; subscriptions own unread state and active-room updates.
        return intent.Target.Id == activeChatId
            ? ForegroundNotificationAction.Ignore
            : ForegroundNotificationAction.ChatHandledByRealtime;
    }
}
