using System;
using System.IO;
using Godot;

namespace Tenshi;

public static class PxDecoder
{
    private const int MaxDecodedBytes = 128 * 1024 * 1024;

    public static Image DecodeBackground(byte[] packed, byte kind)
    {
        byte[] data = DecodeLzss(packed);
        if (data.Length < 32)
            throw new InvalidDataException("PX image header is truncated");
        int width = checked((int)BitConverter.ToUInt32(data, 0));
        int height = checked((int)BitConverter.ToUInt32(data, 4));
        uint tag = BitConverter.ToUInt32(data, 16);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException($"Unsupported PX image layout: {width}x{height}, tag 0x{BitConverter.ToUInt32(data, 16):X8}, decoded {data.Length} bytes");
        if (tag == 0x00080001 && data.Length == 32L + (long)width * height)
        {
            byte[] mask = new byte[width * height];
            Buffer.BlockCopy(data, 32, mask, 0, mask.Length);
            return Image.CreateFromData(width, height, false, Image.Format.L8, mask);
        }
        if (tag != 0x00200001 || data.Length != 32L + (long)width * height * 4)
            throw new InvalidDataException($"Unsupported PX image layout: {width}x{height}, tag 0x{tag:X8}, decoded {data.Length} bytes");

        byte[] rgba = new byte[checked(width * height * 4)];
        if (kind < 0x80 || kind > 0x89)
            throw new InvalidDataException($"Unsupported PX predictor kind {kind}");

        int previousB = 0, previousG = 0, previousR = 0;
        int baseline = kind & 0x0F;
        for (int src = 32, dst = 0; dst < rgba.Length; src += 4, dst += 4)
        {
            int b = data[src], g = data[src + 1], r = data[src + 2];
            int predictor = data[src + 3];
            int adjustment = predictor - baseline;
            b = (b + previousB + adjustment) & 255;
            g = (g + previousG + adjustment) & 255;
            r = (r + previousR + adjustment) & 255;

            rgba[dst] = (byte)r;
            rgba[dst + 1] = (byte)g;
            rgba[dst + 2] = (byte)b;
            rgba[dst + 3] = 255;
            previousB = b;
            previousG = g;
            previousR = r;
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    public static byte[] DecodeLzss(byte[] packed)
    {
        if (packed.Length < 5)
            throw new InvalidDataException("Compressed PX header is truncated");
        uint expected = BitConverter.ToUInt32(packed, 0);
        if (expected == 0 || expected > MaxDecodedBytes)
            throw new InvalidDataException($"Invalid PX decoded size: {expected}");

        byte[] output = new byte[expected];
        byte[] window = new byte[4096];
        int input = 4, written = 0, windowPos = 0xFEE;
        while (written < output.Length)
        {
            if (input >= packed.Length)
                throw new InvalidDataException("PX LZSS stream ends early");
            int flags = packed[input++];
            for (int bit = 0; bit < 8 && written < output.Length; bit++)
            {
                if ((flags & (1 << bit)) != 0)
                {
                    if (input >= packed.Length)
                        throw new InvalidDataException("PX literal ends early");
                    byte value = packed[input++];
                    output[written++] = value;
                    window[windowPos] = value;
                    windowPos = (windowPos + 1) & 0xFFF;
                }
                else
                {
                    if (input + 1 >= packed.Length)
                        throw new InvalidDataException("PX back-reference ends early");
                    int lo = packed[input++], hi = packed[input++];
                    int source = lo | ((hi & 0xF0) << 4);
                    int length = (hi & 0x0F) + 3;
                    for (int i = 0; i < length && written < output.Length; i++)
                    {
                        byte value = window[(source + i) & 0xFFF];
                        output[written++] = value;
                        window[windowPos] = value;
                        windowPos = (windowPos + 1) & 0xFFF;
                    }
                }
            }
        }
        return output;
    }
}
