using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Drawing.Imaging;

class Program
{
    static void Main()
    {
        string wzlPath = @"d:\sources\github3\MirClient\Bin\Data\mmap.wzl";
        string wzxPath = @"d:\sources\github3\MirClient\Bin\Data\mmap.wzx";
        string outDir = @"d:\sources\github3\MirClient\minmap_preview";
        Directory.CreateDirectory(outDir);

        // === 1. 读 WZX 偏移表 ===
        byte[] wzx = File.ReadAllBytes(wzxPath);
        // TWZXIndexHeader: Title(44 bytes) + IndexCount(4 bytes) = 48 bytes
        int indexCount = BitConverter.ToInt32(wzx, 44);
        Console.WriteLine("WZX IndexCount = " + indexCount);

        var offsets = new int[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            offsets[i] = BitConverter.ToInt32(wzx, 48 + i * 4);
        }
        Console.WriteLine("Offsets loaded, first = {0}, last = {1}", offsets[0], offsets[indexCount - 1]);

        // === 2. 读 WZL 文件 ===
        byte[] wzl = File.ReadAllBytes(wzlPath);
        Console.WriteLine("WZL size = " + wzl.Length);

        // === 3. 逐张解码 ===
        int success = 0, fail = 0, skip = 0;
        using (var outMs = new MemoryStream())
        {
            for (int idx = 0; idx < indexCount; idx++)
            {
                int pos = offsets[idx];
                if (pos <= 0 || pos + 16 > wzl.Length) { skip++; continue; }

                // 读 TWZLImageInfo (16 bytes)
                byte enc1 = wzl[pos];
                byte enc2 = wzl[pos + 1];
                // byte type1 = wzl[pos+2];
                // byte type2 = wzl[pos+3];
                short w = BitConverter.ToInt16(wzl, pos + 4);
                short h = BitConverter.ToInt16(wzl, pos + 6);
                short px = BitConverter.ToInt16(wzl, pos + 8);
                short py = BitConverter.ToInt16(wzl, pos + 10);
                uint len = BitConverter.ToUInt32(wzl, pos + 12);

                if (w <= 0 || h <= 0 || w > 2000 || h > 2000) { skip++; continue; }

                int bitSize = enc1 switch { 3 => 8, 5 => 16, 6 => 24, 7 => 32, _ => 0 };
                if (bitSize == 0) { skip++; continue; }

                // 像素数据
                byte[] pixels;
                int headerSize = 16;

                if (len > 0)
                {
                    // 压缩数据：ZLib 解压
                    int dataOffset = pos + headerSize;
                    if (dataOffset + len > wzl.Length) { skip++; continue; }
                    byte[] compressed = new byte[len];
                    Array.Copy(wzl, dataOffset, compressed, 0, (int)len);
                    try
                    {
                        using (var ms = new MemoryStream(compressed))
                        using (var zs = new ZLibStream(ms, CompressionMode.Decompress))
                        {
                            outMs.SetLength(0);
                            zs.CopyTo(outMs);
                        }
                        pixels = outMs.ToArray();
                    }
                    catch { fail++; continue; }
                }
                else
                {
                    // 未压缩
                    int expected = w * h * (bitSize / 8);
                    int dataOffset = pos + headerSize;
                    if (dataOffset + expected > wzl.Length) { skip++; continue; }
                    pixels = new byte[expected];
                    Array.Copy(wzl, dataOffset, pixels, 0, expected);
                }

                // === 4. 转成 BMP ===
                try
                {
                    Bitmap bmp;
                    int bpp = bitSize;
                    bool isAlpha = (enc2 == 9);

                    if (bpp == 8)
                    {
                        // 8位灰度 → RGB24
                        bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        unsafe
                        {
                            byte* dst = (byte*)bd.Scan0;
                            int stride = bd.Stride;
                            fixed (byte* src = pixels)
                            {
                                for (int y = 0; y < h; y++)
                                {
                                    byte* d = dst + y * stride;
                                    byte* s = src + y * w;
                                    for (int x = 0; x < w; x++)
                                    {
                                        byte g = s[x];
                                        d[x * 3] = g;
                                        d[x * 3 + 1] = g;
                                        d[x * 3 + 2] = g;
                                    }
                                }
                            }
                        }
                        bmp.UnlockBits(bd);
                    }
                    else if (bpp == 16)
                    {
                        // 16位 → RGB24 (RGB565)
                        bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        unsafe
                        {
                            byte* dst = (byte*)bd.Scan0;
                            int stride = bd.Stride;
                            fixed (byte* src = pixels)
                            {
                                ushort* sp = (ushort*)src;
                                for (int y = 0; y < h; y++)
                                {
                                    byte* d = dst + y * stride;
                                    for (int x = 0; x < w; x++)
                                    {
                                        ushort p = sp[y * w + x];
                                        int r = (p >> 11) & 0x1F;
                                        int g = (p >> 5) & 0x3F;
                                        int b = p & 0x1F;
                                        d[x * 3] = (byte)((r << 3) | (r >> 2));
                                        d[x * 3 + 1] = (byte)((g << 2) | (g >> 4));
                                        d[x * 3 + 2] = (byte)((b << 3) | (b >> 2));
                                    }
                                }
                            }
                        }
                        bmp.UnlockBits(bd);
                    }
                    else if (bpp == 24)
                    {
                        bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        unsafe
                        {
                            byte* dst = (byte*)bd.Scan0;
                            int stride = bd.Stride;
                            fixed (byte* src = pixels)
                            {
                                for (int y = 0; y < h; y++)
                                {
                                    byte* d = dst + y * stride;
                                    byte* s = src + y * w * 3;
                                    for (int x = 0; x < w; x++)
                                    {
                                        d[x * 3] = s[x * 3 + 2];     // B
                                        d[x * 3 + 1] = s[x * 3 + 1]; // G
                                        d[x * 3 + 2] = s[x * 3];     // R
                                    }
                                }
                            }
                        }
                        bmp.UnlockBits(bd);
                    }
                    else if (bpp == 32)
                    {
                        PixelFormat pf = isAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppRgb;
                        bmp = new Bitmap(w, h, pf);
                        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, pf);
                        unsafe
                        {
                            byte* dst = (byte*)bd.Scan0;
                            int stride = bd.Stride;
                            fixed (byte* src = pixels)
                            {
                                for (int y = 0; y < h; y++)
                                {
                                    byte* d = dst + y * stride;
                                    byte* s = src + y * w * 4;
                                    for (int x = 0; x < w; x++)
                                    {
                                        d[x * 4] = s[x * 4];       // B
                                        d[x * 4 + 1] = s[x * 4 + 1]; // G
                                        d[x * 4 + 2] = s[x * 4 + 2]; // R
                                        d[x * 4 + 3] = s[x * 4 + 3]; // A
                                    }
                                }
                            }
                        }
                        bmp.UnlockBits(bd);
                    }
                    else { fail++; continue; }

                    string name = $"mmap_{idx:D4}_{w}x{h}_off{pos:X6}_px{px}_{py}.png";
                    string path = Path.Combine(outDir, name);
                    bmp.Save(path, ImageFormat.Png);
                    bmp.Dispose();
                    success++;

                    if (idx % 500 == 0)
                        Console.WriteLine($"  [{idx}] OK {w}x{h} enc{enc1} len={len} -> {name}");
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"  [{idx}] FAIL: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"\nDone! success={success} fail={fail} skip={skip}");
        Console.WriteLine("Output: " + outDir);
    }
}
