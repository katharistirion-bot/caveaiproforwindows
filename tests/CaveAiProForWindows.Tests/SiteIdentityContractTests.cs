using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SiteIdentityContractTests
{
    [TestMethod]
    public void Fixture_cases_produceExpectedSummaryFragments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "android-reference", "site-identity-fixture.json");
        path = Path.GetFullPath(path);
        Assert.IsTrue(File.Exists(path), $"Missing fixture: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = doc.RootElement.GetProperty("cases");
        var i = 0;
        foreach (var c in cases.EnumerateArray())
        {
            var project = new CaveProjectDocument
            {
                Name = c.GetProperty("name").GetString() ?? "",
                SurveySiteType = c.GetProperty("surveySiteType").GetString(),
            };
            var expected = c.GetProperty("expectedSummaryContains").GetString() ?? "";
            var line = SiteIdentity.SummarizeActiveProject(project);
            StringAssert.Contains(line, expected, $"case {i}: {line}");
            i++;
        }
    }
}
