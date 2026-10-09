# Notification Processing API

A small ASP.NET Core (.NET 8) Web API that validates, stores and processes customer notifications. Email is the only supported type for now.

## 1. How to run

Requires the .NET 8 SDK (or a newer SDK with the .NET 8 runtime).

```bash
dotnet run --project src/NotificationApi --launch-profile http
dotnet test
```

- Swagger UI: <http://localhost:5080/swagger>
- Ready-made requests for every scenario: `src/NotificationApi/NotificationApi.http`
- To see the provider-failure path, set `EmailProvider__FailureRate=1` before running.

| Method | Route | Purpose | Responses |
|---|---|---|---|
| POST | `/api/notifications` | Create a notification | 201, 400, 409 (duplicate) |
| GET | `/api/notifications/{id}` | Get one by ID | 200, 404 |
| GET | `/api/customers/{customerId}/notifications` | Get a customer's notifications | 200 |
| POST | `/api/notifications/process` | Process due Pending notifications | 200 |

Every response body is `{ "statusCode": <int>, "data": <object | list | message | field errors> }`.

## 2. Design

One API project with a folder per responsibility, plus an MSTest project.

| Folder | Responsibility |
|---|---|
| `Controllers` | HTTP only: binds the request and maps the service result to a status code |
| `Validation` | A pure validator that returns all field errors together |
| `Services` | Business rules: create, duplicate detection, processing |
| `Repositories` | `INotificationRepository` with an in-memory implementation |
| `Providers` | `INotificationProvider` with a simulated email provider that logs |
| `Contracts`, `Models` | Request/response DTOs, and the stored notification |

- **Storage**: a `ConcurrentDictionary`, with a lock only around the two "check then write" operations (duplicate check on insert, and claiming Pending notifications).
- **Processing**: due Pending notifications are claimed atomically and sent High first, then oldest first within the same priority.
- **Provider failure**: caught per notification, which is marked `Failed` with the reason stored; the rest of the batch continues.
- **Duplicates**: a hash of all request fields. An identical request within 5 minutes (configurable) returns 409 naming the existing notification.
- **Time**: read through `TimeProvider`, so time rules are tested with a fake clock.

## 3. Assumptions

- `subject` and `requestedDeliveryTime` are optional; a missing delivery time means "due now". `priority` is required.
- A delivery time exactly equal to now is accepted; only earlier times are rejected.
- A notification with a future delivery time stays Pending until it is due.
- An unknown customer returns an empty list, not 404.
- `type`, `priority`, customer IDs and email addresses are case-insensitive.

## 4. Trade-offs

- **In-memory storage**: no setup needed, but data is lost on restart and it works for one instance only.
- **Processing by endpoint**: easy to demo and test, but something has to call it.
- **No automatic retry**: a failed notification stays `Failed`.
- **Custom response envelope**: one shape for every response, instead of the standard `ProblemDetails` for errors.
- **Duplicate lookup scans the store**: fine at this size; a database would use an index.

## 5. Production improvements

- **Queue-driven processing**: publish each accepted notification to Azure Service Bus and process it in an Azure Function with a Service Bus trigger, instead of calling the process endpoint. A queue gives retry and dead-lettering out of the box: a message whose send fails is redelivered automatically, and after the maximum delivery count it moves to the dead-letter queue for inspection, so none of that has to be hand-written. It also scales automatically; scheduled messages cover future delivery times, and a separate queue per priority keeps High ahead of the rest.
- **Separate class libraries**: with more time I would move the services and repositories out of the API project into their own class library projects, so other hosts (such as the Azure Function above) can reuse them without referencing the Web API. I kept one project here to avoid over-engineering a small exercise.
- **Database**: EF Core with SQL Server or PostgreSQL, a unique index on the duplicate hash, and paging.
- **Retries**: exponential backoff with an attempt counter, and recovery of notifications stuck in `Processing`.
- **Idempotency key**: a client-supplied `Idempotency-Key` header as the main duplicate mechanism.
- **Security and operations**: authentication, rate limiting, health checks, metrics and tracing.

## 6. Unclear requirements and my interpretation

- **"The exact same request"**: every field matches. A change in any field, including the message, is a new notification.
- **"Within a short period"**: a configurable window, 5 minutes by default.
- **"Avoid sending twice"**: the duplicate is rejected with 409 and not stored. If the earlier copy ended as `Failed`, the retry is accepted, because nothing was delivered.
- **"Process pending notifications"**: an explicit API call that processes everything due at that moment.
- **Status on provider failure**: `Failed`, with the reason recorded.

## Tests

41 MSTest tests on the rules that are easy to get wrong:

- **Validator**: required fields, email formats, allowed priorities, and the past / exactly-now delivery time boundary.
- **Service**: duplicates (within the window, after it, after a failure, 50 concurrent requests), priority and FIFO order, provider failure, future notifications.
- **API over HTTP**: status codes and the response envelope for 201, 400, 409 and 404.
