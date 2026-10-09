using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationApi.Contracts;
using NotificationApi.Models;
using NotificationApi.Repositories;
using NotificationApi.Services;

namespace NotificationApi.Tests;

[TestClass]
public class NotificationServiceTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeEmailProvider _provider = new();
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly NotificationOptions _options = new() { DuplicateWindow = TimeSpan.FromMinutes(5) };

    private NotificationService CreateService() => new(
        _repository, [_provider], _time, Options.Create(_options), NullLogger<NotificationService>.Instance);

    private async Task<Notification> CreateAsync(NotificationService service, CreateNotificationRequest request)
    {
        var result = await service.CreateAsync(request);
        Assert.AreEqual(CreateNotificationStatus.Created, result.Status);
        return result.Notification!;
    }

    [TestMethod]
    public async Task Create_ValidRequest_IsStoredAsPending()
    {
        var service = CreateService();

        var created = await CreateAsync(service, Requests.Valid());

        var stored = await service.GetByIdAsync(created.Id);
        Assert.IsNotNull(stored);
        Assert.AreNotEqual(Guid.Empty, stored.Id);
        Assert.AreEqual(NotificationStatus.Pending, stored.Status);
        Assert.AreEqual(_time.UtcNow, stored.CreatedAt);
        // No delivery time requested means "due now".
        Assert.AreEqual(_time.UtcNow, stored.RequestedDeliveryTime);
    }

    [TestMethod]
    public async Task Create_InvalidRequest_IsNotStored()
    {
        var service = CreateService();
        var request = Requests.Valid();
        request.Recipient = "not-an-email";

        var result = await service.CreateAsync(request);

        Assert.AreEqual(CreateNotificationStatus.Invalid, result.Status);
        Assert.AreEqual(0, (await service.GetByCustomerAsync("CUST-101")).Count);
    }

    [TestMethod]
    public async Task Create_SameRequestWithinWindow_IsDuplicateAndSentOnlyOnce()
    {
        var service = CreateService();
        var first = await CreateAsync(service, Requests.Valid());

        _time.Advance(TimeSpan.FromMinutes(1));
        var second = await service.CreateAsync(Requests.Valid());

        Assert.AreEqual(CreateNotificationStatus.Duplicate, second.Status);
        Assert.AreEqual(first.Id, second.Notification!.Id);

        await service.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(1, _provider.Sent.Count);
    }

    [TestMethod]
    public async Task Create_SameRequestAfterAlreadySent_IsStillDuplicate()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid());
        await service.ProcessPendingAsync(CancellationToken.None);

        var retry = await service.CreateAsync(Requests.Valid());

        Assert.AreEqual(CreateNotificationStatus.Duplicate, retry.Status);
    }

    [TestMethod]
    public async Task Create_SameRequestAfterWindow_IsAccepted()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid());

        _time.Advance(_options.DuplicateWindow + TimeSpan.FromSeconds(1));
        var second = await service.CreateAsync(Requests.Valid());

        Assert.AreEqual(CreateNotificationStatus.Created, second.Status);
    }

    [TestMethod]
    public async Task Create_RequestDifferingInOneField_IsNotDuplicate()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid(message: "Order 1 confirmed."));

        var second = await service.CreateAsync(Requests.Valid(message: "Order 2 confirmed."));

        Assert.AreEqual(CreateNotificationStatus.Created, second.Status);
    }

    [TestMethod]
    public async Task Create_SameRequestAfterFailedSend_IsAccepted()
    {
        var service = CreateService();
        _provider.ShouldFail = _ => true;
        await CreateAsync(service, Requests.Valid());
        await service.ProcessPendingAsync(CancellationToken.None);

        // The first one was never delivered, so resubmitting cannot cause a double send.
        var retry = await service.CreateAsync(Requests.Valid());

        Assert.AreEqual(CreateNotificationStatus.Created, retry.Status);
    }

    [TestMethod]
    public async Task Create_ConcurrentIdenticalRequests_OnlyOneIsAccepted()
    {
        var service = CreateService();

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => service.CreateAsync(Requests.Valid()))));

        Assert.AreEqual(1, results.Count(r => r.Status == CreateNotificationStatus.Created));
        Assert.AreEqual(49, results.Count(r => r.Status == CreateNotificationStatus.Duplicate));
    }

    [TestMethod]
    public async Task Process_SendsHighPriorityFirst_ThenOldestFirstWithinSamePriority()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid("low-1", "Low"));
        await CreateAsync(service, Requests.Valid("normal-1", "Normal"));
        await CreateAsync(service, Requests.Valid("high-1", "High"));
        await CreateAsync(service, Requests.Valid("normal-2", "Normal"));
        await CreateAsync(service, Requests.Valid("high-2", "High"));

        var summary = await service.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(5, summary.Sent);
        CollectionAssert.AreEqual(
            new[] { "high-1", "high-2", "normal-1", "normal-2", "low-1" },
            _provider.Sent.Select(n => n.Message).ToArray());
    }

    [TestMethod]
    public async Task Process_ProviderFailure_MarksFailedAndContinuesWithTheRest()
    {
        var service = CreateService();
        var bad = await CreateAsync(service, Requests.Valid("bad", "High"));
        var good = await CreateAsync(service, Requests.Valid("good", "Low"));
        _provider.ShouldFail = n => n.Message == "bad";

        var summary = await service.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(new ProcessingSummary(Processed: 2, Sent: 1, Failed: 1), summary);

        var failed = await service.GetByIdAsync(bad.Id);
        Assert.AreEqual(NotificationStatus.Failed, failed!.Status);
        Assert.AreEqual("Provider unavailable.", failed.FailureReason);
        Assert.IsNotNull(failed.ProcessedAt);

        var sent = await service.GetByIdAsync(good.Id);
        Assert.AreEqual(NotificationStatus.Sent, sent!.Status);
        Assert.IsNull(sent.FailureReason);
    }

    [TestMethod]
    public async Task Process_FutureNotification_WaitsUntilItIsDue()
    {
        var service = CreateService();
        var request = Requests.Valid();
        request.RequestedDeliveryTime = _time.UtcNow.AddHours(1);
        var created = await CreateAsync(service, request);

        var early = await service.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(0, early.Processed);
        Assert.AreEqual(NotificationStatus.Pending, (await service.GetByIdAsync(created.Id))!.Status);

        _time.Advance(TimeSpan.FromHours(1));
        var onTime = await service.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(1, onTime.Sent);
    }

    [TestMethod]
    public async Task Process_RunTwice_DoesNotResendAlreadyProcessedNotifications()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid());

        await service.ProcessPendingAsync(CancellationToken.None);
        var second = await service.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, second.Processed);
        Assert.AreEqual(1, _provider.Sent.Count);
    }

    [TestMethod]
    public async Task GetByCustomer_ReturnsOnlyThatCustomersNotifications()
    {
        var service = CreateService();
        await CreateAsync(service, Requests.Valid("for 101"));
        var other = Requests.Valid("for 202");
        other.CustomerId = "CUST-202";
        await CreateAsync(service, other);

        var result = await service.GetByCustomerAsync("CUST-101");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("for 101", result[0].Message);
    }
}
