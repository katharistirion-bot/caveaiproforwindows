using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Gemini;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class LidarAndCloudPlanTests
{
    [TestMethod]
    public void Square_cloud_makes_an_outline()
    {
        var xs = new List<float>();
        var ys = new List<float>();
        for (var i = 0; i <= 20; i++)
        {
            xs.Add(i);
            ys.Add(0);
            xs.Add(i);
            ys.Add(20);
            xs.Add(0);
            ys.Add(i);
            xs.Add(20);
            ys.Add(i);
        }

        var lines = LidarPlanOutline.Polylines(xs, ys);
        Assert.IsTrue(lines.Count > 0, "expected a wall outline");
        Assert.IsTrue(lines[0].Count >= 4);
    }

    [TestMethod]
    public void Degree_cloud_is_not_a_plan_outline()
    {
        var xs = new List<float>();
        var ys = new List<float>();
        for (var i = 0; i < 12; i++)
        {
            xs.Add(23.70f + i * 0.001f);
            ys.Add(37.90f);
        }

        Assert.IsTrue(LidarPlanOutline.LooksGeographic(xs, ys));
        Assert.AreEqual(0, LidarPlanOutline.Polylines(xs, ys).Count);
    }

    [TestMethod]
    public void Binary_frame_reads_xyz()
    {
        var data = new byte[7 + 12];
        data[0] = 0xCA;
        data[1] = 0x1D;
        data[2] = 0x01;
        data[3] = 0x02;
        data[4] = 0x01;
        data[5] = 1;
        BitConverter.GetBytes(4f).CopyTo(data, 7);
        BitConverter.GetBytes(5f).CopyTo(data, 11);
        BitConverter.GetBytes(6f).CopyTo(data, 15);
        var points = LidarPlanOutline.ParseBinaryFrame(data);
        Assert.AreEqual(1, points.Count);
        Assert.AreEqual(4f, points[0].X, 0.001f);
        Assert.AreEqual(6f, points[0].Z, 0.001f);
    }

    [TestMethod]
    public void Cloud_response_image_is_decoded()
    {
        var png = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var raw = "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"" + png + "\"}}]}}]}";
        var bytes = CloudPlanRenderer.TryExtractInlineImage(raw);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, bytes);
    }
}
