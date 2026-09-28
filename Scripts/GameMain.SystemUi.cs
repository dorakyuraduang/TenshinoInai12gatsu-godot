using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private Control _systemLayer = null!;
    private Texture2D[] _systemFrames = [], _saveFrames = [], _commonFrames = [];
    private bool _historyShowing, _slotsLoading;
    private int _historyPosition, _settingsTab, _savePage;
    private MessageView? _historyText;
    private OriginalClock _clock = null!;

    private void BuildSystemUi()
    {
        _waitCursor = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _game.AddChild(_waitCursor);
        _clock = new OriginalClock { Visible = false };
        _game.AddChild(_clock);
        _sceneCover = new TextureRect { Visible = false, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = Vector2.Zero, Size = new Vector2(800, 600) };
        _game.AddChild(_sceneCover);
        _systemLayer = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _systemLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_systemLayer);
        _systemLayer.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true } mouse) return;
            if (mouse.ButtonIndex == MouseButton.Right) CloseSystemUi();
            if (_historyShowing && mouse.ButtonIndex == MouseButton.WheelUp) ChangeHistory(-1);
            if (_historyShowing && mouse.ButtonIndex == MouseButton.WheelDown) ChangeHistory(1);
        };
    }

    private Texture2D[] DecodeUiTextures(string name, params int[] indices)
    {
        byte[] data = _uiArchive!.Read(name).Data;
        if (indices.Length == 0) indices = Enumerable.Range(0, UiPxDecoder.FrameCount(data)).ToArray();
        return UiPxDecoder.DecodeFrames(data, indices).Select(frame =>
        {
            using (frame.Image) return (Texture2D)ImageTexture.CreateFromImage(frame.Image);
        }).ToArray();
    }

    private void InitializeSystemUi()
    {
        _commonFrames = DecodeUiTextures("common.px");
        _fullPagePanel.Texture = _commonFrames[106];
        _clock.Frames = _commonFrames;
        _systemFrames = DecodeUiTextures("system.px");
        _saveFrames = DecodeUiTextures("saveload.px");
        _waitFrames = DecodeUiTextures("cursor.px");
        for (int i = 0; i < _gameButtons.Count; i++)
        {
            int frame = 16 + i * 5;
            _gameButtons[i].TextureNormal = _commonFrames[frame];
            _gameButtons[i].TextureHover = _commonFrames[frame + 1];
            _gameButtons[i].TexturePressed = _commonFrames[frame + 2];
            _gameButtons[i].TextureDisabled = _commonFrames[frame + 3];
        }
    }

    private void EmptySystemUi()
    {
        foreach (Node node in _systemLayer.GetChildren()) { _systemLayer.RemoveChild(node); node.QueueFree(); }
        _historyText = null;
        _historyShowing = false;
        _systemLayer.Visible = true;
        _waitCursor.Visible = false;
    }

    private void CloseSystemUi()
    {
        if (_systemLayer == null) return;
        _systemLayer.Visible = false;
        _historyShowing = false;
        _messageTimer = 0;
        SaveReadMessages();
        SaveUnlocks();
        if (_galleryOpen)
        {
            _galleryOpen = false;
            _galleryMusicScreen = _galleryPlaying = false;
            StopStoryMusic();
            _voicePlayer.Stop();
            foreach (TextureButton button in _titleButtons) button.Visible = true;
            if (_menu.Visible && !_menuMusic.Playing) _menuMusic.Play();
        }
    }

    private static TextureRect UiImage(Control parent, Texture2D texture, float x, float y)
    {
        var image = new TextureRect { Texture = texture, Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(image);
        return image;
    }

    private TextureButton UiButton(Texture2D texture, float x, float y, string hint, Action action, bool selected = true)
    {
        var button = new TextureButton
        {
            TextureNormal = texture, Position = new Vector2(x, y), TooltipText = hint,
            FocusMode = FocusModeEnum.All, Modulate = selected ? Colors.White : new Color(0.55f, 0.55f, 0.55f)
        };
        Color normal = button.Modulate;
        button.MouseEntered += () => button.Modulate = Colors.White;
        button.MouseExited += () => button.Modulate = normal;
        button.Pressed += action;
        _systemLayer.AddChild(button);
        return button;
    }

    private Label UiLabel(Control parent, string text, float x, float y, float width, float height, int size = 18)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, height),
            ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", _uiFont);
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    private void SystemBackdrop()
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f), Size = new Vector2(800, 600),
            MouseFilter = MouseFilterEnum.Ignore };
        _systemLayer.AddChild(shade);
        UiImage(_systemLayer, _systemFrames[40], 0, 0);
        UiImage(_systemLayer, _systemFrames[41], 0, 544);
    }

    private void OpenSettings(int tab)
    {
        if (_systemFrames.Length == 0) return;
        EmptySystemUi();
        _settingsTab = tab;
        SystemBackdrop();
        UiImage(_systemLayer, _systemFrames[tab], 120, 120);
        Vector2[] tabs = [new(120, 43), new(0, 120), new(720, 120), new(120, 496)];
        string[] hints = ["文字设置", "存档／读档／返回标题／退出", "画面设置", "声音设置"];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            UiButton(_systemFrames[4 + i], tabs[i].X, tabs[i].Y, hints[i], () => OpenSettings(index), tab == i);
        }
        if (tab == 0) BuildTextSettings();
        if (tab == 1)
        {
            UiButton(_systemFrames[8], 240, 160, "存档", () => OpenSaveSlots(false)).Disabled = !_game.Visible;
            UiButton(_systemFrames[9], 390, 160, "读档", () => OpenSaveSlots(true));
            UiButton(_systemFrames[10], 240, 310, "返回标题", () => ConfirmSystem("返回标题画面？", ShowMenu));
            UiButton(_systemFrames[11], 390, 310, "退出游戏", () => ConfirmSystem("结束游戏？", () => GetTree().Quit()));
        }
        if (tab == 2) BuildDisplaySettings();
        if (tab == 3) BuildSoundSettings();
    }

    private void SettingPair(int first, int second, Vector2 a, Vector2 b, string title, bool value, Action<bool> change)
    {
        void Set(bool next) { change(next); PersistPreferences(); OpenSettings(_settingsTab); }
        UiButton(_systemFrames[first], a.X, a.Y, title, () => Set(true), value);
        UiButton(_systemFrames[second], b.X, b.Y, title, () => Set(false), !value);
    }

    private void OriginalSlider(int x, int y, int count, int value, string hint, Action<int> change)
    {
        var area = new Control { Position = new Vector2(x, y), Size = new Vector2(count == 10 ? 118 : 238, 30),
            MouseFilter = MouseFilterEnum.Stop, TooltipText = hint };
        _systemLayer.AddChild(area);
        UiImage(area, _systemFrames[count == 10 ? 44 : 45], 0, 0);
        var bars = new List<TextureRect>();
        for (int i = 0; i < count; i++)
        {
            TextureRect bar = UiImage(area, _systemFrames[39], i * 12, 0);
            bar.Visible = i < value;
            bars.Add(bar);
        }
        void Set(int v)
        {
            value = Math.Clamp(v, count == 10 ? 1 : 0, count);
            for (int i = 0; i < count; i++) bars[i].Visible = i < value;
            change(value);
            PersistPreferences();
        }
        area.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true } mouse)
            {
                if (mouse.ButtonIndex == MouseButton.Left) Set((int)(mouse.Position.X * count / area.Size.X) + 1);
                if (mouse.ButtonIndex == MouseButton.WheelUp) Set(value + 1);
                if (mouse.ButtonIndex == MouseButton.WheelDown) Set(value - 1);
            }
            if (input is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left))
                Set((int)(motion.Position.X * count / area.Size.X) + 1);
        };
    }

    private void BuildTextSettings()
    {
        for (int i = 0; i < 3; i++) UiImage(_systemLayer, _systemFrames[46 + i], 160, 137 + 58 * i);
        SettingPair(33, 32, new(480, 147), new(372, 147), "已读快进", _preferences.ReadSkip,
            v => { _preferences.ReadSkip = v; _skipRunning = v && _messageWasRead; });
        SettingPair(34, 35, new(289, 205), new(397, 205), "文字显示方式", _preferences.InstantText, v => _preferences.InstantText = v);
        OriginalSlider(502, 205, 10, _preferences.TextSpeed + 1, "文字速度", v => _preferences.TextSpeed = v - 1);
        SettingPair(37, 36, new(397, 263), new(289, 263), "自动播放", _autoRunning, v => { _autoRunning = v; _skipRunning = false; });
        OriginalSlider(502, 263, 10, _preferences.AutoSpeed + 1, "自动播放速度", v => _preferences.AutoSpeed = v - 1);
        void PickFont(bool full)
        {
            string? font = NativeFontPicker.Pick(full ? _preferences.FullFont : _preferences.PrimaryFont);
            if (font == null) return;
            if (full) _preferences.FullFont = font; else _preferences.PrimaryFont = font;
            PersistPreferences();
            OpenSettings(0);
        }
        UiButton(_systemFrames[49], 160, 311, "对话字体", () => PickFont(false));
        UiButton(_systemFrames[51], 160, 369, "整页叙述字体", () => PickFont(true));
        UiLabel(_systemLayer, _preferences.PrimaryFont, 290, 320, 180, 26);
        UiLabel(_systemLayer, _preferences.FullFont, 290, 377, 180, 26);
        UiButton(_systemFrames[50], 513, 311, "对话字体平滑", () => { _preferences.SmoothText = !_preferences.SmoothText; PersistPreferences(); OpenSettings(0); });
        UiButton(_systemFrames[50], 513, 369, "整页字体平滑", () => { _preferences.SmoothFullText = !_preferences.SmoothFullText; PersistPreferences(); OpenSettings(0); });
        if (_preferences.SmoothText) UiImage(_systemLayer, _systemFrames[38], 587, 321);
        if (_preferences.SmoothFullText) UiImage(_systemLayer, _systemFrames[38], 587, 379);
    }

    private void BuildDisplaySettings()
    {
        for (int i = 0; i < 5; i++) UiImage(_systemLayer, _systemFrames[12 + i], 200, 160 + i * 58);
        SettingPair(22, 23, new(370, 170), new(478, 170), "全屏／窗口",
            DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen,
            v => DisplayServer.WindowSetMode(v ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed));
        SettingPair(24, 25, new(370, 228), new(478, 228), "画面效果", _preferences.Effects, v => _preferences.Effects = v);
        SettingPair(27, 26, new(478, 286), new(370, 286), "等待垂直同步", _preferences.VSync,
            v => { _preferences.VSync = v; DisplayServer.WindowSetVsyncMode(v ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled); });
        SettingPair(28, 29, new(370, 344), new(478, 344), "飘雪效果", _preferences.Particles,
            v => _preferences.Particles = v);
        SettingPair(30, 31, new(370, 402), new(478, 402), "时钟显示", _preferences.Clock, v => _preferences.Clock = v);
    }

    private void BuildSoundSettings()
    {
        for (int i = 0; i < 5; i++) UiImage(_systemLayer, _systemFrames[17 + i], 200, 178 + i * 58);
        OriginalSlider(318, 188, 20, (int)(_preferences.MasterVolume * 20), "总音量", v => _preferences.MasterVolume = v / 20f);
        OriginalSlider(318, 246, 20, (int)(_preferences.MusicVolume * 20), "音乐音量", v => _preferences.MusicVolume = v / 20f);
        OriginalSlider(318, 304, 20, (int)(_preferences.VoiceVolume * 20), "语音音量", v => _preferences.VoiceVolume = v / 20f);
        OriginalSlider(318, 362, 20, (int)(_preferences.SoundVolume * 20), "音效音量", v => _preferences.SoundVolume = v / 20f);
        SettingPair(24, 25, new(362, 420), new(470, 420), "翻页时语音继续／停止", !_preferences.InterruptVoice,
            v => _preferences.InterruptVoice = !v);
    }

    private void ConfirmSystem(string question, Action yes)
    {
        EmptySystemUi();
        SystemBackdrop();
        UiImage(_systemLayer, _commonFrames[68], 105, 174);
        Label label = UiLabel(_systemLayer, question, 130, 201, 540, 42, 24);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", new Color(0.1f, 0.17f, 0.17f));
        UiButton(_commonFrames[69], 251, 262, "确定", () => { CloseSystemUi(); yes(); });
        UiButton(_commonFrames[70], 420, 262, "取消", CloseSystemUi);
    }

    private void OpenHistory()
    {
        if (!_game.Visible || _history.Count == 0) return;
        StopMessageModes();
        _historyPosition = _history.Count - 1;
        RenderHistory();
    }

    private void ChangeHistory(int change)
    {
        if (!_historyShowing) return;
        if (_historyPosition + change >= _history.Count) { CloseSystemUi(); return; }
        _historyPosition = Math.Max(0, _historyPosition + change);
        RenderHistory();
    }

    private void RenderHistory()
    {
        EmptySystemUi();
        _historyShowing = true;
        HistoryEntry entry = _history[_historyPosition];
        if (entry.Background >= 0 && _backgroundArchive != null)
        {
            var (bytes, kind) = _backgroundArchive.Read(ImageName(entry.Background));
            using Image background = PxDecoder.DecodeBackground(bytes, kind);
            UiImage(_systemLayer, ImageTexture.CreateFromImage(background), 0, 0);
        }
        int[] centers = entry.CharacterCount switch { 1 => [399], 2 => [209, 589], _ => [150, 400, 650] };
        if (entry.Characters != null && _characterArchive != null)
            for (int i = 0; i < Math.Min(3, entry.Characters.Length); i++)
            {
                int code = entry.Characters[i];
                if (code == 0) continue;
                if (i >= centers.Length) continue;
                string name = $"{_characterStrings[((code >> 8) & 15) * 2]}{(code & 15) * 100 + ((code >> 4) & 15):000}.px";
                UiPxFrame frame = UiPxDecoder.DecodeCharacter(_characterArchive.Read(name).Data);
                using (frame.Image) UiImage(_systemLayer, ImageTexture.CreateFromImage(frame.Image), centers[i] + frame.Origin.X, 599 + frame.Origin.Y);
            }
        if (entry.FullPage)
        {
            UiImage(_systemLayer, _commonFrames[106], 0, 0);
            _systemLayer.AddChild(CreateFullPageTone());
        }
        else
        {
            UiImage(_systemLayer, _commonFrames[0], 0, 497);
            UiImage(_systemLayer, _commonFrames[8], 0, 462);
            UiLabel(_systemLayer, entry.Speaker, 96, 468, 170, 28, 24).HorizontalAlignment = HorizontalAlignment.Center;
        }
        _historyText = new MessageView { Position = entry.FullPage ? new Vector2(74, 20) : new Vector2(154, 504) };
        _historyText.ConfigureLayout(entry.FullPage);
        _historyText.SetFonts(_preferences.PrimaryFont, _preferences.FullFont);
        _historyText.Smooth = _preferences.SmoothText;
        _historyText.FullSmooth = _preferences.SmoothFullText;
        _historyText.SetMessage(entry.Text);
        _historyText.RevealAll();
        _systemLayer.AddChild(_historyText);
        // Reuse the native replay/back/return controls from the common atlas.
        UiButton(_commonFrames[91], 670, 561, "上一条（滚轮向上）", () => ChangeHistory(-1));
        UiButton(_commonFrames[96], 696, 535, "重播语音", () => ReplayVoice(entry.Voice)).Disabled = entry.Voice.Length == 0;
        UiButton(_commonFrames[101], 722, 509, "返回游戏（右键）", CloseSystemUi);
        UiLabel(_systemLayer, $"{_historyPosition + 1}/{_history.Count}", 18, 505, 115, 24, 16);
    }
}
