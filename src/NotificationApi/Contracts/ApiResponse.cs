namespace NotificationApi.Contracts;

/// <summary>
/// Single envelope for every response body. <typeparamref name="T"/> is whatever fits the outcome:
/// an object on success, a message string, or a field -> errors map for validation failures.
/// </summary>
public sealed record ApiResponse<T>(int StatusCode, T Data);
