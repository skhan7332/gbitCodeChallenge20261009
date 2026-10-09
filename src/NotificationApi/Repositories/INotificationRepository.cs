using NotificationApi.Models;

namespace NotificationApi.Repositories;

/// <summary>
/// Outcome of an add: the stored notification, or the existing one when <paramref name="IsDuplicate"/> is true.
/// </summary>
public sealed record AddResult(Notification Notification, bool IsDuplicate);

public interface INotificationRepository
{
    /// <summary>
    /// Atomically stores the notification unless one with the same fingerprint was created
    /// at or after <paramref name="duplicateCutoff"/>. On a duplicate, the existing one is returned.
    /// </summary>
    Task<AddResult> AddUnlessDuplicateAsync(Notification notification, DateTimeOffset duplicateCutoff);

    /// <summary>
    /// Returns the notification with the given ID, or null when none exists.
    /// </summary>
    Task<Notification?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns all notifications for the customer (case-insensitive match), newest first.
    /// Returns an empty list when the customer has none.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetByCustomerAsync(string customerId);

    /// <summary>
    /// Atomically moves due Pending notifications to Processing and returns them in processing
    /// order (highest priority first, then oldest first), so two callers never claim the same one.
    /// </summary>
    Task<IReadOnlyList<Notification>> ClaimDuePendingAsync(DateTimeOffset now, int maxCount);

    /// <summary>
    /// Replaces the stored notification that has the same ID, e.g. to record a new status after processing.
    /// </summary>
    Task UpdateAsync(Notification notification);
}
