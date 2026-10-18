using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carousel.Application.Abstractions;

/// <summary>
/// Maps the raw Bot API update JSON (snake_case: update_id, message_id, chat.id,
/// business_connection, callback_query) onto the flat records. Without this mapping
/// live updates deserialize to defaults (System.Text.Json does not match underscores).
/// </summary>
public sealed class TelegramUpdateJsonConverter : JsonConverter<TelegramUpdate>
{
    public override TelegramUpdate Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var updateId = ReadInt64(root, "update_id");
        var message = ReadMessage(root);
        var callback = ReadCallback(root);
        var connection = ReadBusinessConnection(root);
        return new TelegramUpdate(updateId, message, callback, connection);
    }

    public override void Write(
        Utf8JsonWriter writer, TelegramUpdate value, JsonSerializerOptions options) =>
        throw new NotSupportedException("TelegramUpdate is read-only from the wire.");

    private static TelegramMessage? ReadMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var from = m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
        var chat = m.TryGetProperty("chat", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;

        return new TelegramMessage(
            ReadInt64(m, "message_id"),
            chat.ValueKind == JsonValueKind.Object ? ReadInt64(chat, "id") : 0,
            from.ValueKind == JsonValueKind.Object ? ReadInt64(from, "id") : 0,
            from.ValueKind == JsonValueKind.Object ? ReadString(from, "username") : null,
            from.ValueKind == JsonValueKind.Object ? ReadString(from, "first_name") : null,
            ReadString(m, "text") ?? string.Empty,
            ReadSuccessfulPayment(m));
    }

    private static TelegramSuccessfulPayment? ReadSuccessfulPayment(JsonElement m)
    {
        if (!m.TryGetProperty("successful_payment", out var sp) || sp.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new TelegramSuccessfulPayment(
            ReadString(sp, "invoice_payload") ?? string.Empty,
            ReadInt64(sp, "total_amount"),
            ReadString(sp, "currency") ?? string.Empty,
            ReadString(sp, "telegram_payment_charge_id") ?? string.Empty);
    }

    private static TelegramCallbackQuery? ReadCallback(JsonElement root)
    {
        if (!root.TryGetProperty("callback_query", out var cb) || cb.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var from = cb.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
        return new TelegramCallbackQuery(
            ReadString(cb, "id") ?? string.Empty,
            from.ValueKind == JsonValueKind.Object ? ReadInt64(from, "id") : 0,
            (from.ValueKind == JsonValueKind.Object ? ReadString(from, "username") : null) ?? string.Empty,
            ReadString(cb, "data") ?? string.Empty);
    }

    private static TelegramBusinessConnection? ReadBusinessConnection(JsonElement root)
    {
        if (!root.TryGetProperty("business_connection", out var bc) || bc.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var user = bc.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        var enabled = true;
        if (bc.TryGetProperty("is_enabled", out var flag) && flag.ValueKind == JsonValueKind.False)
        {
            enabled = false;
        }

        return new TelegramBusinessConnection(
            ReadString(bc, "id") ?? string.Empty,
            user.ValueKind == JsonValueKind.Object ? ReadInt64(user, "id") : 0,
            user.ValueKind == JsonValueKind.Object ? ReadString(user, "username") : null,
            enabled);
    }

    private static long ReadInt64(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : 0;

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}