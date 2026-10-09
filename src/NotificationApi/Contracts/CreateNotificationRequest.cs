namespace NotificationApi.Contracts;

/// <summary>
/// Everything is nullable and Type/Priority are strings so that missing or unknown values
/// reach our validator and get a clear per-field message instead of a generic binding error.
/// </summary>
public sealed class CreateNotificationRequest
{
    public string? CustomerId { get; set; }
    public string? Type { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }
    public string? Priority { get; set; }

    /// <summary>Optional. When omitted the notification is due immediately.</summary>
    public DateTimeOffset? RequestedDeliveryTime { get; set; }
}
