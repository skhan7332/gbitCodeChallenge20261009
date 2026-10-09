using System.Security.Cryptography;
using System.Text;
using NotificationApi.Models;

namespace NotificationApi.Services;

public static class NotificationFingerprint
{
    /// <summary>
    /// Two requests are "the exact same request" when every field matches after trivial
    /// normalisation (trimmed IDs, case-insensitive email, delivery time compared as an instant).
    /// </summary>
    public static string Compute(
        string customerId,
        NotificationType type,
        string recipient,
        string? subject,
        string message,
        NotificationPriority priority,
        DateTimeOffset? requestedDeliveryTime)
    {
        // The requested time as sent by the caller is used, not the "now" we default to,
        // so two retries of an "immediate" request still match.
        var parts = new[]
        {
            customerId.ToUpperInvariant(),
            type.ToString(),
            recipient.ToLowerInvariant(),
            subject ?? string.Empty,
            message,
            priority.ToString(),
            requestedDeliveryTime?.UtcTicks.ToString() ?? "immediate"
        };

        // Length-prefix each part so ("ab","c") and ("a","bc") cannot collide.
        var canonical = string.Join("|", parts.Select(p => $"{p.Length}:{p}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
