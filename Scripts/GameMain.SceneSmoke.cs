using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private void SmokeScene()
    {
        int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); GD.Print($"Scene regression: {name} passed"); }
            catch (Exception error) { failures++; GD.PushError($"{name}: {error}"); }
        }
        Check("common-route glyph rasterization", () =>
        {
            char[] glyphs = _baseEvents.Where(e => e.Kind == SceneEventKind.Dialogue)
                .SelectMany(e => FormatMessageText(e.Text)).Distinct().ToArray();
            foreach (int size in new[] { 24, 26 })
            {
                using var font = new NativeGlyphFont(size);
                foreach (char glyph in glyphs)
                {
                    if (char.IsControl(glyph)) continue;
                    try { font.Get(glyph.ToString()); }
                    catch (Exception error) { throw new InvalidDataException($"Glyph U+{(int)glyph:X4}, size {size}", error); }
                }
            }
            GD.Print($"Rasterized {glyphs.Length} distinct script characters at both native sizes");
        });
        Check("all motion frames", () =>
        {
            foreach (int effect in new[] { 4959, 5251, 6036, 6200 })
                for (int frame = 0; frame <= 92; frame++) OriginalEffectMath.Sample(effect, frame);
        });
        Check("first standing-character to CG transition", () =>
        {
            ClearCharacters();
            int backgrounds = 0;
            foreach (SceneEvent item in _baseEvents.Skip(_prologueEventCount))
            {
                if (item.Kind is SceneEventKind.Background or SceneEventKind.ClearCharacters or
                    SceneEventKind.Character or SceneEventKind.HideCharacter) ApplySceneEvent(item);
                if (item.Kind != SceneEventKind.Background) continue;
                backgrounds++;
                if (item.Value != 0x1020) continue;
                if (_characterCodes.Any(code => code != 0) || _characters.Any(node => node.Texture != null))
                    throw new InvalidDataException("com_0110.p:2796 left the preceding portrait over CG 0x1020");
                GD.Print($"CG 0x1020 reached after {backgrounds} background changes with no portrait layers");
                return;
            }
            throw new InvalidDataException("Target CG was not reached");
        });
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
