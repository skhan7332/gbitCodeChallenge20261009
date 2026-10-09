namespace NotificationApi.Models;

public enum NotificationType
{
    Email
}

// Numeric values define processing order: higher value is processed first.
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum NotificationStatus
{
    Pending,
    Processing,
    Sent,
    Failed
}

/// <summary>
/// Immutable so a stored notification is only ever replaced as a whole, never partially updated.
/// </summary>
public sealed record Notification
{
    public required Guid Id { get; init; }
    public required string CustomerId { get; init; }
    public required NotificationType Type { get; init; }
    public required string Recipient { get; init; }
    public string? Subject { get; init; }
    public required string Message { get; init; }
    public required NotificationPriority Priority { get; init; }
    public required DateTimeOffset RequestedDeliveryTime { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public NotificationStatus Status { get; init; } = NotificationStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>Hash of the request content, used to detect retried (duplicate) requests.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Insertion order assigned by the repository; breaks ties between equal priorities (FIFO).</summary>
    public long Sequence { get; init; }
}
