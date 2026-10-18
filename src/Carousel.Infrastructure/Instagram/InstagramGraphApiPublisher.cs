using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carousel.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Instagram;

/// <summary>
/// Real Instagram Content Publishing adapter (Meta Graph API).
/// Flow: create child containers for each image → create CAROUSEL parent → media_publish.
/// Requires a publicly reachable image URL base (Instagram does not accept binary uploads).
/// </summary>
public sealed class InstagramGraphApiPublisher : IInstagramPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<InstagramGraphApiPublisher> _logger;
    private readonly string _publicMediaBaseUrl;
    private readonly string _fallbackAccessToken;

    public InstagramGraphApiPublisher(
        HttpClient http,
        ILogger<InstagramGraphApiPublisher> logger,
        string publicMediaBaseUrl,
        string fallbackAccessToken)
    {
        _http = http;
        _logger = logger;
        _publicMediaBaseUrl = publicMediaBaseUrl.TrimEnd('/');
        _fallbackAccessToken = fallbackAccessToken;
    }

    public async Task<PublishResult> PublishCarouselAsync(InstagramPublishRequest request, CancellationToken ct = default)
    {
        // Stage 4 MVP: access token from app config; per-account tokens via
        // credentials land with the OAuth flow. ExternalId = ig-user-id.
        var accessToken = _fallbackAccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning(
                "Instagram publish skipped for {PublicationId}: Instagram:AccessToken is not configured.",
                request.PublicationId);
            return new PublishResult(false, null, "Instagram access token is not configured.");
        }

        var imageCards = request.Cards
            .Where(c => c.MediaAssetId is not null)
            .ToList();
        if (imageCards.Count == 0)
        {
            return new PublishResult(false, null, "Instagram carousel requires at least one image.");
        }

        var caption = BuildCaption(request.Cards);

        try
        {
            string parentId;
            if (imageCards.Count == 1)
            {
                parentId = await CreateImageContainerAsync(
                    request.AccountExternalId,
                    BuildImageUrl(imageCards[0]),
                    isCarouselItem: false,
                    caption,
                    accessToken,
                    ct);
            }
            else
            {
                var childIds = new List<string>();
                foreach (var card in imageCards)
                {
                    var child = await CreateImageContainerAsync(
                        request.AccountExternalId,
                        BuildImageUrl(card),
                        isCarouselItem: true,
                        caption: null,
                        accessToken,
                        ct);
                    childIds.Add(child);
                }

                parentId = await CreateCarouselContainerAsync(
                    request.AccountExternalId, childIds, caption, accessToken, ct);
            }

            var publishId = await PublishAsync(request.AccountExternalId, parentId, accessToken, ct);

            _logger.LogInformation(
                "Instagram published publication {PublicationId} as {ExternalId} to {Account}.",
                request.PublicationId, publishId, request.AccountExternalId);

            return new PublishResult(true, publishId, null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Ошибка HTTP {Method} {Url} for Instagram publish",
                "POST", "graph.facebook.com (instagram)");
            return new PublishResult(false, null, $"Graph API HTTP error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Instagram Graph API returned unexpected JSON for publication {PublicationId}.", request.PublicationId);
            return new PublishResult(false, null, "Graph API returned unexpected JSON.");
        }
    }

    private string BuildImageUrl(PublishCard card)
        => $"{_publicMediaBaseUrl}/api/media/{card.MediaAssetId}";

    private static string BuildCaption(IReadOnlyList<PublishCard> cards)
        => string.Join("\n", cards.Select(c => c.Text).Where(t => !string.IsNullOrWhiteSpace(t)));

    private async Task<string> CreateImageContainerAsync(
        string igUserId,
        string imageUrl,
        bool isCarouselItem,
        string? caption,
        string accessToken,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            $"image_url={Uri.EscapeDataString(imageUrl)}",
            $"access_token={Uri.EscapeDataString(accessToken)}",
        };
        if (isCarouselItem)
        {
            query.Add("is_carousel_item=true");
        }

        if (!string.IsNullOrEmpty(caption))
        {
            query.Add($"caption={Uri.EscapeDataString(caption)}");
        }

        var response = await _http.PostAsync($"{igUserId}/media?{string.Join("&", query)}", null, ct);
        return await ReadIdAsync(response, "create image container", ct);
    }

    private async Task<string> CreateCarouselContainerAsync(
        string igUserId,
        IReadOnlyList<string> childIds,
        string caption,
        string accessToken,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            "media_type=CAROUSEL",
            $"children={Uri.EscapeDataString(string.Join(",", childIds))}",
            $"caption={Uri.EscapeDataString(caption)}",
            $"access_token={Uri.EscapeDataString(accessToken)}",
        };

        var response = await _http.PostAsync($"{igUserId}/media?{string.Join("&", query)}", null, ct);
        return await ReadIdAsync(response, "create carousel container", ct);
    }

    private async Task<string> PublishAsync(string igUserId, string creationId, string accessToken, CancellationToken ct)
    {
        var query = string.Join("&",
            $"creation_id={Uri.EscapeDataString(creationId)}",
            $"access_token={Uri.EscapeDataString(accessToken)}");
        var response = await _http.PostAsync($"{igUserId}/media_publish?{query}", null, ct);
        return await ReadIdAsync(response, "media_publish", ct);
    }

    private async Task<string> ReadIdAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Instagram Graph API {Operation} failed: {Status} {Body}",
                operation, (int)response.StatusCode, body);
            throw new HttpRequestException($"Instagram {operation} failed with {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<GraphIdResponse>(JsonOptions, ct);
        if (payload?.Id is null)
        {
            throw new JsonException("Graph API response has no id.");
        }

        return payload.Id;
    }

    private sealed class GraphIdResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
