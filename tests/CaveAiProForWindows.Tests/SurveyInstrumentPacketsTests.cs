using Microsoft.VisualStudio.TestTools.UnitTesting;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyInstrumentPacketsTests
{
    [TestMethod]
    public void Disto_reads_distance_azimuth_and_clino()
    {
        var data = new byte[8];
        data[0] = 0x01;
        data[1] = 0x38;
        data[2] = 0x15;
        WriteU16(data, 3, 16384);
        WriteU16(data, 5, 0);
        Assert.IsTrue(SurveyInstrumentPackets.TryParseDisto(data, out var reading));
        Assert.AreEqual(5.432f, reading.DistanceM, 0.001f);
        Assert.AreEqual(90f, reading.AzimuthDeg, 0.02f);
        Assert.AreEqual(0f, reading.ClinoDeg, 0.02f);
    }

    [TestMethod]
    public void Disto_rejects_a_short_frame()
    {
        Assert.IsFalse(SurveyInstrumentPackets.TryParseDisto(new byte[] { 1, 2, 3 }, out _));
    }

    [TestMethod]
    public void CaveBle_reads_little_endian_floats()
    {
        var data = new byte[17];
        data[0] = 1;
        WriteF(data, 1, 12.5f);
        WriteF(data, 5, -33.25f);
        WriteF(data, 9, 4f);
        WriteF(data, 13, 8.25f);
        Assert.IsTrue(SurveyInstrumentPackets.TryParseCaveBle(data, out var reading));
        Assert.AreEqual(8.25f, reading.DistanceM, 0.001f);
        Assert.AreEqual(12.5f, reading.AzimuthDeg, 0.001f);
        Assert.AreEqual(-33.25f, reading.ClinoDeg, 0.001f);
        Assert.AreEqual(0x56, reading.Ack);
    }

    [TestMethod]
    public void Bric_rejects_a_negative_distance()
    {
        var data = new byte[20];
        WriteF(data, 8, -1f);
        WriteF(data, 12, 180f);
        WriteF(data, 16, 10f);
        Assert.IsFalse(SurveyInstrumentPackets.TryParseBric(data, out _));
    }

    private static void WriteU16(byte[] data, int index, ushort value)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)(value >> 8);
    }

    private static void WriteF(byte[] data, int index, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        bytes.CopyTo(data, index);
    }
}
