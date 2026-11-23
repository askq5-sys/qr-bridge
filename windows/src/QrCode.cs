// Генератор QR-кодов без внешних зависимостей.
// Байтовый режим (UTF-8), версии 1-40, уровни коррекции L/M/Q/H.
// Алгоритм по ISO/IEC 18004, по мотивам QR Code generator library (Project Nayuki, MIT).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace QrBridge
{
    public enum Ecc { L = 0, M = 1, Q = 2, H = 3 }

    /// <summary>Текст не помещается в один QR-код (больше 2953 байт в UTF-8).</summary>
    public class DataTooLongException : Exception { }

    public sealed class QrCode
    {
        readonly bool[,] modules;   // [y, x], true = чёрный модуль

        public int Size { get; private set; }
        public int Version { get { return (Size - 17) / 4; } }
        public bool IsDark(int x, int y) { return modules[y, x]; }

        QrCode(bool[,] modules)
        {
            this.modules = modules;
            Size = modules.GetLength(0);
        }

        public static QrCode Encode(string text, Ecc ecc)
        {
            return new QrCode(Encoder.Encode(Encoding.UTF8.GetBytes(text), (int)ecc));
        }

        /// <summary>Картинка со стороной не больше target и белой рамкой в border модулей.</summary>
        public Bitmap ToBitmap(int target, int border)
        {
            int n = Size + 2 * border;
            int box = Math.Max(1, target / n);
            int side = n * box;
            Bitmap bmp = new Bitmap(side, side, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                int stride = bd.Stride;
                byte[] px = new byte[stride * side];
                for (int i = 0; i < px.Length; i++) px[i] = 255;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        if (!modules[y, x]) continue;
                        int py0 = (y + border) * box, px0 = (x + border) * box * 3;
                        for (int dy = 0; dy < box; dy++)
                        {
                            int row = (py0 + dy) * stride + px0;
                            for (int k = 0; k < box * 3; k++) px[row + k] = 0;
                        }
                    }
                Marshal.Copy(px, 0, bd.Scan0, px.Length);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        // ================================================================ кодирование
        static class Encoder
        {
            static readonly int[] FormatBits = { 1, 0, 3, 2 };

            static readonly int[,] EccPerBlock = {
                { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
                { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
                { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
                { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
            };
            static readonly int[,] NumBlocks = {
                { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
                { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
                { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
                { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }
            };
            static readonly string[] FinderLike = { "10111010000", "00001011101" };

            // таблицы поля GF(256), порождающий многочлен 0x11D
            static readonly int[] Exp = new int[512];
            static readonly int[] Log = new int[256];

            static Encoder()
            {
                int v = 1;
                for (int i = 0; i < 255; i++)
                {
                    Exp[i] = v;
                    Log[v] = i;
                    v <<= 1;
                    if ((v & 0x100) != 0) v ^= 0x11D;
                }
                for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
            }

            static int Mul(int x, int y)
            {
                if (x == 0 || y == 0) return 0;
                return Exp[Log[x] + Log[y]];
            }

            static int RawModules(int ver)
            {
                int r = (16 * ver + 128) * ver + 64;
                if (ver >= 2)
                {
                    int n = ver / 7 + 2;
                    r -= (25 * n - 10) * n - 55;
                    if (ver >= 7) r -= 36;
                }
                return r;
            }

            static int DataCodewords(int ver, int e)
            {
                return RawModules(ver) / 8 - EccPerBlock[e, ver] * NumBlocks[e, ver];
            }

            static int[] Divisor(int deg)
            {
                int[] res = new int[deg];
                res[deg - 1] = 1;
                int root = 1;
                for (int i = 0; i < deg; i++)
                {
                    for (int j = 0; j < deg; j++)
                    {
                        res[j] = Mul(res[j], root);
                        if (j + 1 < deg) res[j] ^= res[j + 1];
                    }
                    root = Mul(root, 0x02);
                }
                return res;
            }

            static int[] Remainder(int[] data, int start, int len, int[] div)
            {
                int[] res = new int[div.Length];
                for (int k = start; k < start + len; k++)
                {
                    int f = data[k] ^ res[0];
                    Array.Copy(res, 1, res, 0, res.Length - 1);
                    res[res.Length - 1] = 0;
                    if (f == 0) continue;
                    for (int i = 0; i < div.Length; i++) res[i] ^= Mul(div[i], f);
                }
                return res;
            }

            static int[] AlignPositions(int ver)
            {
                if (ver == 1) return new int[0];
                int n = ver / 7 + 2;
                int step = (ver * 8 + n * 3 + 5) / (n * 4 - 4) * 2;
                int[] res = new int[n];
                res[0] = 6;
                for (int i = 0; i < n - 1; i++) res[n - 1 - i] = ver * 4 + 10 - i * step;
                return res;
            }

            static void Put(List<int> bits, int val, int n)
            {
                for (int i = n - 1; i >= 0; i--) bits.Add((val >> i) & 1);
            }

            static bool Bit(int x, int i) { return ((x >> i) & 1) != 0; }

            sealed class Grid
            {
                public readonly bool[,] M;
                public readonly bool[,] F;   // служебные модули: маска их не трогает
                public readonly int N;
                public Grid(int n) { N = n; M = new bool[n, n]; F = new bool[n, n]; }
                public void Set(int x, int y, bool v) { M[y, x] = v; F[y, x] = true; }
            }

            static void DrawFormat(Grid g, int fmt, int mask)
            {
                int d = fmt << 3 | mask;
                int r = d;
                for (int i = 0; i < 10; i++) r = (r << 1) ^ ((r >> 9) * 0x537);
                int b = (d << 10 | r) ^ 0x5412;
                int size = g.N;
                for (int i = 0; i < 6; i++) g.Set(8, i, Bit(b, i));
                g.Set(8, 7, Bit(b, 6));
                g.Set(8, 8, Bit(b, 7));
                g.Set(7, 8, Bit(b, 8));
                for (int i = 9; i < 15; i++) g.Set(14 - i, 8, Bit(b, i));
                for (int i = 0; i < 8; i++) g.Set(size - 1 - i, 8, Bit(b, i));
                for (int i = 8; i < 15; i++) g.Set(8, size - 15 + i, Bit(b, i));
                g.Set(8, size - 8, true);
            }

            static bool MaskBit(int m, int x, int y)
            {
                switch (m)
                {
                    case 0: return (x + y) % 2 == 0;
                    case 1: return y % 2 == 0;
                    case 2: return x % 3 == 0;
                    case 3: return (x + y) % 3 == 0;
                    case 4: return (x / 3 + y / 2) % 2 == 0;
                    case 5: return x * y % 2 + x * y % 3 == 0;
                    case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                    default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
                }
            }

            static void ApplyMask(Grid g, int m)
            {
                for (int y = 0; y < g.N; y++)
                    for (int x = 0; x < g.N; x++)
                        if (!g.F[y, x] && MaskBit(m, x, y)) g.M[y, x] = !g.M[y, x];
            }

            static int Penalty(bool[,] m, int size)
            {
                int res = 0;
                char[] line = new char[size];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int a = 0; a < size; a++)
                    {
                        int run = 0;
                        bool prev = false, first = true;
                        for (int b = 0; b < size; b++)
                        {
                            bool v = pass == 0 ? m[a, b] : m[b, a];
                            line[b] = v ? '1' : '0';
                            if (!first && v == prev)
                            {
                                run++;
                                if (run == 5) res += 3;
                                else if (run > 5) res += 1;
                            }
                            else { prev = v; run = 1; first = false; }
                        }
                        string s = new string(line);
                        foreach (string pat in FinderLike)
                        {
                            int start = 0;
                            while (true)
                            {
                                int k = s.IndexOf(pat, start, StringComparison.Ordinal);
                                if (k < 0) break;
                                res += 40;
                                start = k + 1;
                            }
                        }
                    }
                }
                for (int y = 0; y < size - 1; y++)
                    for (int x = 0; x < size - 1; x++)
                    {
                        bool c = m[y, x];
                        if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) res += 3;
                    }
                int dark = 0;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (m[y, x]) dark++;
                int total = size * size;
                res += ((Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10;
                return res;
            }

            public static bool[,] Encode(byte[] data, int e)
            {
                int ver = 0, ccbits = 0;
                for (int v = 1; v <= 40; v++)
                {
                    int cb = v <= 9 ? 8 : 16;
                    if (4 + cb + data.Length * 8 <= DataCodewords(v, e) * 8) { ver = v; ccbits = cb; break; }
                }
                if (ver == 0) throw new DataTooLongException();

                // --- битовый поток ---
                List<int> bits = new List<int>();
                Put(bits, 4, 4);
                Put(bits, data.Length, ccbits);
                foreach (byte by in data) Put(bits, by, 8);
                int cap = DataCodewords(ver, e) * 8;
                Put(bits, 0, Math.Min(4, cap - bits.Count));
                Put(bits, 0, (8 - bits.Count % 8) % 8);
                int pad = 0xEC;
                while (bits.Count < cap) { Put(bits, pad, 8); pad ^= 0xEC ^ 0x11; }
                int[] words = new int[bits.Count / 8];
                for (int i = 0; i < words.Length; i++)
                {
                    int w = 0;
                    for (int j = 0; j < 8; j++) w = (w << 1) | bits[i * 8 + j];
                    words[i] = w;
                }

                // --- коррекция ошибок и перемешивание блоков ---
                int nblocks = NumBlocks[e, ver];
                int ecclen = EccPerBlock[e, ver];
                int raw = RawModules(ver) / 8;
                int nshort = nblocks - raw % nblocks;
                int shortlen = raw / nblocks;
                int[] div = Divisor(ecclen);
                int[][] blocks = new int[nblocks][];
                int pos = 0;
                for (int i = 0; i < nblocks; i++)
                {
                    int dlen = shortlen - ecclen + (i < nshort ? 0 : 1);
                    int[] ecc = Remainder(words, pos, dlen, div);
                    int[] blk = new int[shortlen + 1];
                    int p = 0;
                    for (int j = 0; j < dlen; j++) blk[p++] = words[pos + j];
                    if (i < nshort) blk[p++] = -1;
                    for (int j = 0; j < ecclen; j++) blk[p++] = ecc[j];
                    blocks[i] = blk;
                    pos += dlen;
                }
                List<int> cw = new List<int>();
                for (int i = 0; i < shortlen + 1; i++)
                    for (int j = 0; j < nblocks; j++)
                        if (blocks[j][i] != -1) cw.Add(blocks[j][i]);

                // --- служебные элементы ---
                int size = ver * 4 + 17;
                Grid g = new Grid(size);
                for (int i = 0; i < size; i++)
                {
                    g.Set(6, i, i % 2 == 0);
                    g.Set(i, 6, i % 2 == 0);
                }
                int[][] centers = { new int[] { 3, 3 }, new int[] { size - 4, 3 }, new int[] { 3, size - 4 } };
                foreach (int[] c in centers)
                    for (int dy = -4; dy <= 4; dy++)
                        for (int dx = -4; dx <= 4; dx++)
                        {
                            int x = c[0] + dx, y = c[1] + dy;
                            if (x >= 0 && x < size && y >= 0 && y < size)
                            {
                                int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                                g.Set(x, y, d != 2 && d != 4);
                            }
                        }
                int[] al = AlignPositions(ver);
                int na = al.Length;
                for (int i = 0; i < na; i++)
                    for (int j = 0; j < na; j++)
                    {
                        if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                g.Set(al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                DrawFormat(g, FormatBits[e], 0);
                if (ver >= 7)
                {
                    int r = ver;
                    for (int i = 0; i < 12; i++) r = (r << 1) ^ ((r >> 11) * 0x1F25);
                    int b = ver << 12 | r;
                    for (int i = 0; i < 18; i++)
                    {
                        bool v = ((b >> i) & 1) != 0;
                        int a = size - 11 + i % 3, c2 = i / 3;
                        g.Set(a, c2, v);
                        g.Set(c2, a, v);
                    }
                }

                // --- данные змейкой ---
                int idx = 0, totalBits = cw.Count * 8;
                for (int right = size - 1; right >= 1; right -= 2)
                {
                    if (right == 6) right = 5;
                    bool upward = ((right + 1) & 2) == 0;
                    for (int vert = 0; vert < size; vert++)
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            int y = upward ? size - 1 - vert : vert;
                            if (!g.F[y, x] && idx < totalBits)
                            {
                                g.M[y, x] = ((cw[idx >> 3] >> (7 - (idx & 7))) & 1) != 0;
                                idx++;
                            }
                        }
                }

                // --- выбор маски с наименьшим штрафом ---
                int best = 0, bestPen = int.MaxValue;
                for (int m = 0; m < 8; m++)
                {
                    ApplyMask(g, m);
                    DrawFormat(g, FormatBits[e], m);
                    int pen = Penalty(g.M, size);
                    if (pen < bestPen) { best = m; bestPen = pen; }
                    ApplyMask(g, m);
                }
                ApplyMask(g, best);
                DrawFormat(g, FormatBits[e], best);
                return g.M;
            }
        }
    }
}
