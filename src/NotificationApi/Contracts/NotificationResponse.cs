using NotificationApi.Models;

namespace NotificationApi.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    string CustomerId,
    NotificationType Type,
    string Recipient,
    string? Subject,
    string Message,
    NotificationPriority Priority,
    DateTimeOffset RequestedDeliveryTime,
    DateTimeOffset CreatedAt,
    NotificationStatus Status,
    DateTimeOffset? ProcessedAt,
    string? FailureReason)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id, n.CustomerId, n.Type, n.Recipient, n.Subject, n.Message, n.Priority,
        n.RequestedDeliveryTime, n.CreatedAt, n.Status, n.ProcessedAt, n.FailureReason);
}

public sealed record ProcessingSummaryResponse(int Processed, int Sent, int Failed);
