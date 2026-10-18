using System.Text.Json;
using System.Text.Json.Serialization;
using Carousel.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

/// <summary>
/// Десериализация живого JSON Bot API (update_id, message_id, from.id, business_connection)
/// должна работать с теми же опциями, что у TelegramBotApi.JsonOptions.
/// </summary>
public class TelegramUpdateDeserializationTests
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Envelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public List<TelegramUpdate>? Result { get; set; }
    }

    [Fact]
    public void Live_message_json_maps_to_TelegramMessage()
    {
        const string json = """
            {"ok":true,"result":[{"update_id":987654,"message":{"message_id":55,"from":{"id":42,"username":"devuser","first_name":"Dev"},"chat":{"id":42},"text":"/start"}}]}
            """;

        var env = JsonSerializer.Deserialize<Envelope>(json, Opts);
        var update = env!.Result![0];

        update.UpdateId.Should().Be(987654);
        var msg = update.Message;
        msg.Should().NotBeNull();
        msg!.MessageId.Should().Be(55);
        msg.FromUserId.Should().Be(42);
        msg.ChatId.Should().Be(42);
        msg.Text.Should().Be("/start");
        msg.FromUsername.Should().Be("devuser");
    }

    [Fact]
    public void Live_business_connection_json_maps_to_TelegramBusinessConnection()
    {
        const string json = """
            {"ok":true,"result":[{"update_id":12,"business_connection":{"id":"conn-abc","user":{"id":77,"username":"owner"},"user_chat_id":77,"date":1700000000,"is_enabled":true}}]}
            """;

        var env = JsonSerializer.Deserialize<Envelope>(json, Opts);
        var bc = env!.Result![0].BusinessConnection;

        bc.Should().NotBeNull();
        bc!.Id.Should().Be("conn-abc");
        bc.UserId.Should().Be(77);
        bc.Username.Should().Be("owner");
        bc.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Live_successful_payment_json_maps_to_TelegramSuccessfulPayment()
    {
        const string json = """
            {"ok":true,"result":[{"update_id":5,"message":{"message_id":60,"from":{"id":42,"username":"devuser"},"chat":{"id":42},"text":"","successful_payment":{"currency":"XTR","total_amount":1,"invoice_payload":"pub:3f2a1111-2222-3333-4444-555566667777","telegram_payment_charge_id":"ch_abc"}}}]}
            """;

        var env = JsonSerializer.Deserialize<Envelope>(json, Opts);
        var msg = env!.Result![0].Message;

        msg.Should().NotBeNull();
        msg!.Text.Should().BeEmpty("у успешной оплаты текст пустой — не должна срабатывать текстовая логика");
        var sp = msg.SuccessfulPayment;
        sp.Should().NotBeNull();
        sp!.Payload.Should().Be("pub:3f2a1111-2222-3333-4444-555566667777");
        sp.Amount.Should().Be(1);
        sp.Currency.Should().Be("XTR");
        sp.ChargeId.Should().Be("ch_abc");
    }

    [Fact]
    public void Live_callback_json_maps_to_TelegramCallbackQuery()
    {
        const string json = """
            {"ok":true,"result":[{"update_id":77,"callback_query":{"id":"cbq-1","from":{"id":9,"username":"u"},"data":"open_preview:abc"}}]}
            """;

        var env = JsonSerializer.Deserialize<Envelope>(json, Opts);
        var cb = env!.Result![0].CallbackQuery;

        cb.Should().NotBeNull();
        cb!.Id.Should().Be("cbq-1");
        cb.FromUserId.Should().Be(9);
        cb.Data.Should().Be("open_preview:abc");
    }
}