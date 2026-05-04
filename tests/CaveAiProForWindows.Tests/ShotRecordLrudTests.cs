using CaveAiProForWindows.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ShotRecordLrudTests
{
    [TestMethod]
    public void EffectivePlanLrud_prefers_radials_when_classic_lrud_zero_even_without_hasSplayWatch()
    {
        var radials = Enumerable.Range(0, 12).Select(i => (float)(i + 1)).ToList();
        var s = new ShotRecord
        {
            HasSplayWatch = false,
            L = 0,
            R = 0,
            U = 0,
            D = 0,
            Radials = radials,
        };
        var (l, r, u, d) = s.EffectivePlanLrud();
        Assert.AreEqual(10f, l, 1e-5f); // index 9
        Assert.AreEqual(4f, r, 1e-5f); // index 3
        Assert.AreEqual(1f, u, 1e-5f); // index 0
        Assert.AreEqual(7f, d, 1e-5f); // index 6
    }

    [TestMethod]
    public void EffectivePlanLrud_keeps_classic_when_nonzero()
    {
        var radials = Enumerable.Range(0, 12).Select(i => (float)(i + 1)).ToList();
        var s = new ShotRecord
        {
            HasSplayWatch = false,
            L = 2f,
            R = 3f,
            U = 0.5f,
            D = 0.5f,
            Radials = radials,
        };
        var (l, r, u, d) = s.EffectivePlanLrud();
        Assert.AreEqual(2f, l);
        Assert.AreEqual(3f, r);
        Assert.AreEqual(0.5f, u);
        Assert.AreEqual(0.5f, d);
    }
}
