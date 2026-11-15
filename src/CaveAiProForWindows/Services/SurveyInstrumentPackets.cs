using System.Buffers.Binary;

namespace CaveAiProForWindows.Services;

public enum SurveyInstrumentKind
{
    DistoX,
    CaveBle,
    Bric,
}

public readonly record struct SurveyInstrumentReading(
    float DistanceM,
    float AzimuthDeg,
    float ClinoDeg,
    float RollDeg,
    byte Ack);

/// <summary>Same packet layouts the phone uses for DistoX, CaveBLE, and BRIC.</summary>
public static class SurveyInstrumentPackets
{
    public static bool TryParse(SurveyInstrumentKind kind, ReadOnlySpan<byte> data, out SurveyInstrumentReading reading)
    {
        reading = default;
        return kind switch
        {
            SurveyInstrumentKind.DistoX => TryParseDisto(data, out reading),
            SurveyInstrumentKind.CaveBle => TryParseCaveBle(data, out reading),
            SurveyInstrumentKind.Bric => TryParseBric(data, out reading),
            _ => false,
        };
    }

    public static bool TryParseDisto(ReadOnlySpan<byte> data, out SurveyInstrumentReading reading)
    {
        reading = default;
        if (data.Length < 8 || (data[0] & 0x3F) != 0x01)
            return false;
        var high = (data[0] & 0x40) != 0 ? 65536 : 0;
        var rawMm = high + BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
        var distance = rawMm < 99_999 ? rawMm / 1000f : 100f + (rawMm - 100_000) / 100f;
        var azimuth = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]) * 360f / 65536f;
        var rawCl = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        var clino = rawCl < 32768
            ? rawCl * 90f / 16384f
            : (65536 - rawCl) * -90f / 16384f;
        var roll = data[7] * 180f / 128f;
        if (distance < 0f || !float.IsFinite(distance) || !float.IsFinite(azimuth) || !float.IsFinite(clino))
            return false;
        reading = new SurveyInstrumentReading(distance, azimuth, clino, roll, (byte)((data[0] & 0x80) | 0x55));
        return true;
    }

    public static bool TryParseCaveBle(ReadOnlySpan<byte> data, out SurveyInstrumentReading reading)
    {
        reading = default;
        if (data.Length < 17)
            return false;
        var azimuth = BinaryPrimitives.ReadSingleLittleEndian(data[1..]);
        var clino = BinaryPrimitives.ReadSingleLittleEndian(data[5..]);
        var roll = BinaryPrimitives.ReadSingleLittleEndian(data[9..]);
        var distance = BinaryPrimitives.ReadSingleLittleEndian(data[13..]);
        if (!float.IsFinite(distance) || !float.IsFinite(azimuth) || !float.IsFinite(clino) || distance < 0f)
            return false;
        var ack = (byte)((data[0] & 1) == 0 ? 0x55 : 0x56);
        reading = new SurveyInstrumentReading(distance, azimuth, clino, float.IsFinite(roll) ? roll : float.NaN, ack);
        return true;
    }

    public static bool TryParseBric(ReadOnlySpan<byte> data, out SurveyInstrumentReading reading)
    {
        reading = default;
        if (data.Length < 20)
            return false;
        var distance = BinaryPrimitives.ReadSingleLittleEndian(data[8..]);
        var azimuth = BinaryPrimitives.ReadSingleLittleEndian(data[12..]);
        var clino = BinaryPrimitives.ReadSingleLittleEndian(data[16..]);
        if (!float.IsFinite(distance) || !float.IsFinite(azimuth) || !float.IsFinite(clino))
            return false;
        if (distance < 0f || distance > 5000f)
            return false;
        reading = new SurveyInstrumentReading(distance, azimuth, clino, float.NaN, 0);
        return true;
    }
}
