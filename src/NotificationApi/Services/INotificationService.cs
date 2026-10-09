using NotificationApi.Contracts;
using NotificationApi.Models;

namespace NotificationApi.Services;

/// <summary>
/// The possible outcomes of a create request.
/// </summary>
public enum CreateNotificationStatus
{
    Created,
    Invalid,
    Duplicate
}

/// <summary>
/// Result of a create request. Lets the service report expected outcomes without throwing,
/// and leaves the choice of HTTP status code to the controller.
/// </summary>
public sealed class CreateNotificationResult
{
    /// <summary>Which outcome occurred; decides which of the other properties are set.</summary>
    public CreateNotificationStatus Status { get; init; }

    /// <summary>The new notification when Created; the already-stored one when Duplicate; null when Invalid.</summary>
    public Notification? Notification { get; init; }

    /// <summary>Field name -> error messages. Only set when Invalid.</summary>
    public Dictionary<string, string[]>? Errors { get; init; }
}

/// <summary>
/// Counts from one processing run: how many notifications were picked up, and how many of those were sent or failed.
/// </summary>
public sealed record ProcessingSummary(int Processed, int Sent, int Failed);

/// <summary>
/// Settings bound from the "Notifications" configuration section.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>An identical request within this window of the original is treated as a retry.</summary>
    public TimeSpan DuplicateWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum notifications handled by one processing run.</summary>
    public int ProcessingBatchSize { get; set; } = 100;
}

/// <summary>
/// Business logic for notifications: accepting requests, retrieving them, and processing pending ones.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Validates the request and stores it as a Pending notification.
    /// Returns Invalid with field errors when validation fails, or Duplicate with the existing
    /// notification when an identical request was accepted within the duplicate window.
    /// </summary>
    Task<CreateNotificationResult> CreateAsync(CreateNotificationRequest request);

    /// <summary>
    /// Returns the notification with the given ID, or null when none exists.
    /// </summary>
    Task<Notification?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns all notifications for the customer, newest first. Returns an empty list when there are none.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetByCustomerAsync(string customerId);

    /// <summary>
    /// Sends every Pending notification that is due, highest priority first and then oldest first.
    /// Each one ends as Sent or Failed; a provider failure is recorded and does not stop the rest.
    /// </summary>
    Task<ProcessingSummary> ProcessPendingAsync(CancellationToken cancellationToken);
}
