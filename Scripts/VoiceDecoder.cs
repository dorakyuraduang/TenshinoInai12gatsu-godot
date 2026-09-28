using System;
using System.IO;
using Godot;

namespace Tenshi;

public static class VoiceDecoder
{
    private static readonly byte[] Vorbis = "vorbis"u8.ToArray();

    public static AudioStreamOggVorbis Decode(byte[] source)
    {
        using var output = new MemoryStream(source.Length + 32);
        int offset = 0;
        int headerPackets = 0;
        while (offset < source.Length)
        {
            if (offset + 27 > source.Length)
                throw new InvalidDataException("Voice Ogg page header is truncated");
            int segmentCount = source[offset + 26];
            int segmentTable = offset + 27;
            int payloadStart = segmentTable + segmentCount;
            if (segmentCount == 0 || payloadStart > source.Length)
                throw new InvalidDataException("Voice Ogg segment table is invalid");

            int payloadSize = 0;
            for (int i = 0; i < segmentCount; i++) payloadSize += source[segmentTable + i];
            if (payloadStart + payloadSize > source.Length)
                throw new InvalidDataException("Voice Ogg payload is truncated");

            byte[] lacing = new byte[segmentCount];
            Buffer.BlockCopy(source, segmentTable, lacing, 0, segmentCount);
            using var payload = new MemoryStream(payloadSize + 16);
            int cursor = payloadStart;
            bool packetStart = true;
            bool repairing = false;
            for (int i = 0; i < segmentCount; i++)
            {
                int length = source[segmentTable + i];
                if (packetStart && headerPackets < 3)
                {
                    byte expectedType = (byte)(headerPackets * 2 + 1);
                    if (length < 2 || source[cursor] != expectedType || source[cursor + 1] != 1)
                        throw new InvalidDataException("Voice Vorbis header marker is invalid");
                    payload.WriteByte(expectedType);
                    payload.Write(Vorbis);
                    payload.Write(source, cursor + 2, length - 2);
                    repairing = true;
                }
                else
                    payload.Write(source, cursor, length);
                cursor += length;
                packetStart = length < 255;
                if (packetStart && repairing)
                {
                    if (lacing[i] > 250)
                        throw new InvalidDataException("Voice Vorbis packet cannot fit its repaired lacing");
                    lacing[i] += 5;
                    headerPackets++;
                    repairing = false;
                }
            }

            byte[] page = new byte[27 + segmentCount + payload.Length];
            "OggS"u8.CopyTo(page);
            Buffer.BlockCopy(source, offset + 4, page, 4, 23);
            Buffer.BlockCopy(lacing, 0, page, 27, segmentCount);
            Buffer.BlockCopy(payload.GetBuffer(), 0, page, 27 + segmentCount, (int)payload.Length);
            Array.Clear(page, 22, 4);
            uint crc = 0;
            foreach (byte value in page)
            {
                crc ^= (uint)value << 24;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
            }
            BitConverter.GetBytes(crc).CopyTo(page, 22);
            output.Write(page);
            offset = payloadStart + payloadSize;
        }
        if (headerPackets != 3)
            throw new InvalidDataException("Voice is missing Vorbis headers");
        AudioStreamOggVorbis? stream = AudioStreamOggVorbis.LoadFromBuffer(output.ToArray());
        return stream ?? throw new InvalidDataException("Godot rejected the repaired voice stream");
    }
}
