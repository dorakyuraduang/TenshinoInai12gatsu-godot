using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private sealed class SaveRecord
    {
        public int Version { get; set; } = 3;
        public int DialogueOrdinal { get; set; }
        public string Source { get; set; } = "";
        public int MessageId { get; set; }
        public int Page { get; set; }
        public string Surname { get; set; } = "木田";
        public string GivenName { get; set; } = "时纪";
        public DateTime SavedAt { get; set; }
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";
        public HistoryEntry[] History { get; set; } = [];
        public int[] Decisions { get; set; } = [];
    }

    private static string SlotPath(int slot) => DataPath($"save_{slot:00}.json");

    private SaveRecord? ReadSlot(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) return null;
        try
        {
            SaveRecord? record = JsonSerializer.Deserialize<SaveRecord>(File.ReadAllText(path));
            return record?.Version == 3 ? record : null;
        }
        catch (JsonException error) { GD.PushWarning($"Save {slot + 1} is invalid: {error.Message}"); return null; }
    }

    private void WriteSlot(int slot)
    {
        if (_activeMessage == null || _activeMessageIndex < 0) return;
        var record = new SaveRecord
        {
            DialogueOrdinal = _events.Take(_activeMessageIndex + 1).Count(e => e.Kind == SceneEventKind.Dialogue) - 1,
            Source = _activeMessage.Source, MessageId = _activeMessage.Value, Page = _dialogue.Page,
            Surname = _surname, GivenName = _givenName, SavedAt = DateTime.Now,
            Speaker = _speaker.Text, Text = _dialogue.Text, History = _history.ToArray(),
            Decisions = _runtime.Decisions.ToArray()
        };
        string path = SlotPath(slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(record));
        File.Move(path + ".tmp", path, true);
        SaveReadMessages();
        _continue.Disabled = false;
    }

    private void LoadSlot(int slot)
    {
        SaveRecord? record = ReadSlot(slot);
        if (record == null)
        {
            if (slot == 0 && File.Exists(SavePath)) { CloseSystemUi(); Load(); }
            return;
        }
        int position = FindSavedRouteMessage(record);
        if (position < 0) { GD.PushWarning("Save scene is not supported by the current script runtime"); return; }
        _surname = record.Surname;
        _givenName = record.GivenName;
        RestoreScene(position);
        _dialogue.RestorePage(record.Page);
        _history.Clear();
        _history.AddRange(record.History.TakeLast(100));
        if (position >= _prologueEventCount) _firstLaunch = false;
        _menuMusic.Stop();
        _menu.Visible = _nameScreen.Visible = false;
        _game.Visible = true;
        CloseSystemUi();
    }

    private void OpenSaveSlots(bool loading)
    {
        if (_saveFrames.Length == 0 || (!loading && !_game.Visible)) return;
        EmptySystemUi();
        _slotsLoading = loading;
        SystemBackdrop();
        UiImage(_systemLayer, _saveFrames[0], 11, 69);
        UiImage(_systemLayer, _saveFrames[loading ? 3 : 2], 20, 94);
        for (int i = 0; i < 8; i++)
        {
            int slot = _savePage * 8 + i;
            int x = 64 + i / 4 * 370, y = 81 + i % 4 * 114;
            SaveRecord? record = ReadSlot(slot);
            bool legacy = slot == 0 && record == null && File.Exists(SavePath);
            TextureButton cell = UiButton(_saveFrames[1], x, y, $"{slot + 1:00}", () =>
            {
                if (_slotsLoading) { LoadSlot(slot); return; }
                if (ReadSlot(slot) != null || (slot == 0 && File.Exists(SavePath)))
                    ConfirmSystem($"覆盖第 {slot + 1:00} 个存档？", () => { WriteSlot(slot); OpenSaveSlots(false); });
                else { WriteSlot(slot); OpenSaveSlots(false); }
            });
            cell.Disabled = loading && record == null && !legacy;
            UiLabel(cell, $"{slot + 1:00}", 6, 5, 60, 25, 16);
            if (record == null)
            {
                UiLabel(cell, legacy ? "旧存档" : "NO DATA", 16, 40, 324, 32, 18);
                continue;
            }
            AddSavePreview(cell, record);
            UiLabel(cell, record.SavedAt.ToString("yyyy/MM/dd HH:mm:ss"), 16, 32, 210, 24, 18);
            UiLabel(cell, record.Speaker.Length == 0 ? "" : $"〔{record.Speaker}〕", 4, 54, 220, 24, 16);
            UiLabel(cell, record.Text.Replace('\u007f', ' '), 4, 80, 220, 24, 16);
        }
        for (int i = 0; i < 5; i++)
        {
            int page = i;
            UiButton(_saveFrames[4 + i * 2 + (i == _savePage ? 1 : 0)], 22, 320 + i * 40,
                $"第 {i + 1} 页", () => { _savePage = page; OpenSaveSlots(loading); });
        }
    }

    private void AddSavePreview(Control cell, SaveRecord record)
    {
        HistoryEntry? entry = record.History.LastOrDefault();
        if (entry == null || entry.Background < 0) return;
        // _sysMenu:13079 draws a 114x85 thumbnail at slot +(228,9). Persist only
        // scene identifiers in the save and reconstruct in memory, never export art.
        var preview = new Control { Position = new Vector2(228, 9), Size = new Vector2(800, 600),
            Scale = new Vector2(114f / 800, 85f / 600), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        cell.AddChild(preview);
        var image = _backgroundArchive!.Read(ImageName(entry.Background));
        using (Image background = PxDecoder.DecodeBackground(image.Data, image.Kind))
            UiImage(preview, ImageTexture.CreateFromImage(background), 0, 0);
        int[] centers = entry.CharacterCount switch { 1 => [399], 2 => [209, 589], _ => [150, 400, 650] };
        if (entry.Characters != null)
            for (int i = 0; i < Math.Min(3, entry.Characters.Length); i++)
            {
                int code = entry.Characters[i];
                if (code == 0) continue;
                if (i >= centers.Length) continue;
                string name = $"{_characterStrings[((code >> 8) & 15) * 2]}{(code & 15) * 100 + ((code >> 4) & 15):000}.px";
                UiPxFrame frame = UiPxDecoder.DecodeCharacter(_characterArchive!.Read(name).Data);
                using (frame.Image) UiImage(preview, ImageTexture.CreateFromImage(frame.Image), centers[i] + frame.Origin.X, 599 + frame.Origin.Y);
            }
        if (entry.FullPage) UiImage(preview, _commonFrames[106], 0, 0);
        else
        {
            UiImage(preview, _commonFrames[0], 0, 497);
            if (entry.Speaker.Length > 0)
            {
                UiImage(preview, _commonFrames[8], 0, 462);
                UiLabel(preview, entry.Speaker, 96, 468, 170, 28, 24).HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
        var text = new MessageView { Position = entry.FullPage ? new Vector2(74, 20) : new Vector2(154, 504) };
        preview.AddChild(text);
        text.ConfigureLayout(entry.FullPage);
        text.SetFonts(_preferences.PrimaryFont, _preferences.FullFont);
        text.SetMessage(entry.Text);
        text.RestorePage(record.Page);
        text.RevealAll();
    }
}
