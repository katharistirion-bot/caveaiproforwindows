using System.Text.Json.Nodes;
using CaveAiProForWindows.Services.Gemini;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class GeminiProxyClientTests
{
    [TestMethod]
    public void IsConfigured_when_default_proxy_url_present() => Assert.IsTrue(GeminiProxyClient.IsConfigured());

    [TestMethod]
    public void BuildChatBody_includes_model_system_and_user_turn()
    {
        var body = GeminiProxyClient.BuildChatBody(GeminiProxyClient.DefaultModel, "You are Cave AI.", "How deep?");
        Assert.AreEqual(GeminiProxyClient.DefaultModel, body["model"]?.GetValue<string>());
        Assert.AreEqual(1, body["contents"]?.AsArray()?.Count);
    }

    [TestMethod]
    public void ExtractText_reads_simplified_wrapper()
    {
        Assert.AreEqual("hello", GeminiProxyClient.ExtractTextFromGenerateContentResponse("{\"text\":\"hello\"}"));
    }
}
