using System;
using System.IO;
using Godot;

namespace Tenshi;

public static class AudioDecoder
{
    public static AudioStreamWav Decode(byte[] data)
    {
        if (data.Length < 18)
            throw new InvalidDataException("W audio header is truncated");
        int channels = data[0];
        int blockAlign = data[1];
        int sampleRate = BitConverter.ToUInt16(data, 2);
        int bits = BitConverter.ToUInt16(data, 4);
        uint byteRate = BitConverter.ToUInt32(data, 6);
        uint length = BitConverter.ToUInt32(data, 10);
        if ((channels != 1 && channels != 2) || (bits != 8 && bits != 16) ||
            blockAlign != channels * bits / 8 || byteRate != sampleRate * blockAlign ||
            length > data.Length - 18)
            throw new InvalidDataException("Unsupported W audio format");

        byte[] pcm = new byte[length];
        Buffer.BlockCopy(data, 18, pcm, 0, (int)length);
        return new AudioStreamWav
        {
            MixRate = sampleRate,
            Stereo = channels == 2,
            Format = bits == 16 ? AudioStreamWav.FormatEnum.Format16Bits : AudioStreamWav.FormatEnum.Format8Bits,
            Data = pcm
        };
    }
}
