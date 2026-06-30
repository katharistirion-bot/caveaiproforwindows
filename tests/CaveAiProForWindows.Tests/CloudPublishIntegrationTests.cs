using System.Net;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CloudPublishIntegrationTests
{
    [TestMethod]
    public async Task PublishAsync_uploads_storage_and_patches_firestore()
    {
        var payload = """{"sub":"uid123","exp":4102444800,"iat":1700000000}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        Assert.IsTrue(FirebaseIdTokenParser.TryParse(jwt, out var token));
        Assert.IsNotNull(token);

        var handler = new RecordingHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"name":"users/uid123/desktop_publishes/s1/cave/ai_map.png","bucket":"b.appspot.com","downloadTokens":"tok1"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"name":"users/uid123/desktop_publishes/s1/cave/data.json","bucket":"b.appspot.com","downloadTokens":"tok2"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"fields":{"caveName":{"stringValue":"Demo Cave"},"description":{"stringValue":"Existing"},"depth":{"doubleValue":5},"length":{"doubleValue":10},"imageUrls":{"arrayValue":{"values":[]}},"cartographyImageUrls":{"arrayValue":{"values":[]}}}}""");
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var cache = new FirebaseAuthTokenCache();
        var rest = new FirebaseRestClient(new FirebaseProjectConfig { ProjectId = "test-proj", StorageBucket = "b.appspot.com" }, handler);
        var service = new CloudPublishService(cache, rest);

        var project = new CaveProjectDocument { Name = "Demo Cave", Shots = [] };
        var bundle = new CloudPublishArtifactBundle
        {
            Project = project,
            PublishedCaveDocId = "caveDoc1",
            AiMapPng = [1, 2, 3],
            SurveyJsonUtf8 = Encoding.UTF8.GetBytes("[]"),
        };

        var metadata = await service.PublishAsync(bundle, token!);
        Assert.AreEqual("caveDoc1", metadata.PublishedCaveDocId);
        Assert.IsTrue(metadata.CartographyImageUrls?[0].Contains("token=tok1", StringComparison.Ordinal));
        Assert.AreEqual(4, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[0].Uri, "firebasestorage.googleapis.com");
        StringAssert.Contains(handler.Requests[2].Uri, "published_caves/caveDoc1");
        Assert.AreEqual("GET", handler.Requests[2].Method);
        StringAssert.Contains(handler.Requests[3].Uri, "published_caves/caveDoc1");
        Assert.AreEqual("PATCH", handler.Requests[3].Method);
        var patchBody = await handler.Requests[3].ReadBodyAsync();
        StringAssert.Contains(patchBody, "lastSyncedAtMs");
        StringAssert.Contains(patchBody, "surveyJsonUrl");
        Assert.IsFalse(patchBody.Contains("updatedAtMs", StringComparison.Ordinal));
        Assert.IsFalse(patchBody.Contains("galleryPhotoUrls", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PublishedCaveUrlParser_reads_cave_query_and_workspace_path()
    {
        Assert.IsTrue(PublishedCaveUrlParser.TryExtractDocId(
            "https://www.caveaipro.com/map?embed=windows&cave=abc123", out var q));
        Assert.AreEqual("abc123", q);

        Assert.IsTrue(PublishedCaveUrlParser.TryExtractDocId(
            "https://www.caveaipro.com/workspace/ws-id-9", out var w));
        Assert.AreEqual("ws-id-9", w);

        Assert.IsFalse(PublishedCaveUrlParser.TryExtractDocId("https://www.caveaipro.com/map", out _));
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Code, string Body)> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public sealed class RecordedRequest
        {
            public required string Method { get; init; }

            public required string Uri { get; init; }

            public required Func<Task<string>> ReadBodyAsync { get; init; }
        }

        public void Enqueue(HttpStatusCode code, string body) => _responses.Enqueue((code, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyCopy = "";
            if (request.Content != null)
                bodyCopy = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest
            {
                Method = request.Method.Method,
                Uri = request.RequestUri?.ToString() ?? "",
                ReadBodyAsync = () => Task.FromResult(bodyCopy),
            });
            var (code, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.InternalServerError, "no mock");
            return await Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
