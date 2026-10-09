using System.Net.Mail;
using NotificationApi.Contracts;
using NotificationApi.Models;

namespace NotificationApi.Validation;

public static class NotificationRequestValidator
{
    public const int MaxCustomerIdLength = 100;
    public const int MaxRecipientLength = 254;
    public const int MaxSubjectLength = 200;
    public const int MaxMessageLength = 5000;

    /// <summary>
    /// Returns field name -> error messages. An empty dictionary means the request is valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(CreateNotificationRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CustomerId))
            errors["customerId"] = ["Customer ID is required."];
        else if (request.CustomerId.Trim().Length > MaxCustomerIdLength)
            errors["customerId"] = [$"Customer ID must be at most {MaxCustomerIdLength} characters."];

        NotificationType? type = null;
        if (string.IsNullOrWhiteSpace(request.Type))
            errors["type"] = ["Notification type is required."];
        else if (!TryParseEnum<NotificationType>(request.Type, out var parsedType))
            errors["type"] = [$"Notification type '{request.Type}' is not supported. Supported types: {Names<NotificationType>()}."];
        else
            type = parsedType;

        if (string.IsNullOrWhiteSpace(request.Recipient))
            errors["recipient"] = ["Recipient is required."];
        else if (request.Recipient.Trim().Length > MaxRecipientLength)
            errors["recipient"] = [$"Recipient must be at most {MaxRecipientLength} characters."];
        else if (type == NotificationType.Email && !IsValidEmail(request.Recipient.Trim()))
            errors["recipient"] = ["Recipient must be a valid email address."];

        if (request.Subject is { Length: > MaxSubjectLength })
            errors["subject"] = [$"Subject must be at most {MaxSubjectLength} characters."];

        if (string.IsNullOrWhiteSpace(request.Message))
            errors["message"] = ["Message is required."];
        else if (request.Message.Length > MaxMessageLength)
            errors["message"] = [$"Message must be at most {MaxMessageLength} characters."];

        if (string.IsNullOrWhiteSpace(request.Priority))
            errors["priority"] = ["Priority is required."];
        else if (!TryParseEnum<NotificationPriority>(request.Priority, out _))
            errors["priority"] = [$"Priority '{request.Priority}' is invalid. Allowed values: {Names<NotificationPriority>()}."];

        // Strictly "in the past": a time exactly equal to now is accepted.
        if (request.RequestedDeliveryTime is { } requested && requested < now)
            errors["requestedDeliveryTime"] = ["Requested delivery time cannot be in the past."];

        return errors;
    }

    /// <summary>
    /// Matches enum names only (case-insensitive). Enum.TryParse would also accept numbers like "7".
    /// </summary>
    public static bool TryParseEnum<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        var trimmed = value?.Trim();
        var name = Enum.GetNames<TEnum>().FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        return name is not null && Enum.TryParse(name, out result);
    }

    public static bool IsValidEmail(string value)
    {
        // MailAddress accepts display-name forms ("Bob <bob@x.com>") and dot-less hosts,
        // so require the parsed address to equal the input and the domain to contain a dot.
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value)
            return false;

        var host = address.Host;
        return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..");
    }

    private static string Names<TEnum>() where TEnum : struct, Enum => string.Join(", ", Enum.GetNames<TEnum>());
}
