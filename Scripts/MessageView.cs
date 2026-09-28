using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Godot;

namespace Tenshi;

// !SysDisp: 494x90 surface, 480 pixel wrap width, 24 pixel font, 4 pixel leading.
// Native 100188A0 advances ASCII by 12 pixels and double-byte characters by 24.
public partial class MessageView : Control
{
    public readonly record struct Glyph(string Text, Vector2 Position, float Advance, float Offset, int FontSize);
    private readonly List<List<Glyph>> _pages = new();
    // Android does not ship the Windows SimHei/黑体 family and cannot use the
    // original GDI rasterizer. Keep the same font in the PCK and use it for
    // the fallback path so Chinese glyphs do not depend on device fonts.
    private readonly SystemFont _fallback = new() { FontNames = ["SimHei", "黑体", "Noto Sans SC", "sans"] };
    private readonly FontFile? _bundledFallback = GD.Load<FontFile>("res://fonts/simhei.ttf");
    private NativeGlyphFont _font = new();
    private NativeGlyphFont _fullFont = new(26);
    private readonly Dictionary<(int Size, string Face), NativeGlyphFont> _styledFonts = new();
    private string _primaryFace = "黑体", _fullFace = "黑体";
    public bool FullPage { get; private set; }
    public int FontSize => FullPage ? 26 : 24;
    public int LineHeight => FullPage ? 42 : 28;
    public int WrapWidth => FullPage ? 640 : 480;
    private string _text = "";
    private float _revealed;
    public int Page { get; private set; }
    public int PageCount => _pages.Count;
    public IReadOnlyList<List<Glyph>> Pages => _pages;
    public bool Complete => _pages.Count == 0 || _revealed >= PageLength;
    public bool LastPage => Page + 1 >= _pages.Count;
    public float PageLength => _pages.Count == 0 || _pages[Page].Count == 0 ? 0 :
        _pages[Page][^1].Offset + _pages[Page][^1].Advance;
    public float Revealed => _revealed;
    public bool Smooth { get; set; } = true;
    public bool FullSmooth { get; set; } = true;

    public void SetFonts(string primary, string full)
    {
        if (_primaryFace != primary || _fullFace != full) ClearStyledFonts();
        if (_primaryFace != primary) { _font.Dispose(); _font = new NativeGlyphFont(24, primary); _primaryFace = primary; }
        if (_fullFace != full) { _fullFont.Dispose(); _fullFont = new NativeGlyphFont(26, full); _fullFace = full; }
        _fallback.FontNames = [FullPage ? full : primary, "SimHei", "黑体"];
        QueueRedraw();
    }
    public string Text { get => _text; set => SetMessage(value); }
    public Vector2 EndPosition => _pages.Count == 0 || _pages[Page].Count == 0 ? Vector2.Zero :
        _pages[Page][^1].Position + new Vector2(_pages[Page][^1].Advance, -_pages[Page][^1].FontSize);

    public MessageView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        Size = new Vector2(494, 90);
    }

    public void ConfigureLayout(bool fullPage)
    {
        FullPage = fullPage;
        _fallback.FontNames = [fullPage ? _fullFace : _primaryFace, "SimHei", "黑体"];
        Size = fullPage ? new Vector2(654, 520) : new Vector2(494, 90);
        SetMessage(_text);
    }

    public void SetMessage(string text)
    {
        _text = text;
        _pages.Clear();
        _pages.Add(new List<Glyph>());
        Page = 0;
        _revealed = 0;
        float x = FullPage ? 0 : 2, offset = 0, height = 0;
        int row = 0, size = FontSize, leading = LineHeight - FontSize;
        var styles = new Stack<int>();
        var line = new List<Glyph>();
        int compression = 0;
        void NewLine()
        {
            int maximumSize = line.Count == 0 ? size : 0;
            foreach (Glyph glyph in line) maximumSize = Math.Max(maximumSize, glyph.FontSize);
            float lineHeight = maximumSize + leading;
            if (row >= (FullPage ? 12 : 3) || height + lineHeight > Size.Y)
            {
                row = 0;
                height = offset = 0;
                _pages.Add(new List<Glyph>());
            }
            height += lineHeight;
            int shift = compression;
            float startX = FullPage ? 0 : 2;
            foreach (Glyph glyph in line)
            {
                // Native kinsoku spacing can make the first glyph move left by
                // one pixel at a page boundary. Keep that correction inside
                // the message surface so the Godot clip rectangle does not
                // cut the glyph or trip the route-layout invariant.
                float glyphX = Math.Max(startX, glyph.Position.X + shift);
                _pages[^1].Add(glyph with { Position = new Vector2(glyphX, height), Offset = offset });
                offset += glyph.Advance;
                shift += compression;
            }
            line.Clear();
            compression = 0;
            row++;
            x = FullPage ? 0 : 2;
        }
        for (int index = 0; index < text.Length;)
        {
            // Native 100192E0 parses $p<number>; 100189D0 applies it to
            // glyph advances and the maximum font height on the current line.
            if (text[index] == '$' && index + 1 < text.Length && text[index + 1] == 'p')
            {
                int start = index + 2, end = start;
                if (end < text.Length && text[end] == '-') end++;
                while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
                if (int.TryParse(text.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested))
                {
                    // Keep malformed external scripts within the message surface.
                    size = Math.Clamp(requested, 1, (int)Size.Y - leading);
                    index = end;
                    continue;
                }
            }
            if (text[index] == '<') { styles.Push(size); index++; continue; }
            if (text[index] == '>') { if (styles.Count > 0) size = styles.Pop(); index++; continue; }
            string value = StringInfo.GetNextTextElement(text, index);
            index += value.Length;
            if (value == "\r") continue;
            if (value is "\n" or "\u007f") { NewLine(); continue; }
            float advance = value[0] < 256 ? size >> 1 : size;
            bool closing = "ぁぃぅぇぉっゃゅょんをァィゥェォッャュョンヲ、。，．・：；？！゛゜ーヽヾ々’”）〕］｝〉》」』】".Contains(value);
            if (x + advance >= WrapWidth)
            {
                if (line.Count > 0 && "‘“（〔［｛〈《「『【".Contains(line[^1].Text))
                {
                    Glyph opening = line[^1];
                    line.RemoveAt(line.Count - 1);
                    compression = 1;
                    NewLine();
                    line.Add(opening with { Position = new Vector2(x, 0) });
                    x += opening.Advance;
                }
                else if (closing && compression >= -1) compression--;
                else NewLine();
            }
            line.Add(new Glyph(value, new Vector2(x, 0), advance, 0, size));
            x += advance;
        }
        if (line.Count > 0) NewLine();
        if (_pages.Count > 1 && _pages[^1].Count == 0) _pages.RemoveAt(_pages.Count - 1);
        QueueRedraw();
    }

    public void Tick(double delta, int speed, bool instant)
    {
        if (Complete) return;
        // !SysMsg increases the reveal distance by 2*speed+1 every 60 Hz tick.
        _revealed = instant ? PageLength : Math.Min(PageLength, _revealed + (float)delta * 60 * (2 * speed + 1));
        QueueRedraw();
    }

    public void RevealAll() { _revealed = PageLength; QueueRedraw(); }

    public void AppendMessage(string text)
    {
        int page = Page;
        float distance = _revealed;
        SetMessage(_text + text);
        Page = Math.Min(page, _pages.Count - 1);
        _revealed = Math.Min(distance, PageLength);
        QueueRedraw();
    }

    public bool NextPage()
    {
        if (!Complete || LastPage) return false;
        Page++;
        _revealed = 0;
        QueueRedraw();
        return true;
    }

    public void RestorePage(int page)
    {
        Page = Math.Clamp(page, 0, _pages.Count - 1);
        _revealed = 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_pages.Count == 0) return;
        foreach (Glyph glyph in _pages[Page])
        {
            float alpha = Complete ? 1 : Mathf.Clamp((_revealed - glyph.Offset) / glyph.Advance, 0, 1);
            if (alpha <= 0) break;
            // Blank glyphs still occupy their native advance and reveal time.
            if (string.IsNullOrWhiteSpace(glyph.Text)) continue;
            Color color = new(1, 1, 1, alpha);
            if (GetFont(glyph.FontSize).Get(glyph.Text, FullPage ? FullSmooth : Smooth) is { } native)
            {
                Vector2 position = glyph.Position + native.Origin;
                DrawTexture(native.Texture, position + Vector2.One, new Color(0, 0, 0, alpha * 0.7f));
                DrawTexture(native.Texture, position, color);
            }
            else
                DrawString((Font?)_bundledFallback ?? _fallback, glyph.Position, glyph.Text,
                    fontSize: glyph.FontSize, modulate: color);
        }
    }

    private NativeGlyphFont GetFont(int size)
    {
        if (size == FontSize) return FullPage ? _fullFont : _font;
        var key = (size, FullPage ? _fullFace : _primaryFace);
        if (!_styledFonts.TryGetValue(key, out NativeGlyphFont? font))
            _styledFonts.Add(key, font = new NativeGlyphFont(size, key.Item2));
        return font;
    }

    private void ClearStyledFonts()
    {
        foreach (NativeGlyphFont font in _styledFonts.Values) font.Dispose();
        _styledFonts.Clear();
    }

    public override void _ExitTree() { ClearStyledFonts(); _font.Dispose(); _fullFont.Dispose(); _fallback.Dispose(); }
}

// The Windows original uses GDI's GB2312 SimHei glyph rasterizer. Reuse its
// outlines in memory so Godot fallback-font ascent/leading cannot change layout.
internal sealed class NativeGlyphFont : IDisposable
{
    internal sealed record GlyphImage(ImageTexture Texture, Vector2 Origin);
    private readonly Dictionary<string, GlyphImage?> _cache = new();
    private readonly IntPtr _dc, _font, _previous;
    [StructLayout(LayoutKind.Sequential)] private struct Metrics
    {
        public uint Width, Height;
        public int X, Y;
        public short AdvanceX, AdvanceY;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Matrix
    {
        public ushort Fraction11; public short Value11;
        public ushort Fraction12; public short Value12;
        public ushort Fraction21; public short Value21;
        public ushort Fraction22; public short Value22;
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetGlyphOutlineW(
        IntPtr dc, uint character, uint format, out Metrics metrics, uint size, byte[]? buffer, ref Matrix matrix);

    public NativeGlyphFont(int size = 24, string face = "黑体")
    {
        if (!OperatingSystem.IsWindows()) return;
        _dc = CreateCompatibleDC(IntPtr.Zero);
        _font = CreateFontW(-size, 0, 0, 0, 0, 0, 0, 0, 134, 5, 0, 2, 0, face);
        if (_dc != IntPtr.Zero && _font != IntPtr.Zero) _previous = SelectObject(_dc, _font);
    }

    public GlyphImage? Get(string text, bool smooth = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (_dc == IntPtr.Zero || _font == IntPtr.Zero || text.Length != 1) return null;
        string key = text + (smooth ? "s" : "b");
        if (_cache.TryGetValue(key, out GlyphImage? cached)) return cached;
        var matrix = new Matrix { Value11 = 1, Value22 = 1 };
        uint length = GetGlyphOutlineW(_dc, text[0], 6, out Metrics metric, 0, null, ref matrix);
        // GDI can return length 0 for U+3000 while GLYPHMETRICS still has a box.
        if (length == 0 || length == uint.MaxValue || metric.Width == 0 || metric.Height == 0)
        { _cache[key] = null; return null; }
        byte[] buffer = new byte[length];
        uint written = GetGlyphOutlineW(_dc, text[0], 6, out metric, length, buffer, ref matrix);
        long rowBytes = ((long)metric.Width + 3) & ~3L;
        long required = rowBytes * metric.Height;
        if (written == 0 || written == uint.MaxValue || required == 0 ||
            required > written || written > buffer.Length || required > int.MaxValue / 4)
        { _cache[key] = null; return null; }
        int width = (int)metric.Width, height = (int)metric.Height, stride = (int)rowBytes;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int target = (y * width + x) * 4;
            rgba[target] = rgba[target + 1] = rgba[target + 2] = 255;
            int intensity = buffer[y * stride + x];
            rgba[target + 3] = smooth ? (byte)Math.Min(255, intensity * 255 / 64) : intensity >= 32 ? (byte)255 : (byte)0;
        }
        using Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        var glyph = new GlyphImage(ImageTexture.CreateFromImage(image), new Vector2(metric.X, -metric.Y));
        _cache[key] = glyph;
        return glyph;
    }

    public void Dispose()
    {
        foreach (GlyphImage? glyph in _cache.Values) glyph?.Texture.Dispose();
        _cache.Clear();
        if (_dc != IntPtr.Zero) { SelectObject(_dc, _previous); DeleteDC(_dc); }
        if (_font != IntPtr.Zero) DeleteObject(_font);
    }
}
