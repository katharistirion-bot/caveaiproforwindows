using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class PublicLibrarySearchKeyTests
{
    [TestMethod]
    public void FromCaveName_normalizesGreekToLatinLowercase()
    {
        var key = PublicLibrarySearchKey.FromCaveName("\u03A3\u03C0\u03AE\u03BB\u03B1\u03B9\u03BF \u0394\u03BF\u03BA\u03B9\u03BC\u03AE\u03C2");
        StringAssert.Contains(key, "spilaio");
        Assert.IsFalse(key.Contains('\u03AE'));
    }

    [TestMethod]
    public void FromCaveName_lowercasesLatin()
    {
        Assert.AreEqual("alpha cave", PublicLibrarySearchKey.FromCaveName("Alpha Cave"));
    }
}
