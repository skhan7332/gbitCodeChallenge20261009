using NotificationApi.Models;

namespace NotificationApi.Providers;

/// <summary>
/// Abstraction over an external delivery provider (e.g. SendGrid, SES).
/// One implementation per notification type; implementations throw on failure.
/// </summary>
public interface INotificationProvider
{
    /// <summary>
    /// The notification type this provider delivers; used to pick the provider for a notification.
    /// </summary>
    NotificationType Type { get; }

    /// <summary>
    /// Delivers the notification. Completes normally on success and throws when delivery fails.
    /// </summary>
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when a provider cannot deliver a notification.
/// </summary>
public sealed class NotificationProviderException : Exception
{
    public NotificationProviderException(string message) : base(message) { }
}
