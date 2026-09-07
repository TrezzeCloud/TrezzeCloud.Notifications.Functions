namespace TrezzeCloud.Contracts.Events;

public sealed record PaymentProcessedEvent(
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    decimal Price,
    string Status,
    DateTime ProcessedAt
);