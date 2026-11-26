using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchIsland
{
    /// <summary>
    /// Minimal QR code encoder: byte mode, error correction level M, versions 1–6 (up to 106 bytes),
    /// which is plenty for a "http://192.168.x.x:port/token/" link. Follows the standard algorithm
    /// (Reed–Solomon over GF(256), zig-zag placement, best of 8 masks).
    /// </summary>
    public static class QrCode
    {
        static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16 };
        static readonly int[] NumBlocks = { 0, 1, 1, 1, 2, 2, 4 };

        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int ver = 0;
            for (int v = 1; v <= 6; v++)
                if (4 + 8 + data.Length * 8 <= DataCodewords(v) * 8) { ver = v; break; }
            if (ver == 0) throw new ArgumentException("Text too long for a small QR code");

            // ---- bit stream ----
            var bits = new List<bool>();
            Append(bits, 0x4, 4);               // byte mode
            Append(bits, data.Length, 8);       // char count (versions 1-9)
            foreach (var b in data) Append(bits, b, 8);
            int cap = DataCodewords(ver) * 8;
            Append(bits, 0, Math.Min(4, cap - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] all = AddEccAndInterleave(codewords, ver);

            // ---- matrix ----
            int size = ver * 4 + 17;
            var modules = new bool[size, size];   // [y, x]
            var isFunc = new bool[size, size];
            DrawFunctionPatterns(modules, isFunc, ver, size);
            DrawCodewords(modules, isFunc, all, size);

            int bestMask = 0; long bestPenalty = long.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(modules, isFunc, m, size);
                DrawFormatBits(modules, isFunc, m, size);
                long p = Penalty(modules, size);
                if (p < bestPenalty) { bestPenalty = p; bestMask = m; }
                ApplyMask(modules, isFunc, m, size); // undo (XOR)
            }
            ApplyMask(modules, isFunc, bestMask, size);
            DrawFormatBits(modules, isFunc, bestMask, size);
            return modules;
        }

        static void Append(List<bool> bits, int val, int len) { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }

        static int RawCodewords(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2) { int n = ver / 7 + 2; result -= (25 * n - 10) * n - 55; }
            return result / 8;
        }
        static int DataCodewords(int ver) { return RawCodewords(ver) - EccPerBlock[ver] * NumBlocks[ver]; }

        // ---- Reed–Solomon ----
        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
            return z & 0xFF;
        }
        static byte[] Divisor(int degree)
        {
            var r = new byte[degree]; r[degree - 1] = 1; int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++) { r[j] = (byte)Mul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
                root = Mul(root, 0x02);
            }
            return r;
        }
        static byte[] Remainder(byte[] data, int start, int len, byte[] div)
        {
            var r = new byte[div.Length];
            for (int k = 0; k < len; k++)
            {
                int factor = data[start + k] ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
            }
            return r;
        }

        static byte[] AddEccAndInterleave(byte[] data, int ver)
        {
            int numBlocks = NumBlocks[ver], eccLen = EccPerBlock[ver], raw = RawCodewords(ver);
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            var div = Divisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
                var ecc = Remainder(data, k, datLen, div);
                var block = new byte[shortLen + 1];
                Array.Copy(data, k, block, 0, datLen);
                k += datLen;
                // short blocks get a dummy byte so all blocks line up; it's skipped when interleaving
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>();
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        // ---- function patterns ----
        static void Set(bool[,] m, bool[,] f, int x, int y, bool dark) { m[y, x] = dark; f[y, x] = true; }

        static void DrawFunctionPatterns(bool[,] m, bool[,] f, int ver, int size)
        {
            for (int i = 0; i < size; i++) { Set(m, f, 6, i, i % 2 == 0); Set(m, f, i, 6, i % 2 == 0); }
            Finder(m, f, 3, 3, size); Finder(m, f, size - 4, 3, size); Finder(m, f, 3, size - 4, size);
            if (ver >= 2)
            {
                int p = size - 7; // versions 2-6 have a single alignment pattern
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, f, p + dx, p + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            DrawFormatBits(m, f, 0, size); // reserve the format areas (overwritten later)
        }

        static void Finder(bool[,] m, bool[,] f, int x, int y, int size)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size) Set(m, f, xx, yy, d != 2 && d != 4);
                }
        }

        static void DrawFormatBits(bool[,] m, bool[,] f, int mask, int size)
        {
            int data = (0 << 3) | mask; // level M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(bits, i));
            Set(m, f, 8, 7, Bit(bits, 6));
            Set(m, f, 8, 8, Bit(bits, 7));
            Set(m, f, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(bits, i));
            Set(m, f, 8, size - 8, true); // the always-dark module
        }

        static bool Bit(int x, int i) { return ((x >> i) & 1) != 0; }

        static void DrawCodewords(bool[,] m, bool[,] f, byte[] data, int size)
        {
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (!f[y, x] && i < data.Length * 8)
                        {
                            m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] f, int mask, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (f[y, x]) continue;
                    bool inv;
                    switch (mask)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) m[y, x] = !m[y, x];
                }
        }

        /// <summary>Simplified penalty (runs, 2x2 blocks, finder look-alikes, balance) — only picks the nicest mask.</summary>
        static long Penalty(bool[,] m, int size)
        {
            long p = 0; int dark = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                        if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                        else run = 1;
                    }
                    for (int b = 0; b + 6 < size; b++)
                    {
                        Func<int, bool> at = k => pass == 0 ? m[a, b + k] : m[b + k, a];
                        if (at(0) && !at(1) && at(2) && at(3) && at(4) && !at(5) && at(6)) p += 40;
                    }
                }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) p += 3;
                }
            int total = size * size;
            p += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10;
            return p;
        }

        /// <summary>Crisp bitmap of the code with a 4-module quiet zone.</summary>
        public static BitmapSource Render(bool[,] modules, int pixelsPerModule, Color dark, Color light)
        {
            int n = modules.GetLength(0), quiet = 4, dim = (n + quiet * 2) * pixelsPerModule;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(light), null, new Rect(0, 0, dim, dim));
                var b = new SolidColorBrush(dark);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (modules[y, x])
                            dc.DrawRectangle(b, null, new Rect((x + quiet) * pixelsPerModule, (y + quiet) * pixelsPerModule, pixelsPerModule, pixelsPerModule));
            }
            var rtb = new RenderTargetBitmap(dim, dim, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
