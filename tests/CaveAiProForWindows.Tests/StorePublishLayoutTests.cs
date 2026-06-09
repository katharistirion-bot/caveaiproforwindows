using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class StorePublishLayoutTests
{
    private const string FrameworkDependentRuntimeConfig = """
        {
          "runtimeOptions": {
            "tfm": "net8.0",
            "frameworks": [
              { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
              { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" }
            ]
          }
        }
        """;

    private const string SelfContainedRuntimeConfig = """
        {
          "runtimeOptions": {
            "tfm": "net8.0",
            "includedFrameworks": [
              { "name": "Microsoft.NETCore.App", "version": "8.0.25" },
              { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.25" }
            ]
          }
        }
        """;

    [TestMethod]
    public void IsSelfContainedRuntimeConfig_true_for_includedFrameworks()
    {
        Assert.IsTrue(IsSelfContainedRuntimeConfig(SelfContainedRuntimeConfig));
    }

    [TestMethod]
    public void IsSelfContainedRuntimeConfig_false_for_frameworks()
    {
        Assert.IsFalse(IsSelfContainedRuntimeConfig(FrameworkDependentRuntimeConfig));
    }

    private static bool IsSelfContainedRuntimeConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions))
            return false;

        if (runtimeOptions.TryGetProperty("frameworks", out _))
            return false;

        return runtimeOptions.TryGetProperty("includedFrameworks", out var included)
               && included.GetArrayLength() > 0;
    }
}
