using NotificationApi.Validation;

namespace NotificationApi.Tests;

[TestClass]
public class NotificationRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ValidRequest_HasNoErrors()
    {
        var errors = NotificationRequestValidator.Validate(Requests.Valid(), Now);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void MissingRequiredFields_AreAllReportedTogether()
    {
        var request = Requests.Valid();
        request.CustomerId = " ";
        request.Type = null;
        request.Recipient = "";
        request.Message = null;

        var errors = NotificationRequestValidator.Validate(request, Now);

        CollectionAssert.AreEquivalent(
            new[] { "customerId", "type", "recipient", "message" },
            errors.Keys.ToArray());
    }

    [DataTestMethod]
    [DataRow("not-an-email")]
    [DataRow("missing-domain@")]
    [DataRow("@missing-local.com")]
    [DataRow("no-tld@localhost")]
    [DataRow("two@@example.com")]
    [DataRow("Bob <bob@example.com>")]
    [DataRow("trailing.dot@example.")]
    public void InvalidEmailRecipient_IsRejected(string recipient)
    {
        var request = Requests.Valid();
        request.Recipient = recipient;

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.IsTrue(errors.ContainsKey("recipient"));
    }

    [DataTestMethod]
    [DataRow("customer@example.com")]
    [DataRow("first.last+tag@sub.example.co.uk")]
    public void ValidEmailRecipient_IsAccepted(string recipient)
    {
        var request = Requests.Valid();
        request.Recipient = recipient;

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.IsFalse(errors.ContainsKey("recipient"));
    }

    [DataTestMethod]
    [DataRow("Urgent")]
    [DataRow("")]
    [DataRow("2")] // numeric enum values must not slip through
    public void InvalidPriority_IsRejected(string priority)
    {
        var request = Requests.Valid(priority: priority);

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.IsTrue(errors.ContainsKey("priority"));
    }

    [DataTestMethod]
    [DataRow("Low")]
    [DataRow("Normal")]
    [DataRow("High")]
    [DataRow("high")]
    public void AllowedPriority_IsAccepted(string priority)
    {
        var errors = NotificationRequestValidator.Validate(Requests.Valid(priority: priority), Now);

        Assert.IsFalse(errors.ContainsKey("priority"));
    }

    [TestMethod]
    public void UnsupportedType_IsRejected()
    {
        var request = Requests.Valid();
        request.Type = "Sms";

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.IsTrue(errors.ContainsKey("type"));
    }

    [TestMethod]
    public void DeliveryTimeInThePast_IsRejected()
    {
        var request = Requests.Valid();
        request.RequestedDeliveryTime = Now.AddTicks(-1);

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.IsTrue(errors.ContainsKey("requestedDeliveryTime"));
    }

    [TestMethod]
    public void DeliveryTimeExactlyNow_IsAccepted()
    {
        var request = Requests.Valid();
        request.RequestedDeliveryTime = Now;

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void DeliveryTimeOmitted_IsAccepted()
    {
        var request = Requests.Valid();
        request.RequestedDeliveryTime = null;

        var errors = NotificationRequestValidator.Validate(request, Now);

        Assert.AreEqual(0, errors.Count);
    }
}
