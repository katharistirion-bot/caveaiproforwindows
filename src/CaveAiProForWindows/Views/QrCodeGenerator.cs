using System;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Minimal pure-C# QR code encoder (versions 1-10, byte mode, error correction level M).
/// Returns a bool[,] where true = dark module.
/// Ported from the QR code specification; sufficient for short-to-medium ASCII URLs.
/// </summary>
internal static class QrCodeGenerator
{
    /// <summary>Encodes <paramref name="text"/> into a QR code matrix. Module cell size for display is controlled by the caller.</summary>
    /// <param name="minVersion">Minimum QR version (1-10). Pass 1 for auto-select.</param>
    public static bool[,] Encode(string text, int minVersion = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = System.Text.Encoding.UTF8.GetBytes(text);

        // Pick the smallest version that fits
        int version = Math.Max(1, minVersion);
        for (; version <= 10; version++)
        {
            int cap = ByteCapacity(version);
            if (data.Length <= cap)
                break;
        }
        version = Math.Min(version, 10);

        return BuildMatrix(data, version);
    }

    // Byte-mode capacity for versions 1-10 with error correction level M
    private static readonly int[] ByteCapacityM = { 0, 14, 26, 42, 62, 84, 106, 122, 154, 180, 213 };

    private static int ByteCapacity(int v) => v >= 1 && v <= 10 ? ByteCapacityM[v] : 0;

    private static bool[,] BuildMatrix(byte[] data, int version)
    {
        int size = version * 4 + 17;
        var mat = new bool[size, size];
        var reserved = new bool[size, size]; // marks function modules

        AddFinderPatterns(mat, reserved, size);
        AddTimingPatterns(mat, reserved, size);
        AddAlignmentPatterns(mat, reserved, size, version);
        AddDarkModule(mat, reserved, version);

        // Reserve format info areas (two copies)
        ReserveFormatInfo(reserved, size);

        // Encode data codewords
        byte[] codewords = BuildCodewords(data, version);
        PlaceCodewords(mat, reserved, size, codewords);

        // Apply best mask
        int bestMask = 0;
        int bestPenalty = int.MaxValue;
        bool[,]? bestMat = null;
        for (int m = 0; m < 8; m++)
        {
            var candidate = ApplyMask(mat, reserved, size, m);
            int penalty = ComputePenalty(candidate, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = m;
                bestMat = candidate;
            }
        }

        var result = bestMat ?? mat;
        WriteFormatInfo(result, reserved, size, bestMask);
        return result;
    }

    // ---------- Finder + separator patterns ----------
    private static void AddFinderPatterns(bool[,] m, bool[,] r, int size)
    {
        PlaceFinder(m, r, 0, 0);
        PlaceFinder(m, r, 0, size - 7);
        PlaceFinder(m, r, size - 7, 0);
        // Separators already covered by placing finder patterns (the 8x8 area reserved)
        ReserveSeparator(r, 0, 0, size);
        ReserveSeparator(r, 0, size - 7, size);
        ReserveSeparator(r, size - 7, 0, size);
    }

    private static void PlaceFinder(bool[,] m, bool[,] r, int row, int col)
    {
        bool[,] pat = {
            { true, true, true, true, true, true, true },
            { true, false, false, false, false, false, true },
            { true, false, true, true, true, false, true },
            { true, false, true, true, true, false, true },
            { true, false, true, true, true, false, true },
            { true, false, false, false, false, false, true },
            { true, true, true, true, true, true, true },
        };
        for (int dr = 0; dr < 7; dr++)
            for (int dc = 0; dc < 7; dc++)
            {
                m[row + dr, col + dc] = pat[dr, dc];
                r[row + dr, col + dc] = true;
            }
    }

    private static void ReserveSeparator(bool[,] r, int fr, int fc, int size)
    {
        // Horizontal separator
        int row = fr < size / 2 ? fr + 7 : fr - 1;
        int colStart = fc < size / 2 ? 0 : fc - 1;
        int colEnd = fc < size / 2 ? 8 : size;
        int rowClamped = Math.Clamp(row, 0, size - 1);
        for (int c = Math.Max(0, colStart); c < Math.Min(size, colEnd); c++)
            r[rowClamped, c] = true;
        // Vertical separator
        int col = fc < size / 2 ? fc + 7 : fc - 1;
        int rowStart = fr < size / 2 ? 0 : fr - 1;
        int rowEnd = fr < size / 2 ? 8 : size;
        int colClamped = Math.Clamp(col, 0, size - 1);
        for (int rr = Math.Max(0, rowStart); rr < Math.Min(size, rowEnd); rr++)
            r[rr, colClamped] = true;
    }

    // ---------- Timing patterns ----------
    private static void AddTimingPatterns(bool[,] m, bool[,] r, int size)
    {
        for (int i = 8; i < size - 8; i++)
        {
            m[6, i] = (i % 2 == 0);
            m[i, 6] = (i % 2 == 0);
            r[6, i] = true;
            r[i, 6] = true;
        }
    }

    // ---------- Alignment patterns ----------
    // Positions for versions 1-10
    private static readonly int[][] AlignPos = {
        /*1*/ Array.Empty<int>(),
        /*2*/ new[]{6,18},
        /*3*/ new[]{6,22},
        /*4*/ new[]{6,26},
        /*5*/ new[]{6,30},
        /*6*/ new[]{6,34},
        /*7*/ new[]{6,22,38},
        /*8*/ new[]{6,24,42},
        /*9*/ new[]{6,26,46},
        /*10*/ new[]{6,28,50},
    };

    private static void AddAlignmentPatterns(bool[,] m, bool[,] r, int size, int version)
    {
        if (version < 2) return;
        int[] pos = AlignPos[version - 1];
        foreach (int row in pos)
            foreach (int col in pos)
            {
                if (IsFinderOverlap(row, col, size)) continue;
                PlaceAlignment(m, r, row, col);
            }
    }

    private static bool IsFinderOverlap(int row, int col, int size) =>
        (row <= 8 && col <= 8) || (row <= 8 && col >= size - 8) || (row >= size - 8 && col <= 8);

    private static void PlaceAlignment(bool[,] m, bool[,] r, int row, int col)
    {
        bool[,] pat = {
            { true, true, true, true, true },
            { true, false, false, false, true },
            { true, false, true, false, true },
            { true, false, false, false, true },
            { true, true, true, true, true },
        };
        for (int dr = -2; dr <= 2; dr++)
            for (int dc = -2; dc <= 2; dc++)
            {
                m[row + dr, col + dc] = pat[dr + 2, dc + 2];
                r[row + dr, col + dc] = true;
            }
    }

    private static void AddDarkModule(bool[,] m, bool[,] r, int version)
    {
        int row = version * 4 + 9;
        m[row, 8] = true;
        r[row, 8] = true;
    }

    // ---------- Format info reservation ----------
    private static void ReserveFormatInfo(bool[,] r, int size)
    {
        // Around top-left finder
        for (int i = 0; i <= 8; i++) { r[8, i] = true; r[i, 8] = true; }
        // Top-right and bottom-left copies
        for (int i = 0; i < 8; i++) { r[8, size - 1 - i] = true; r[size - 1 - i, 8] = true; }
    }

    // ---------- Data encoding ----------
    private static byte[] BuildCodewords(byte[] data, int version)
    {
        // Error correction capacities for level M, versions 1-10
        // {total codewords, ec codewords per block, blocks in group 1, data codewords in g1, blocks in g2, data in g2}
        int[,] ecInfo = {
            /*v1 */ {26,10,1,16,0,0},
            /*v2 */ {44,16,1,28,0,0},
            /*v3 */ {70,26,1,44,0,0},
            /*v4 */ {100,18,2,32,0,0},
            /*v5 */ {134,24,2,43,0,0},
            /*v6 */ {172,16,4,27,0,0},
            /*v7 */ {196,18,4,31,0,0},
            /*v8 */ {242,22,2,38,2,39},
            /*v9 */ {292,22,3,36,2,37},
            /*v10*/ {346,26,4,43,1,44},
        };
        int vi = version - 1;
        int totalCW = ecInfo[vi, 0];
        int ecPerBlock = ecInfo[vi, 1];
        int b1Count = ecInfo[vi, 2];
        int b1DataCW = ecInfo[vi, 3];
        int b2Count = ecInfo[vi, 4];
        int b2DataCW = ecInfo[vi, 5];

        // Build raw bit stream: mode indicator (4) + char count indicator + data + terminator + padding
        var bits = new System.Collections.Generic.List<bool>();
        // Byte mode indicator
        bits.AddRange(new[]{false,true,false,false}); // 0100
        // Character count (8 bits for versions 1-9 byte mode)
        AppendBits(bits, data.Length, 8);
        // Data bytes
        foreach (byte b in data)
            AppendBits(bits, b, 8);
        // Terminator (up to 4 zero bits)
        int dataBitsCapacity = (b1Count * b1DataCW + b2Count * b2DataCW) * 8;
        for (int i = 0; i < 4 && bits.Count < dataBitsCapacity; i++) bits.Add(false);
        // Pad to byte boundary
        while (bits.Count % 8 != 0) bits.Add(false);
        // Pad codewords
        bool pad = true;
        while (bits.Count < dataBitsCapacity) { AppendBits(bits, pad ? 0xEC : 0x11, 8); pad = !pad; }

        // Convert to byte array
        int totalData = b1Count * b1DataCW + b2Count * b2DataCW;
        var dataCW = new byte[totalData];
        for (int i = 0; i < totalData; i++)
        {
            byte v = 0;
            for (int j = 0; j < 8; j++)
                if (bits[i * 8 + j]) v |= (byte)(1 << (7 - j));
            dataCW[i] = v;
        }

        // Interleave data blocks + generate and interleave EC codewords
        var dataBlocks = SplitBlocks(dataCW, b1Count, b1DataCW, b2Count, b2DataCW);
        var ecBlocks = new byte[dataBlocks.Count][];
        for (int i = 0; i < dataBlocks.Count; i++)
            ecBlocks[i] = GenerateEC(dataBlocks[i], ecPerBlock);

        var result = new byte[totalCW];
        int idx = 0;
        // Interleave data
        int maxData = b2DataCW > 0 ? b2DataCW : b1DataCW;
        for (int col = 0; col < maxData; col++)
            foreach (var block in dataBlocks)
                if (col < block.Length) result[idx++] = block[col];
        // Interleave EC
        for (int col = 0; col < ecPerBlock; col++)
            foreach (var block in ecBlocks)
                result[idx++] = block[col];

        return result;
    }

    private static System.Collections.Generic.List<byte[]> SplitBlocks(
        byte[] data, int b1Count, int b1Size, int b2Count, int b2Size)
    {
        var blocks = new System.Collections.Generic.List<byte[]>();
        int offset = 0;
        for (int i = 0; i < b1Count; i++) { var b = new byte[b1Size]; Array.Copy(data, offset, b, 0, b1Size); blocks.Add(b); offset += b1Size; }
        for (int i = 0; i < b2Count; i++) { var b = new byte[b2Size]; Array.Copy(data, offset, b, 0, b2Size); blocks.Add(b); offset += b2Size; }
        return blocks;
    }

    // GF(256) polynomial division for Reed-Solomon
    private static byte[] GenerateEC(byte[] data, int ecCount)
    {
        byte[] gen = GeneratorPoly(ecCount);
        byte[] msg = new byte[data.Length + ecCount];
        Array.Copy(data, msg, data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            byte coef = msg[i];
            if (coef == 0) continue;
            for (int j = 1; j < gen.Length; j++)
                msg[i + j] ^= GfMul(gen[j], coef);
        }
        var ec = new byte[ecCount];
        Array.Copy(msg, data.Length, ec, 0, ecCount);
        return ec;
    }

    // Generator polynomial for given number of EC codewords
    private static byte[] GeneratorPoly(int degree)
    {
        byte[] g = { 1 };
        for (int i = 0; i < degree; i++)
        {
            byte root = GfPow(2, i);
            var ng = new byte[g.Length + 1];
            ng[0] = g[0];
            for (int j = 1; j < g.Length; j++) ng[j] = (byte)(GfMul(g[j - 1], root) ^ g[j]);
            ng[g.Length] = GfMul(g[g.Length - 1], root);
            g = ng;
        }
        return g;
    }

    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];
    static QrCodeGenerator()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) GfExp[i] = GfExp[i - 255];
    }

    private static byte GfMul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        return GfExp[GfLog[a] + GfLog[b]];
    }

    private static byte GfPow(int x, int power) => GfExp[(GfLog[(byte)x] * power) % 255];

    // ---------- Module placement ----------
    private static void PlaceCodewords(bool[,] mat, bool[,] r, int size, byte[] codewords)
    {
        int bitIdx = 0;
        int totalBits = codewords.Length * 8;
        bool up = true;
        for (int col = size - 1; col >= 1; col -= 2)
        {
            if (col == 6) col--; // skip timing column
            for (int rowStep = 0; rowStep < size; rowStep++)
            {
                int row = up ? (size - 1 - rowStep) : rowStep;
                for (int dc = 0; dc < 2; dc++)
                {
                    int c = col - dc;
                    if (!r[row, c])
                    {
                        bool bit = bitIdx < totalBits && (codewords[bitIdx / 8] & (0x80 >> (bitIdx % 8))) != 0;
                        mat[row, c] = bit;
                        bitIdx++;
                    }
                }
            }
            up = !up;
        }
    }

    // ---------- Masking ----------
    private static bool[,] ApplyMask(bool[,] mat, bool[,] r, int size, int maskNum)
    {
        var m = (bool[,])mat.Clone();
        for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
                if (!r[row, col] && MaskCondition(maskNum, row, col))
                    m[row, col] = !m[row, col];
        return m;
    }

    private static bool MaskCondition(int mask, int row, int col) => mask switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => (row / 2 + col / 3) % 2 == 0,
        5 => (row * col) % 2 + (row * col) % 3 == 0,
        6 => ((row * col) % 2 + (row * col) % 3) % 2 == 0,
        7 => ((row + col) % 2 + (row * col) % 3) % 2 == 0,
        _ => false,
    };

    private static int ComputePenalty(bool[,] m, int size)
    {
        int penalty = 0;
        // Rule 1: 5+ consecutive same-color modules in row/col
        for (int row = 0; row < size; row++) { int run = 1; for (int c = 1; c < size; c++) { if (m[row, c] == m[row, c - 1]) { run++; if (run == 5) penalty += 3; else if (run > 5) penalty++; } else run = 1; } }
        for (int col = 0; col < size; col++) { int run = 1; for (int r = 1; r < size; r++) { if (m[r, col] == m[r - 1, col]) { run++; if (run == 5) penalty += 3; else if (run > 5) penalty++; } else run = 1; } }
        // Rule 2: 2x2 blocks
        for (int r = 0; r < size - 1; r++) for (int c = 0; c < size - 1; c++) { bool b = m[r, c]; if (b == m[r, c + 1] && b == m[r + 1, c] && b == m[r + 1, c + 1]) penalty += 3; }
        return penalty;
    }

    // ---------- Format info ----------
    private static readonly int[] FormatBitsM = {
        0x5412, 0x5125, 0x5E7C, 0x5B4B, 0x45F9, 0x40CE, 0x4F97, 0x4AA0,
    };

    private static void WriteFormatInfo(bool[,] m, bool[,] r, int size, int mask)
    {
        int fmt = FormatBitsM[mask];
        // Top-left copy (row 8 then col 8)
        int[] cols = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };
        int[] rows = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };
        for (int i = 0; i < 15; i++)
            m[rows[i], cols[i]] = ((fmt >> (14 - i)) & 1) == 1;
        // Top-right copy
        for (int i = 0; i < 8; i++) m[8, size - 1 - i] = ((fmt >> i) & 1) == 1;
        // Bottom-left copy
        for (int i = 0; i < 7; i++) m[size - 7 + i, 8] = ((fmt >> (14 - i)) & 1) == 1;
    }

    private static void AppendBits(System.Collections.Generic.List<bool> bits, int val, int count)
    {
        for (int i = count - 1; i >= 0; i--)
            bits.Add(((val >> i) & 1) == 1);
    }
}