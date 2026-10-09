using NotificationApi.Contracts;
using NotificationApi.Models;
using NotificationApi.Providers;

namespace NotificationApi.Tests;

/// <summary>Clock the test controls, so time-based rules are deterministic.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Records what was sent, in order, and fails for chosen messages.</summary>
public sealed class FakeEmailProvider : INotificationProvider
{
    public List<Notification> Sent { get; } = [];
    public Func<Notification, bool> ShouldFail { get; set; } = _ => false;

    public NotificationType Type => NotificationType.Email;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (ShouldFail(notification))
            throw new NotificationProviderException("Provider unavailable.");

        Sent.Add(notification);
        return Task.CompletedTask;
    }
}

public static class Requests
{
    public static CreateNotificationRequest Valid(string message = "Your order has been confirmed.", string priority = "Normal") => new()
    {
        CustomerId = "CUST-101",
        Type = "Email",
        Recipient = "customer@example.com",
        Subject = "Order Confirmed",
        Message = message,
        Priority = priority
    };
}
