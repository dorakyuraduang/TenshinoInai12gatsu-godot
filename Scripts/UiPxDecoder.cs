using System;
using System.IO;
using Godot;

namespace Tenshi;

public readonly record struct UiPxFrame(Image Image, Vector2I Origin);

public static class UiPxDecoder
{
    // Composite blocks and indexed runs follow GARbro's MIT-licensed ImagePX.cs.
    // https://github.com/morkt/GARbro/blob/master/ArcFormats/Leaf/ImagePX.cs
    public static int FrameCount(byte[] packed)
    {
        byte[] data = PxDecoder.DecodeLzss(packed);
        return ReadFrameCount(data);
    }

    public static Image DecodeFrame(byte[] packed, int frame) => DecodeFrames(packed, frame)[0].Image;
    public static Image DecodeStandalone(byte[] packed)
    {
        byte[] data = PxDecoder.DecodeLzss(packed);
        return DecodeImage(data, 0, data.Length);
    }

    public static UiPxFrame DecodeCharacter(byte[] packed)
    {
        byte[] data = PxDecoder.DecodeLzss(packed);
        if (data.Length < 32 || BitConverter.ToUInt16(data, 16) is not (0x40 or 0x44))
            throw new InvalidDataException($"Invalid character PX container: {Convert.ToHexString(data.AsSpan(0, Math.Min(32, data.Length)))}");
        return new UiPxFrame(DecodeImage(data, 0, data.Length),
            new Vector2I(BitConverter.ToInt32(data, 8), BitConverter.ToInt32(data, 12)));
    }

    public static UiPxFrame[] DecodeFrames(byte[] packed, params int[] frames)
    {
        byte[] data = PxDecoder.DecodeLzss(packed);
        int count = ReadFrameCount(data);
        int tableEnd = checked(36 + (count - 1) * 4);
        var images = new UiPxFrame[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            int frame = frames[i];
            if (frame < 0 || frame >= count)
                throw new ArgumentOutOfRangeException(nameof(frames));
            int start = checked(tableEnd + (frame == 0 ? 0 : ReadOffset(data, frame - 1)));
            int end = frame == count - 1 ? data.Length : checked(tableEnd + ReadOffset(data, frame));
            if (start < tableEnd || end > data.Length || end - start < 32)
                throw new InvalidDataException($"UI PX frame {frame} offset is invalid");
            images[i] = new UiPxFrame(DecodeImage(data, start, end),
                new Vector2I(BitConverter.ToInt32(data, start + 8), BitConverter.ToInt32(data, start + 12)));
        }
        return images;
    }

    private static Image DecodeImage(byte[] data, int start, int end)
    {
        int width = checked((int)BitConverter.ToUInt32(data, start));
        int height = checked((int)BitConverter.ToUInt32(data, start + 4));
        uint format = BitConverter.ToUInt32(data, start + 16);
        if ((format & 0xffff) is 0x40 or 0x44)
            return DecodeComposite(data, start, end);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("Unsupported UI PX dimensions");

        int countPixels = checked(width * height);
        byte[] rgba = new byte[checked(countPixels * 4)];
        if (format is 0x00200004 or 0x00300004)
        {
            DecodeColorBlock(data, start + 32, end, rgba, width, height, 0, 0, width, height, format == 0x00300004);
        }
        else if (format == 0x00080007 && end - start == 32L + 1024 + countPixels)
        {
            int palette = start + 32;
            int indices = palette + 1024;
            for (int pixel = 0; pixel < countPixels; pixel++)
            {
                int color = palette + data[indices + pixel] * 4;
                int dst = pixel * 4;
                rgba[dst] = data[color + 2];
                rgba[dst + 1] = data[color + 1];
                rgba[dst + 2] = data[color];
                rgba[dst + 3] = DecodeAlpha(data[color + 3], data[color + 2]);
            }
        }
        else if (format == 0x00200001 && end - start == 32L + countPixels * 4L)
        {
            for (int pixel = 0; pixel < countPixels; pixel++)
            {
                int src = start + 32 + pixel * 4;
                int dst = pixel * 4;
                rgba[dst] = data[src + 2];
                rgba[dst + 1] = data[src + 1];
                rgba[dst + 2] = data[src];
                rgba[dst + 3] = DecodeAlpha(data[src + 3], data[src + 2]);
            }
        }
        else
            throw new NotSupportedException($"UI PX uses unsupported layout 0x{format:X8}");

        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    private static Image DecodeComposite(byte[] data, int start, int end)
    {
        int count = checked((int)BitConverter.ToUInt32(data, start));
        int width = checked((int)BitConverter.ToUInt32(data, start + 20));
        int height = checked((int)BitConverter.ToUInt32(data, start + 24));
        if (count <= 0 || count > 1024 || width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("Invalid composite UI PX header");
        int baseOffset = checked(start + 32 + count * 4);
        if (baseOffset > end)
            throw new InvalidDataException("Composite UI PX block table is truncated");

        byte[] rgba = new byte[checked(width * height * 4)];
        byte[] palette = Array.Empty<byte>();
        for (int i = 0; i < count; i++)
        {
            int block = checked(baseOffset + (int)BitConverter.ToUInt32(data, start + 32 + i * 4));
            int blockEnd = i + 1 == count ? end :
                checked(baseOffset + (int)BitConverter.ToUInt32(data, start + 36 + i * 4));
            if (block < baseOffset || blockEnd > end || blockEnd - block < 32)
                throw new InvalidDataException("Composite UI PX block offset is invalid");

            int blockWidth = BitConverter.ToInt32(data, block);
            int blockHeight = BitConverter.ToInt32(data, block + 4);
            int x = BitConverter.ToInt32(data, block + 8);
            int y = BitConverter.ToInt32(data, block + 12);
            int type = BitConverter.ToUInt16(data, block + 16);
            int bits = BitConverter.ToUInt16(data, block + 18);
            if (type == 0 && bits == 32)
            {
                if (blockWidth <= 0 || blockWidth > 256 || block + 32L + blockWidth * 4 > blockEnd)
                    throw new InvalidDataException("Composite UI PX palette is invalid");
                palette = new byte[blockWidth * 4];
                Buffer.BlockCopy(data, block + 32, palette, 0, palette.Length);
            }
            else if (type == 4 && bits == 9)
            {
                if (palette.Length == 0 || blockWidth <= 0 || blockHeight <= 0 ||
                    blockWidth > 1024 || blockHeight > 8192)
                    throw new InvalidDataException("Composite UI PX indexed block is invalid");
                DecodeIndexedBlock(data, block + 32, blockEnd, palette, rgba,
                    width, height, x, y, blockWidth, blockHeight);
            }
            else if (type == 4 && bits is 32 or 48)
            {
                if (blockWidth <= 0 || blockHeight <= 0 || blockWidth > 1024 || blockHeight > 8192)
                    throw new InvalidDataException("Composite UI PX color block is invalid");
                DecodeColorBlock(data, block + 32, blockEnd, rgba,
                    width, height, x, y, blockWidth, blockHeight, bits == 48);
            }
            else
                throw new NotSupportedException($"Composite UI PX block {type}/{bits} is unsupported");
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    private static void DecodeIndexedBlock(byte[] data, int offset, int end, byte[] palette,
        byte[] image, int imageWidth, int imageHeight, int x, int y, int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        long position = 0;
        bool hasAlpha = true;
        while (true)
        {
            if (offset + 4 > end)
                throw new InvalidDataException("Composite UI PX pixel stream ends early");
            int code = BitConverter.ToInt32(data, offset);
            offset += 4;
            if (code == -1) break;
            if ((code & 0x180000) != 0) hasAlpha = !hasAlpha;
            position += (code & 0x1FF) * 1024L + (code >> 21);
            int count = (code >> 9) & 0x3FF;
            if (offset + count * (hasAlpha ? 2 : 1) > end)
                throw new InvalidDataException("Composite UI PX pixel run ends early");
            for (int i = 0; i < count; i++, position++)
            {
                byte alpha = hasAlpha ? unchecked((byte)((data[offset++] << 1) - 1)) : (byte)255;
                int color = data[offset++] * 4;
                if (color + 3 >= palette.Length)
                    throw new InvalidDataException("Composite UI PX palette index is invalid");
                if (position < 0 || position >= height * 1024L) continue;
                int row = (int)(position / 1024);
                int column = (int)(position % 1024);
                if (row >= height || column >= width) continue;
                int target = (row * width + column) * 4;
                pixels[target] = palette[color + 2];
                pixels[target + 1] = palette[color + 1];
                pixels[target + 2] = palette[color];
                pixels[target + 3] = alpha;
            }
        }
        for (int row = 0; row < height; row++)
        {
            int targetY = y + row;
            if (targetY < 0 || targetY >= imageHeight) continue;
            for (int column = 0; column < width; column++)
            {
                int targetX = x + column;
                if (targetX < 0 || targetX >= imageWidth) continue;
                Buffer.BlockCopy(pixels, (row * width + column) * 4,
                    image, (targetY * imageWidth + targetX) * 4, 4);
            }
        }
    }

    private static void DecodeColorBlock(byte[] data, int offset, int end,
        byte[] image, int imageWidth, int imageHeight, int x, int y, int width, int height, bool extended)
    {
        byte[] pixels = new byte[checked(1024 * height * 4)];
        int position = 0;
        while (true)
        {
            if (offset + 4 > end)
                throw new InvalidDataException("Composite PX color stream ends early");
            int next = BitConverter.ToInt32(data, offset);
            offset += 4;
            if (next == -1) break;
            if (next < 0 || next > 0xffffff) continue;
            position = checked(position + next / 4);
            if (offset + 8 > end)
                throw new InvalidDataException("Composite PX color run is truncated");
            offset += 4;
            int count = BitConverter.ToInt32(data, offset);
            offset += 4;
            int pixelBytes = extended ? 6 : 4;
            if (count < 0 || (long)offset + count * pixelBytes > end)
                throw new InvalidDataException("Composite PX color run exceeds its block");
            for (int i = 0; i < count; i++, position++)
            {
                uint color = BitConverter.ToUInt32(data, offset);
                offset += pixelBytes;
                if (position < 0 || position >= 1024 * height) continue;
                int target = position * 4;
                pixels[target] = (byte)(color >> 16);
                pixels[target + 1] = (byte)(color >> 8);
                pixels[target + 2] = (byte)color;
                byte alpha = (byte)(color >> 24);
                pixels[target + 3] = alpha == 0 ? (extended ? (byte)0 : (byte)255) :
                    unchecked((byte)((color >> 23) + 255));
            }
        }
        for (int row = 0; row < height; row++)
        {
            int targetY = y + row;
            if (targetY < 0 || targetY >= imageHeight) continue;
            int left = Math.Max(0, x);
            int right = Math.Min(imageWidth, x + width);
            if (right <= left) continue;
            Buffer.BlockCopy(pixels, (row * 1024 + left - x) * 4,
                image, (targetY * imageWidth + left) * 4, (right - left) * 4);
        }
    }

    private static byte DecodeAlpha(byte alpha, byte red) =>
        alpha == 0 ? (byte)255 : unchecked((byte)(((alpha << 1) | (red >> 7)) + 255));

    private static int ReadFrameCount(byte[] data)
    {
        if (data.Length < 40 || BitConverter.ToUInt32(data, 16) != 0x80 ||
            !data.AsSpan(20, 12).SequenceEqual("LeafAquaPlus"u8))
            throw new InvalidDataException("Invalid UI PX container");
        int count = checked((int)BitConverter.ToUInt32(data, 0));
        if (count <= 0 || count > 1024 || 36L + (count - 1L) * 4 > data.Length)
            throw new InvalidDataException("Invalid UI PX frame table");
        return count;
    }

    private static int ReadOffset(byte[] data, int index) =>
        checked((int)BitConverter.ToUInt32(data, 36 + index * 4));
}
