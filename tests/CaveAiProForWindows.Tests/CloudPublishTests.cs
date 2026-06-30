using System.Text;
using CaveAiProForWindows.Services.CloudPublish;
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

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
}
