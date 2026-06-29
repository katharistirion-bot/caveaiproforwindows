using System.Net;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FirebaseCallableClientTests
{
    [TestMethod]
    public async Task FirebaseCallableClient_maps_callable_error_to_exception()
    {
        var handler = new StubHandler(_ =>
        {
            var body = """{"error":{"message":"denied","status":"PERMISSION_DENIED"}}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });

        var client = new FirebaseCallableClient(
            "testCallable",
            http: new HttpClient(handler));

        var token = new FirebaseIdToken
        {
            Raw = "fake.jwt.token",
            Subject = "uid123",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            IssuedAtUtc = DateTimeOffset.UtcNow,
        };

        var ex = await Assert.ThrowsExceptionAsync<FirebaseCallableException>(() =>
            client.InvokeAsync<JsonElement>(token, new { modelVersion = "x", input = new { } }));

        Assert.IsTrue(ex.IsPermissionDenied);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_factory(request));
    }
}
