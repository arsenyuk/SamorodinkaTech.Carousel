using System.Security.Cryptography;
using System.Text;
using Carousel.Application.Security;
using Carousel.Application.Services;
using FluentAssertions;
using Xunit;

namespace Carousel.Application.Tests;

public class InitDataValidatorTests
{
    private const string BotToken = "123456:TEST-BOT-TOKEN-for-hmac-unit-tests";

    private static (string InitData, string Hash) BuildSignedInitData(long userId, DateTimeOffset? authDate = null)
    {
        var userJson = $"{{\"id\":{userId},\"first_name\":\"Test\",\"username\":\"tester\"}}";
        var authUnix = (authDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authUnix,
            ["query_id"] = "AAH-test",
            ["user"] = userJson,
        };

        var dataCheck = string.Join('\n', pairs.Select(kv => $"{kv.Key}={kv.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheck))).ToLowerInvariant();

        var initData = string.Join('&', pairs.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))
            + "&hash=" + hash;
        return (initData, hash);
    }

    [Fact]
    public void Valid_signed_initData_passes()
    {
        var (initData, _) = BuildSignedInitData(42);
        var validator = new TelegramInitDataValidator(BotToken, allowDevRawId: false, isDevelopment: false);

        var user = validator.Validate(initData);

        user.TelegramUserId.Should().Be(42);
        user.Username.Should().Be("tester");
        user.DisplayName.Should().Be("Test");
    }

    [Fact]
    public void Forged_hash_is_rejected()
    {
        var (initData, _) = BuildSignedInitData(42);
        var forged = initData.Replace(
            initData[(initData.IndexOf("hash=", StringComparison.Ordinal) + 5)..],
            new string('a', 64));
        var validator = new TelegramInitDataValidator(BotToken, allowDevRawId: false, isDevelopment: false);

        var act = () => validator.Validate(forged);

        act.Should().Throw<ValidationException>().WithMessage("*signature*");
    }

    [Fact]
    public void Expired_auth_date_is_rejected()
    {
        var (initData, _) = BuildSignedInitData(1, DateTimeOffset.UtcNow.AddHours(-48));
        var validator = new TelegramInitDataValidator(BotToken, allowDevRawId: false, isDevelopment: false);

        var act = () => validator.Validate(initData);

        act.Should().Throw<ValidationException>().WithMessage("*expired*");
    }

    [Fact]
    public void Dev_raw_id_allowed_only_when_enabled_and_development()
    {
        var ok = new TelegramInitDataValidator("", allowDevRawId: true, isDevelopment: true);
        var devUser = ok.Validate("123456789");
        devUser.TelegramUserId.Should().Be(123456789);
        devUser.Username.Should().Be("devuser"); // алиас exempt от оплаты Stars

        var prod = new TelegramInitDataValidator(BotToken, allowDevRawId: true, isDevelopment: false);
        var act = () => prod.Validate("123456789");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Prod_without_bot_token_rejects_signed_form()
    {
        var (initData, _) = BuildSignedInitData(1);
        var validator = new TelegramInitDataValidator("", allowDevRawId: false, isDevelopment: false);

        var act = () => validator.Validate(initData);

        act.Should().Throw<ValidationException>().WithMessage("*Bot token*");
    }
}
