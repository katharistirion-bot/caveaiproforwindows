using System.Text;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.SurfaceMap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CloudPublishTests
{
    [TestMethod]
    public void FirebaseIdTokenParser_reads_exp_and_sub_from_jwt_payload()
    {
        var payload = """{"sub":"uid123","email":"u@example.com","exp":4102444800,"iat":1700000000}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        Assert.IsTrue(FirebaseIdTokenParser.TryParse(jwt, out var token));
        Assert.IsNotNull(token);
        Assert.AreEqual("uid123", token!.Subject);
        Assert.AreEqual("u@example.com", token.Email);
        Assert.IsFalse(token.IsExpired());
    }

    [TestMethod]
    public void FirestoreFieldBuilder_encodes_string_and_integer_fields()
    {
        var fields = FirestoreFieldBuilder.BuildFields(
        [
            new KeyValuePair<string, object?>("cartographyImageUrls", new[] { "https://example.com/a.png" }),
            new KeyValuePair<string, object?>("updatedAtMs", 1700000000L),
        ]);

        Assert.IsTrue(fields.ContainsKey("cartographyImageUrls"));
        Assert.IsTrue(fields.ContainsKey("updatedAtMs"));
        var urlField = fields["cartographyImageUrls"] as Dictionary<string, object>;
        Assert.IsNotNull(urlField);
        Assert.IsTrue(urlField!.ContainsKey("arrayValue"));
        var msField = fields["updatedAtMs"] as Dictionary<string, object>;
        Assert.IsNotNull(msField);
        Assert.AreEqual("1700000000", msField!["integerValue"]);
    }

    [TestMethod]
    public void FirestoreFieldBuilder_encodes_string_array()
    {
        var encoded = FirestoreFieldBuilder.EncodeStringArray(new[] { "https://a.png", "https://b.png" });
        Assert.IsTrue(encoded.ContainsKey("arrayValue"));
    }

    [TestMethod]
    public void BuildStorageMediaUrl_includes_download_token_when_present()
    {
        var url = FirebaseRestClient.BuildStorageMediaUrl(
            "bucket.appspot.com",
            "users/u/file.png",
            "abc-def");
        StringAssert.Contains(url, "alt=media");
        StringAssert.Contains(url, "token=abc-def");
    }

    [TestMethod]
    public void DesktopAuthProtocol_parses_valid_token_message()
    {
        var payload = """{"sub":"uid123","exp":4102444800,"iat":1700000000}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        var json = $$"""{"type":"caveai-desktop-auth-token","idToken":"{{jwt}}"}""";
        Assert.IsTrue(DesktopAuthProtocol.TryParseTokenMessage(json, out var token));
        Assert.IsNotNull(token);
        Assert.AreEqual("uid123", token!.Subject);
    }

    [TestMethod]
    public void DesktopAuthProtocol_rejects_unknown_message_type()
    {
        var json = """{"type":"other","idToken":"x.y.z"}""";
        Assert.IsFalse(DesktopAuthProtocol.TryParseTokenMessage(json, out _));
    }

    [TestMethod]
    public void DesktopAuthProtocol_rejects_malformed_json()
    {
        Assert.IsFalse(DesktopAuthProtocol.TryParseTokenMessage("{not json", out _));
    }

    [TestMethod]
    public void DesktopAuthProtocol_parses_valid_appcheck_token_message()
    {
        var payload = """{"exp":4102444800}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        var json = $$"""{"type":"caveai-desktop-appcheck-token","appCheckToken":"{{jwt}}"}""";
        Assert.IsTrue(DesktopAuthProtocol.TryParseAppCheckTokenMessage(json, out var token));
        Assert.IsNotNull(token);
        Assert.AreEqual(jwt, token!.Raw);
        Assert.IsFalse(token.IsExpired());
    }

    [TestMethod]
    public void FirebaseAppCheckTokenCache_returns_usable_token_until_expiry()
    {
        var payload = """{"exp":4102444800}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        Assert.IsTrue(FirebaseAppCheckTokenParser.TryParse(jwt, out var parsed));
        Assert.IsNotNull(parsed);

        var cache = new FirebaseAppCheckTokenCache();
        cache.Update(parsed!);
        Assert.IsNotNull(cache.TryGetUsableToken());
    }

    [TestMethod]
    public async Task FirebaseRestClient_attaches_appcheck_header_when_cached()
    {
        var payload = """{"exp":4102444800}""";
        var jwt = "aaa." + Base64UrlEncode(payload) + ".sig";
        Assert.IsTrue(FirebaseAppCheckTokenParser.TryParse(jwt, out var appCheck));
        Assert.IsNotNull(appCheck);

        var appCheckCache = new FirebaseAppCheckTokenCache();
        appCheckCache.Update(appCheck!);

        var handler = new AppCheckRecordingHandler();
        handler.Enqueue(System.Net.HttpStatusCode.OK, """{"name":"x"}""");

        using var rest = new FirebaseRestClient(
            new FirebaseProjectConfig { ProjectId = "test-proj", StorageBucket = "b.appspot.com" },
            handler,
            appCheckCache);

        var idPayload = """{"sub":"uid","exp":4102444800}""";
        var idJwt = "aaa." + Base64UrlEncode(idPayload) + ".sig";
        Assert.IsTrue(FirebaseIdTokenParser.TryParse(idJwt, out var idToken));
        Assert.IsNotNull(idToken);

        await rest.UploadBytesAsync(idToken!, "users/u/file.png", [1], "image/png");
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.Requests[0].Headers.ContainsKey("X-Firebase-AppCheck"));
        Assert.AreEqual(jwt, handler.Requests[0].Headers["X-Firebase-AppCheck"]);
    }

    private sealed class AppCheckRecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(System.Net.HttpStatusCode Code, string Body)> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public sealed class RecordedRequest
        {
            public required Dictionary<string, string> Headers { get; init; }
        }

        public void Enqueue(System.Net.HttpStatusCode code, string body) => _responses.Enqueue((code, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
                headers[h.Key] = string.Join(",", h.Value);
            Requests.Add(new RecordedRequest { Headers = headers });

            var (code, body) = _responses.Dequeue();
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(body),
            };
        }
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [TestMethod]
    public void PublishedCaveSyncPayload_patch_keys_are_subset_of_firestore_allow_list()
    {
        var pairs = PublishedCaveSyncPayload.BuildOwnerSyncUpdatePairs(new PublishedCaveSyncPayload.OwnerSyncInput
        {
            CaveName = "Demo Cave",
            Description = "Survey sync from Windows.",
            Depth = 12.5,
            Length = 84.2,
            MergedImageUrls = ["https://example.com/gallery.jpg"],
            MergedCartographyImageUrls = ["https://example.com/ai_map.png"],
            SurveyJsonUrl = "https://example.com/data.json",
            LastSyncedAtMs = 1_700_000_000_000,
        });

        PublishedCaveSyncPayload.AssertKeysSubsetAllowed(pairs.Select(p => p.Key));
        CollectionAssert.IsSubsetOf(
            pairs.Select(p => p.Key).ToList(),
            PublishedCaveSyncPayload.SyncUpdateAllowedKeys.ToList());
        Assert.IsFalse(pairs.Any(p => p.Key is "updatedAtMs" or "sourceClient" or "galleryPhotoUrls" or "structureMaskUrl"));
        Assert.IsTrue(pairs.Any(p => p.Key == "lastSyncedAtMs"));
        Assert.IsTrue(pairs.Any(p => p.Key == "imageUrls"));
        Assert.IsTrue(pairs.Any(p => p.Key == "surveyJsonUrl"));
    }

    [TestMethod]
    public void CloudPublishRetryStore_enqueue_and_remove_round_trip()
    {
        CloudPublishRetryStore.Clear();
        CloudPublishRetryStore.Enqueue("Demo Cave", @"C:\temp\demo.zip", "Network timeout");
        var all = CloudPublishRetryStore.LoadAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("Demo Cave", all[0].ProjectName);
        CloudPublishRetryStore.Remove("Demo Cave");
        Assert.AreEqual(0, CloudPublishRetryStore.LoadAll().Count);
    }

    [TestMethod]
    public void SurfaceMapLayerPrefsSync_merge_injects_layers_query()
    {
        var url = SurfaceMapLayerPrefsSync.MergeLayerParamsIntoUrl(
            "https://www.caveaipro.com/map?view=explore&lat=40&lon=22");
        StringAssert.Contains(url, "layers=");
        StringAssert.Contains(url, "hillshade");
        StringAssert.Contains(url, "copernicus");
    }

    [TestMethod]
    public void SurfaceMapLayerPrefsSync_sync_from_explore_url()
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.HillshadeEnabled = false;
        all.SurfaceMap.CopernicusDsmEnabled = false;
        AppUiSettingsStore.Save(all);

        SurfaceMapLayerPrefsSync.SyncSurfaceMapFromExploreUrl(
            "https://www.caveaipro.com/map?view=explore&layers=hillshade=1,copernicus=1");

        var loaded = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        Assert.IsTrue(loaded.HillshadeEnabled);
        Assert.IsTrue(loaded.CopernicusDsmEnabled);
    }
}
