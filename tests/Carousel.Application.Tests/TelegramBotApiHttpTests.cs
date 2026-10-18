using System.Net;
using System.Text;
using Carousel.Application.Abstractions;
using Carousel.Infrastructure.Bot;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Carousel.Application.Tests;

/// <summary>
/// Captures the exact HTTP payloads TelegramBotApi sends to Bot API,
/// so multipart regressions (sendMediaGroup / sendPhoto) are caught without network.
/// </summary>
public class TelegramBotApiHttpTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var isGetMe = request.RequestUri?.ToString().Contains("getMe") == true;
            var json = isGetMe
                ? "{\"ok\":true,\"result\":{\"id\":1,\"is_bot\":true,\"first_name\":\"T\",\"username\":\"carousel_test_bot\"}}"
                : "{\"ok\":true,\"result\":[]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
        }
    }

    private static (TelegramBotApi Api, CaptureHandler Handler) Create()
    {
        var handler = new CaptureHandler();
        var api = new TelegramBotApi(
            new HttpClient(handler),
            NullLogger<TelegramBotApi>.Instance,
            "test-token");
        return (api, handler);
    }

    private static IEnumerable<(string Name, string Value)> MultipartFields(string body, string boundary)
    {
        // .NET may serialize name= unquoted (token form) or name="quoted" — accept both.
        foreach (var part in body.Split("--" + boundary, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.Contains("Content-Disposition:", StringComparison.Ordinal))
            {
                continue;
            }

            var dispIdx = part.IndexOf("Content-Disposition:", StringComparison.Ordinal);
            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var disposition = part[dispIdx..headerEnd];

            var nameIdx = disposition.IndexOf("name=", StringComparison.Ordinal);
            if (nameIdx < 0)
            {
                continue;
            }

            var raw = disposition[(nameIdx + "name=".Length)..].TrimStart();
            string name;
            if (raw.StartsWith('"'))
            {
                name = raw[1..raw.IndexOf('"', 1)];
            }
            else
            {
                name = raw.Split(';', ' ', '\r', '\n')[0];
            }

            var value = part[(headerEnd + 4)..].TrimEnd('\r', '\n');
            yield return (name, value);
        }
    }

    [Fact]
    public async Task SendMediaGroup_sends_media_json_array_with_attach_files()
    {
        var (api, handler) = Create();
        var items = new[]
        {
            new TelegramMediaGroupItem(new byte[] { 1, 2, 3 }, "image/jpeg", "Слайд 1"),
            new TelegramMediaGroupItem(new byte[] { 4, 5 }, "image/png", "Слайд 2"),
        };

        await api.SendMediaGroupAsync(777, items);

        handler.Last.Should().NotBeNull();
        handler.Last!.RequestUri!.ToString().Should().EndWith("sendMediaGroup");

        var boundary = handler.Last.Content!.Headers.ContentType!.Parameters
            .Single(p => p.Name == "boundary").Value!.Trim('"');
        var fields = MultipartFields(handler.LastBody!, boundary).ToList();

        fields.Should().Contain(f => f.Name == "chat_id" && f.Value == "777");

        var media = fields.Single(f => f.Name == "media").Value;
        // media must be a JSON array (Telegram 400 "media must be an array" otherwise)
        media.Should().StartWith("[");
        using var doc = System.Text.Json.JsonDocument.Parse(media);
        doc.RootElement.GetArrayLength().Should().Be(2);
        var first = doc.RootElement[0];
        first.GetProperty("type").GetString().Should().Be("photo");
        first.GetProperty("media").GetString().Should().Be("attach://file0");
        first.GetProperty("caption").GetString().Should().Be("Слайд 1");
        doc.RootElement[1].GetProperty("media").GetString().Should().Be("attach://file1");

        fields.Should().Contain(f => f.Name == "file0" && f.Value.Length > 0);
        fields.Should().Contain(f => f.Name == "file1" && f.Value.Length > 0);
    }

    [Fact]
    public async Task SendPhoto_sends_multipart_photo_with_caption()
    {
        var (api, handler) = Create();

        await api.SendPhotoAsync(42, new byte[] { 9, 9 }, "image/png", "Слайд 1");

        handler.Last!.RequestUri!.ToString().Should().EndWith("sendPhoto");
        var boundary = handler.Last.Content!.Headers.ContentType!.Parameters
            .Single(p => p.Name == "boundary").Value!.Trim('"');
        var fields = MultipartFields(handler.LastBody!, boundary).ToList();

        fields.Should().Contain(f => f.Name == "chat_id" && f.Value == "42");
        fields.Should().Contain(f => f.Name == "caption" && f.Value == "Слайд 1");
        fields.Should().Contain(f => f.Name == "photo" && f.Value.Length > 0);
    }

    [Fact]
    public async Task GetBotUsername_uses_config_override_without_http()
    {
        var handler = new CaptureHandler();
        var api = new TelegramBotApi(
            new HttpClient(handler), NullLogger<TelegramBotApi>.Instance, "test-token", "prod_bot_override");

        (await api.GetBotUsernameAsync()).Should().Be("prod_bot_override");
        handler.Calls.Should().Be(0, "оверрайд не требует сети — тестовый/прод бот из конфига");
    }

    [Fact]
    public async Task GetBotUsername_falls_back_to_getMe_and_caches()
    {
        var (api, handler) = Create();

        (await api.GetBotUsernameAsync()).Should().Be("carousel_test_bot");
        (await api.GetBotUsernameAsync()).Should().Be("carousel_test_bot");
        handler.Calls.Should().Be(1, "getMe выполняется один раз, дальше кэш");
    }

    [Fact]
    public async Task GetBotUsername_without_token_returns_mockbot()
    {
        var api = new TelegramBotApi(
            new HttpClient(), NullLogger<TelegramBotApi>.Instance, string.Empty);

        (await api.GetBotUsernameAsync()).Should().Be("mockbot");
    }

    [Fact]
    public async Task TryPostStory_with_linkUrl_stamps_photo_and_sends_areas()
    {
        var (api, handler) = Create();
        using var bitmap = new SkiaSharp.SKBitmap(400, 400);
        using var original = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = original.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var photo = png.ToArray();

        var result = await api.TryPostStoryAsync(
            "conn-1", photo, "image/png", "Слайд", "https://t.me/carousel_test_bot");

        result.Ok.Should().BeTrue();
        handler.Last!.RequestUri!.ToString().Should().EndWith("postStory");

        var boundary = handler.Last.Content!.Headers.ContentType!.Parameters
            .Single(p => p.Name == "boundary").Value!.Trim('"');
        var fields = MultipartFields(handler.LastBody!, boundary).ToList();

        // Кликабельная зона в правом нижнем углу.
        using var areas = System.Text.Json.JsonDocument.Parse(fields.Single(f => f.Name == "areas").Value);
        var firstArea = areas.RootElement[0];
        firstArea.GetProperty("type").GetProperty("type").GetString().Should().Be("link");
        firstArea.GetProperty("type").GetProperty("url").GetString().Should().Be("https://t.me/carousel_test_bot");
        firstArea.GetProperty("position").GetProperty("x_percentage").GetDouble().Should().Be(80d);
        firstArea.GetProperty("position").GetProperty("y_percentage").GetDouble().Should().Be(93d);

        // Фото напечатано — байты изменились (без печати строка совпала бы с исходником).
        var sent = fields.Single(f => f.Name == "file0").Value;
        sent.Length.Should().BeGreaterThan(0);
        sent.Should().NotBe(Encoding.UTF8.GetString(photo), "подпись должна быть нарисована на фото");
    }
}