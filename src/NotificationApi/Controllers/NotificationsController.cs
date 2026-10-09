using Microsoft.AspNetCore.Mvc;
using NotificationApi.Contracts;
using NotificationApi.Services;

namespace NotificationApi.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationsController(INotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<NotificationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<Dictionary<string, string[]>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest request)
    {
        var result = await _service.CreateAsync(request);

        if (result.Status == CreateNotificationStatus.Invalid)
            return Respond(StatusCodes.Status400BadRequest, result.Errors!);

        var notification = result.Notification!;

        if (result.Status == CreateNotificationStatus.Duplicate)
            return Respond(StatusCodes.Status409Conflict,
                $"Duplicate request: an identical request was already accepted at {notification.CreatedAt:O} " +
                $"as notification '{notification.Id}' (status: {notification.Status}). " +
                "It will not be sent again.");

        // CreatedAtAction (rather than Respond) so the Location header points at the new resource.
        return CreatedAtAction(nameof(GetById), new { id = notification.Id },
            new ApiResponse<NotificationResponse>(StatusCodes.Status201Created, NotificationResponse.From(notification)));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var notification = await _service.GetByIdAsync(id);
        return notification is null
            ? Respond(StatusCodes.Status404NotFound, $"No notification exists with ID '{id}'.")
            : Respond(StatusCodes.Status200OK, NotificationResponse.From(notification));
    }

    /// <summary>Returns the customer's notifications, newest first. An unknown customer yields an empty list.</summary>
    [HttpGet("/api/customers/{customerId}/notifications")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<NotificationResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCustomer(string customerId)
    {
        var notifications = await _service.GetByCustomerAsync(customerId);
        return Respond(StatusCodes.Status200OK, notifications.Select(NotificationResponse.From));
    }

    /// <summary>Processes Pending notifications that are due: High priority first, then oldest first.</summary>
    [HttpPost("process")]
    [ProducesResponseType(typeof(ApiResponse<ProcessingSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProcessPending(CancellationToken cancellationToken)
    {
        var summary = await _service.ProcessPendingAsync(cancellationToken);
        return Respond(StatusCodes.Status200OK,
            new ProcessingSummaryResponse(summary.Processed, summary.Sent, summary.Failed));
    }

    private ObjectResult Respond<T>(int statusCode, T data) =>
        StatusCode(statusCode, new ApiResponse<T>(statusCode, data));
}
