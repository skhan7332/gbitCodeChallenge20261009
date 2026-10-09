using Microsoft.Extensions.Options;
using NotificationApi.Contracts;
using NotificationApi.Models;
using NotificationApi.Providers;
using NotificationApi.Repositories;
using NotificationApi.Validation;

namespace NotificationApi.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly IReadOnlyDictionary<NotificationType, INotificationProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository repository,
        IEnumerable<INotificationProvider> providers,
        TimeProvider timeProvider,
        IOptions<NotificationOptions> options,
        ILogger<NotificationService> logger)
    {
        _repository = repository;
        _providers = providers.ToDictionary(p => p.Type);
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CreateNotificationResult> CreateAsync(CreateNotificationRequest request)
    {
        var now = _timeProvider.GetUtcNow();

        var errors = NotificationRequestValidator.Validate(request, now);
        if (errors.Count > 0)
            return new CreateNotificationResult { Status = CreateNotificationStatus.Invalid, Errors = errors };

        // Safe after validation: required fields are present and the enums parse.
        NotificationRequestValidator.TryParseEnum<NotificationType>(request.Type, out var type);
        NotificationRequestValidator.TryParseEnum<NotificationPriority>(request.Priority, out var priority);
        var customerId = request.CustomerId!.Trim();
        var recipient = request.Recipient!.Trim();
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Type = type,
            Recipient = recipient,
            Subject = subject,
            Message = request.Message!,
            Priority = priority,
            RequestedDeliveryTime = request.RequestedDeliveryTime?.ToUniversalTime() ?? now,
            CreatedAt = now,
            Fingerprint = NotificationFingerprint.Compute(
                customerId, type, recipient, subject, request.Message!, priority, request.RequestedDeliveryTime)
        };

        var result = await _repository.AddUnlessDuplicateAsync(notification, now - _options.DuplicateWindow);
        if (result.IsDuplicate)
        {
            _logger.LogInformation("Duplicate request rejected; matches notification {NotificationId}", result.Notification.Id);
            return new CreateNotificationResult { Status = CreateNotificationStatus.Duplicate, Notification = result.Notification };
        }

        _logger.LogInformation("Notification {NotificationId} accepted for customer {CustomerId}", notification.Id, customerId);
        return new CreateNotificationResult { Status = CreateNotificationStatus.Created, Notification = result.Notification };
    }

    public Task<Notification?> GetByIdAsync(Guid id) => _repository.GetByIdAsync(id);

    public Task<IReadOnlyList<Notification>> GetByCustomerAsync(string customerId) =>
        _repository.GetByCustomerAsync(customerId.Trim());

    public async Task<ProcessingSummary> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var claimed = await _repository.ClaimDuePendingAsync(_timeProvider.GetUtcNow(), _options.ProcessingBatchSize);
        var sent = 0;
        var failed = 0;

        for (var i = 0; i < claimed.Count; i++)
        {
            var notification = claimed[i];
            try
            {
                if (!_providers.TryGetValue(notification.Type, out var provider))
                    throw new NotificationProviderException($"No provider registered for type '{notification.Type}'.");

                await provider.SendAsync(notification, cancellationToken);

                await _repository.UpdateAsync(notification with
                {
                    Status = NotificationStatus.Sent,
                    ProcessedAt = _timeProvider.GetUtcNow()
                });
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown/abort is not a delivery failure: hand unprocessed items back so they are picked up next run.
                foreach (var remaining in claimed.Skip(i))
                    await _repository.UpdateAsync(remaining with { Status = NotificationStatus.Pending });
                throw;
            }
            catch (Exception ex)
            {
                // One bad send must not stop the rest of the batch or crash the app.
                _logger.LogError(ex, "Failed to send notification {NotificationId}", notification.Id);
                await _repository.UpdateAsync(notification with
                {
                    Status = NotificationStatus.Failed,
                    ProcessedAt = _timeProvider.GetUtcNow(),
                    FailureReason = ex.Message
                });
                failed++;
            }
        }

        return new ProcessingSummary(claimed.Count, sent, failed);
    }
}
