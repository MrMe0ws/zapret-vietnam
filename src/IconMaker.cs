using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;

namespace ZapretVN
{
    // Собирает app.ico (PNG-кадры 16..256) из Shield.Draw. Нужен только для сборки.
    static class IconMaker
    {
        static void Main(string[] args)
        {
            string outFile = args.Length > 0 ? args[0] : "app.ico";
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Shield.Draw(s, Shield.On))
                using (var ms = new MemoryStream())
                {
                    if (s >= 256) bmp.Save(ms, ImageFormat.Png);
                    else WriteDib(bmp, new BinaryWriter(ms));
                    frames.Add(ms.ToArray());
                }
            using (var fs = File.Create(outFile))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
            Console.WriteLine("ok " + outFile);
        }

        // Классический кадр ICO: BITMAPINFOHEADER + 32bpp BGRA снизу вверх + пустая AND-маска
        static void WriteDib(System.Drawing.Bitmap bmp, BinaryWriter w)
        {
            int s = bmp.Width;
            int maskRow = ((s + 31) / 32) * 4;
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskRow * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            w.Write(new byte[maskRow * s]);
            w.Flush();
        }
    }
}
