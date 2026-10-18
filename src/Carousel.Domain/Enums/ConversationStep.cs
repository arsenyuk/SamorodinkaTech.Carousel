namespace Carousel.Domain.Enums;

public enum ConversationStep
{
    None = 0,
    AwaitingTitle = 1,
    AwaitingDescription = 2,
    AwaitingImages = 3,
    EditingCards = 4,
    AwaitingPublishConfirm = 5,
}
