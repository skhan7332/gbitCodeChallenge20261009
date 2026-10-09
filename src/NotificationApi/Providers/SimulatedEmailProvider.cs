using Microsoft.Extensions.Options;
using NotificationApi.Models;

namespace NotificationApi.Providers;

public sealed class EmailProviderOptions
{
    public const string SectionName = "EmailProvider";

    /// <summary>Probability (0..1) that a send fails. Lets the failure path be exercised without a real provider.</summary>
    public double FailureRate { get; set; }
}

/// <summary>Stands in for a real email provider: logs instead of sending.</summary>
public sealed class SimulatedEmailProvider : INotificationProvider
{
    private readonly EmailProviderOptions _options;
    private readonly ILogger<SimulatedEmailProvider> _logger;

    public SimulatedEmailProvider(IOptions<EmailProviderOptions> options, ILogger<SimulatedEmailProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public NotificationType Type => NotificationType.Email;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Random.Shared.NextDouble() < _options.FailureRate)
            throw new NotificationProviderException("Simulated email provider failure.");

        _logger.LogInformation("Email sent to {Recipient} with subject '{Subject}' (notification {NotificationId})",
            notification.Recipient, notification.Subject, notification.Id);
        return Task.CompletedTask;
    }
}
