using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NotificationApi.Tests;

/// <summary>
/// End-to-end over HTTP: covers what unit tests cannot (routing, JSON binding, status codes, response envelope).
/// </summary>
[TestClass]
public class NotificationsApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    // A new factory per test gives each test its own empty in-memory store.
    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Post_ThenGetById_ReturnsTheCreatedNotification()
    {
        var post = await _client.PostAsJsonAsync("/api/notifications", Requests.Valid());

        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);
        Assert.IsNotNull(post.Headers.Location);
        Assert.AreEqual(201, (await ReadBodyAsync(post)).GetProperty("statusCode").GetInt32());

        var get = await _client.GetAsync(post.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);

        var body = await ReadBodyAsync(get);
        Assert.AreEqual(200, body.GetProperty("statusCode").GetInt32());
        var data = body.GetProperty("data");
        Assert.AreEqual("CUST-101", data.GetProperty("customerId").GetString());
        Assert.AreEqual("Pending", data.GetProperty("status").GetString());
        Assert.AreEqual("Normal", data.GetProperty("priority").GetString());
    }

    [TestMethod]
    public async Task Post_InvalidRequest_Returns400WithFieldErrors()
    {
        var request = Requests.Valid(priority: "Urgent");
        request.Recipient = "not-an-email";

        var response = await _client.PostAsJsonAsync("/api/notifications", request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadBodyAsync(response);
        Assert.AreEqual(400, body.GetProperty("statusCode").GetInt32());
        var errors = body.GetProperty("data");
        Assert.IsTrue(errors.TryGetProperty("recipient", out _));
        Assert.IsTrue(errors.TryGetProperty("priority", out _));
    }

    [TestMethod]
    public async Task Post_MalformedJson_Returns400InTheSameEnvelope()
    {
        var content = new StringContent("{\"customerId\":", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/notifications", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadBodyAsync(response);
        Assert.AreEqual(400, body.GetProperty("statusCode").GetInt32());
        Assert.AreEqual(JsonValueKind.Object, body.GetProperty("data").ValueKind);
    }

    [TestMethod]
    public async Task Post_DuplicateRequest_Returns409NamingTheExistingNotification()
    {
        var first = await _client.PostAsJsonAsync("/api/notifications", Requests.Valid());
        var firstId = (await ReadBodyAsync(first)).GetProperty("data").GetProperty("id").GetString();

        var second = await _client.PostAsJsonAsync("/api/notifications", Requests.Valid());

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        var body = await ReadBodyAsync(second);
        Assert.AreEqual(409, body.GetProperty("statusCode").GetInt32());
        StringAssert.Contains(body.GetProperty("data").GetString(), firstId);
    }

    [TestMethod]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/notifications/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadBodyAsync(response);
        Assert.AreEqual(404, body.GetProperty("statusCode").GetInt32());
        Assert.AreEqual(JsonValueKind.String, body.GetProperty("data").ValueKind);
    }

    [TestMethod]
    public async Task Process_ThenGetByCustomer_ShowsNotificationAsSent()
    {
        await _client.PostAsJsonAsync("/api/notifications", Requests.Valid());

        var process = await _client.PostAsync("/api/notifications/process", null);
        Assert.AreEqual(HttpStatusCode.OK, process.StatusCode);
        Assert.AreEqual(1, (await ReadBodyAsync(process)).GetProperty("data").GetProperty("sent").GetInt32());

        var list = (await ReadBodyAsync(await _client.GetAsync("/api/customers/CUST-101/notifications"))).GetProperty("data");
        Assert.AreEqual(1, list.GetArrayLength());
        Assert.AreEqual("Sent", list[0].GetProperty("status").GetString());
    }

    private static Task<JsonElement> ReadBodyAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>();
}
