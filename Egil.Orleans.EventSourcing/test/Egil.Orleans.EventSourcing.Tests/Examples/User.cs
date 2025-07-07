namespace Egil.Orleans.EventSourcing.Examples;

[Immutable, GenerateSerializer, Alias("UserProjection.V1")]
public sealed record User(
    string UserId,
    string Name,
    string Email,
    int EventsCount,
    int TotalMessagesCount,
    int OutboxEventsCount,
    int BadWordsCount,
    bool IsDeactivated,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt) : IEventProjection<User>
{
    public static User CreateDefault() => new(
        UserId: string.Empty,
        Name: string.Empty,
        Email: string.Empty,
        EventsCount: 0,
        TotalMessagesCount: 0,
        OutboxEventsCount: 0,
        BadWordsCount: 0,
        IsDeactivated: false,
        CreatedAt: DateTimeOffset.MinValue,
        LastModifiedAt: DateTimeOffset.MinValue);
}
