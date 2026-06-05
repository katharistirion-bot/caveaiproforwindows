using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.SketchAssist;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class GenerativeMapTests
{
    [TestMethod]
    public void GreyLevelFromVerticalExtent_scales_with_passage_height()
    {
        var low = SurveyLrudWallGeometry.GreyLevelFromVerticalExtent(1f, 10f);
        var high = SurveyLrudWallGeometry.GreyLevelFromVerticalExtent(9f, 10f);
        Assert.IsTrue(high > low);
        Assert.IsTrue(low >= 96);
        Assert.IsTrue(high <= 255);
    }

    [TestMethod]
    public void LrudRibbonMaskPolygons_produce_filled_corridor_for_traverse_with_lrud()
    {
        var shots = new List<ShotRecord>
        {
            new()
            {
                FromStation = "A",
                ToStation = "B",
                Distance = 10,
                Azimuth = 0,
                Clino = 0,
                L = 2,
                R = 1.5f,
                U = 1.2f,
                D = 0.8f,
            },
            new()
            {
                FromStation = "B",
                ToStation = "C",
                Distance = 8,
                Azimuth = 90,
                Clino = 0,
                L = 1f,
                R = 2f,
                U = 2f,
                D = 1f,
            },
        };
        var project = new CaveProjectDocument { Name = "LRUD", Shots = shots };
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var masks = SurveyLrudWallGeometry.BuildPlanLrudRibbonMaskPolygons(shots, coords);
        Assert.IsTrue(masks.Count >= 1);
        Assert.IsTrue(masks[0].Outline.Points.Count >= 4);
        Assert.IsTrue(masks[0].GreyLevel >= 96);
    }

    [TestMethod]
    public void StructureMaskPreprocessor_builds_non_empty_lrud_mask_png()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var project = new CaveProjectDocument
            {
                Name = "Corridor",
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "1",
                        ToStation = "2",
                        Distance = 12,
                        Azimuth = 0,
                        Clino = 0,
                        L = 2.5f,
                        R = 2f,
                        U = 1.5f,
                        D = 1f,
                    },
                ],
            };

            var session = SketchAssistInputBuilder.TryBuild(project, new Canvas { Width = 960, Height = 640 }, 960, 640);
            Assert.IsNotNull(session);

            var png = StructureMaskControlNetPreprocessor.TryBuildGrayscaleLrudMaskPng(session!);
            Assert.IsNotNull(png);
            Assert.IsTrue(png!.Length > 512);
        });
    }

    [TestMethod]
    public void StructureMaskPreprocessor_inverts_and_downscales_png()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var whiteOnBlack = CreateSolidPng(256, 128, white: true);
            var prepared = StructureMaskControlNetPreprocessor.PrepareScribbleInput(whiteOnBlack, maxEdgePixels: 128);
            Assert.IsNotNull(prepared);
            Assert.IsTrue(prepared!.Length > 64);
            Assert.AreEqual(0x89, prepared[0]);
        });
    }

    [TestMethod]
    public void ReplicateApiClient_reads_string_output_url()
    {
        var prediction = new ReplicatePrediction
        {
            Status = "succeeded",
            Output = System.Text.Json.JsonDocument.Parse("\"https://example.com/out.png\"").RootElement,
        };
        var url = ReplicateApiClient.TryGetFirstOutputUrl(prediction);
        Assert.AreEqual("https://example.com/out.png", url);
    }

    [TestMethod]
    public void ReplicateApiClient_reads_array_output_url()
    {
        var prediction = new ReplicatePrediction
        {
            Status = "succeeded",
            Output = System.Text.Json.JsonDocument.Parse("[\"https://example.com/a.png\"]").RootElement,
        };
        var url = ReplicateApiClient.TryGetFirstOutputUrl(prediction);
        Assert.AreEqual("https://example.com/a.png", url);
    }

    private static byte[] CreateSolidPng(int width, int height, bool white)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var v = white ? (byte)255 : (byte)0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = v;
            pixels[i + 1] = v;
            pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }

        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            width,
            height,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
