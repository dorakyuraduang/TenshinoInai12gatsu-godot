using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tenshi;

public sealed class Archive
{
    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(long Offset, int Size, byte Kind);

    public Archive(string path)
    {
        _path = path;
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt16() != 0xAF1E)
            throw new InvalidDataException($"Invalid archive header: {path}");
        int count = reader.ReadUInt16();
        long dataStart = 4L + count * 32L;
        if (dataStart > stream.Length)
            throw new InvalidDataException($"Truncated archive index: {path}");

        for (int i = 0; i < count; i++)
        {
            byte[] row = reader.ReadBytes(32);
            int nameLength = Array.IndexOf(row, (byte)0, 0, 23);
            if (nameLength < 0) nameLength = 23;
            string name = Encoding.ASCII.GetString(row, 0, nameLength);
            uint size = BitConverter.ToUInt32(row, 24);
            uint offset = BitConverter.ToUInt32(row, 28);
            long absolute = dataStart + offset;
            if (size > int.MaxValue || absolute + size > stream.Length)
                throw new InvalidDataException($"Invalid entry {name} in {path}");
            var entry = new Entry(absolute, (int)size, row[23]);
            if (!_entries.TryGetValue(name, out Entry existing) ||
                (existing.Kind < 0x80 && entry.Kind >= 0x80))
                _entries[name] = entry;
        }
    }

    public IReadOnlyCollection<string> Names => _entries.Keys;

    public bool Contains(string name) => _entries.ContainsKey(name);

    public (byte[] Data, byte Kind) Read(string name)
    {
        if (!_entries.TryGetValue(name, out Entry entry))
            throw new FileNotFoundException($"Resource {name} not found in {_path}");
        byte[] bytes = new byte[entry.Size];
        using var stream = File.OpenRead(_path);
        stream.Position = entry.Offset;
        stream.ReadExactly(bytes);
        return (bytes, entry.Kind);
    }
}
