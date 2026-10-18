using System.Collections.Concurrent;
using Carousel.Application.Abstractions;

namespace Carousel.Infrastructure.Instagram;

/// <summary>
/// In-process Instagram mock outbox. Records successful publishes for local
/// inspection via the dev API — no live Meta Graph API calls.
/// </summary>
public sealed class InstagramMockOutbox
{
    private readonly ConcurrentQueue<MockInstagramPost> _posts = new();
    private int _failNext;

    public void ArmFailNext() => Interlocked.Exchange(ref _failNext, 1);

    public bool TryConsumeFail()
        => Interlocked.Exchange(ref _failNext, 0) == 1;

    public void Enqueue(MockInstagramPost post) => _posts.Enqueue(post);

    public IReadOnlyList<MockInstagramPost> Peek() => _posts.ToArray();

    public IReadOnlyList<MockInstagramPost> Drain()
    {
        var list = new List<MockInstagramPost>();
        while (_posts.TryDequeue(out var item))
        {
            list.Add(item);
        }

        return list;
    }
}

public sealed record MockInstagramPost(
    string AccountExternalId,
    Guid PublicationId,
    string Caption,
    IReadOnlyList<MockInstagramSlide> Slides,
    string ExternalPostId,
    DateTime AtUtc);

public sealed record MockInstagramSlide(
    Guid? MediaAssetId,
    string? Text,
    long? ImageBytes);
