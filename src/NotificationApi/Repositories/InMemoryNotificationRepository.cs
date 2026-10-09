using System.Collections.Concurrent;
using NotificationApi.Models;

namespace NotificationApi.Repositories;

/// <summary>
/// ConcurrentDictionary makes single-key reads/writes thread-safe and lock-free.
/// The lock is only for the two compound "check then write" operations, which a
/// ConcurrentDictionary cannot make atomic on its own.
/// </summary>
public sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly ConcurrentDictionary<Guid, Notification> _store = new();
    private readonly object _compoundLock = new();
    private long _sequence;

    public Task<AddResult> AddUnlessDuplicateAsync(Notification notification, DateTimeOffset duplicateCutoff)
    {
        lock (_compoundLock)
        {
            // A Failed notification was never delivered, so resubmitting it is not a duplicate send.
            var duplicate = _store.Values
                .Where(n => n.Fingerprint == notification.Fingerprint
                            && n.CreatedAt >= duplicateCutoff
                            && n.Status != NotificationStatus.Failed)
                .OrderByDescending(n => n.Sequence)
                .FirstOrDefault();

            if (duplicate is not null)
                return Task.FromResult(new AddResult(duplicate, IsDuplicate: true));

            var stored = notification with { Sequence = ++_sequence };
            _store[stored.Id] = stored;
            return Task.FromResult(new AddResult(stored, IsDuplicate: false));
        }
    }

    public Task<Notification?> GetByIdAsync(Guid id) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<Notification>> GetByCustomerAsync(string customerId)
    {
        IReadOnlyList<Notification> result = _store.Values
            .Where(n => string.Equals(n.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Notification>> ClaimDuePendingAsync(DateTimeOffset now, int maxCount)
    {
        lock (_compoundLock)
        {
            var claimed = _store.Values
                .Where(n => n.Status == NotificationStatus.Pending && n.RequestedDeliveryTime <= now)
                .OrderByDescending(n => n.Priority)
                .ThenBy(n => n.Sequence)
                .Take(maxCount)
                .Select(n => n with { Status = NotificationStatus.Processing })
                .ToList();

            foreach (var notification in claimed)
                _store[notification.Id] = notification;

            return Task.FromResult<IReadOnlyList<Notification>>(claimed);
        }
    }

    public Task UpdateAsync(Notification notification)
    {
        _store[notification.Id] = notification;
        return Task.CompletedTask;
    }
}
