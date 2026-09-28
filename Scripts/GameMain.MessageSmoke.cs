using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private void SmokeAdvance()
    {
        int previous = _activeMessageIndex;
        for (int i = 0; i < 100 && _game.Visible && !_storyEnded; i++)
        {
            _dialogue.RevealAll();
            ContinueMessage();
            if (_activeMessageIndex != previous) return;
        }
    }

    private void SmokeMessage()
    {
        try
        {
            _preferences = new PlayerPreferences();
            _readMessages.Clear();
            _firstLaunch = true;
            BeginStory();
            int first = _eventIndex;
            if (_dialogue.Complete || _dialogue.Revealed != 0) throw new InvalidDataException("Gradual line was revealed on entry");
            _dialogue.Tick(1.0 / 60, 5, false);
            if (Math.Abs(_dialogue.Revealed - 11) > 0.01 || _dialogue.Complete)
                throw new InvalidDataException("Native default text speed differs");
            Advance();
            if (!_dialogue.Complete || _eventIndex != first)
                throw new InvalidDataException("First click skipped rather than completing the line");
            Advance();
            if (_eventIndex == first || _dialogue.Revealed != 0)
                throw new InvalidDataException("Second click did not enter the next gradual line");

            var probe = new MessageView();
            AddChild(probe);
            int lines = 0, maximumPages = 0;
            string preceding = "";
            foreach (SceneEvent item in _events)
            {
                if (item.Kind == SceneEventKind.MessageLayout) { preceding = ""; probe.ConfigureLayout(item.Value != 0); }
                if (item.Kind == SceneEventKind.ClearMessage ||
                    (item.Kind == SceneEventKind.WaitMessage && item.Value != 0)) preceding = "";
                if (item.Kind != SceneEventKind.Dialogue) continue;
                string text = preceding + item.Text;
                probe.SetMessage(text.TrimStart('\u007f'));
                foreach (var page in probe.Pages)
                foreach (MessageView.Glyph glyph in page)
                    if (glyph.Position.X < 0 || glyph.Position.X + glyph.Advance > probe.Size.X ||
                        glyph.Position.Y < glyph.FontSize || glyph.Position.Y > probe.Size.Y)
                        throw new InvalidDataException($"Text exceeds native surface: {item.Source}:{item.Value}");
                maximumPages = Math.Max(maximumPages, probe.PageCount);
                preceding = (item.Flags & 2) != 0 ? "" : text;
                lines++;
            }
            string longText = string.Concat(Enumerable.Repeat("长名字的边界测试（标点）。", 20));
            probe.ConfigureLayout(false);
            probe.Text = "甲<$p36乙A>丙";
            var styled = probe.Pages.Single();
            if (string.Concat(styled.Select(g => g.Text)) != "甲乙A丙" ||
                !styled.Select(g => g.FontSize).SequenceEqual(new[] { 24, 36, 36, 24 }) ||
                !styled.Select(g => g.Advance).SequenceEqual(new float[] { 24, 36, 18, 24 }) ||
                styled.Any(g => g.Position.Y != 40))
                throw new InvalidDataException("Native scoped font size, advances or shared baseline differs");
            probe.Tick(1.0 / 60, 5, false);
            float styledReveal = probe.Revealed;
            probe.AppendMessage("丁");
            if (probe.Revealed != styledReveal || probe.Pages[0][^1].FontSize != 24)
                throw new InvalidDataException("Appending styled text changed reveal distance or leaked the font size");
            probe.Text = "$p36甲\n乙\n丙";
            if (probe.PageCount != 2 || probe.Pages[0].Count != 2 ||
                probe.Pages[0][1].Position.Y != 80 || probe.Pages[1][0].Position.Y != 40 ||
                probe.PageLength != 72)
                throw new InvalidDataException("Large text did not paginate at the message surface height");
            probe.Text = "甲";
            if (probe.Pages[0][0].FontSize != 24 || probe.Pages[0][0].Position.Y != 28)
                throw new InvalidDataException("Font size leaked into the next message");
            probe.Text = new string('甲', 18) + "〔乙";
            if (probe.Pages[0].Count != 20 || probe.Pages[0][18].Text != "〔" ||
                probe.Pages[0][18].Position.Y != 56 || probe.Pages[0][19].Text != "乙")
                throw new InvalidDataException("Native opening punctuation was not moved to the next line");
            probe.Text = longText;
            if (probe.PageCount < 2 || probe.Pages.Sum(p => p.Count) != longText.Length)
                throw new InvalidDataException("Overflow fallback lost glyphs");
            while (!probe.LastPage) { probe.RevealAll(); probe.NextPage(); }
            probe.RevealAll();
            if (!probe.Complete) throw new InvalidDataException("Final overflow page did not finish");

            SetMessageLayout(true);
            if (!_fullPageTone.Visible || _fullPageTone.Position != new Vector2(56, 0) ||
                _fullPageTone.Size != new Vector2(676, 600) ||
                !Mathf.IsEqualApprox(_fullPageTone.Color.R, 0) ||
                !Mathf.IsEqualApprox(_fullPageTone.Color.G, 34 / 255.0f) ||
                !Mathf.IsEqualApprox(_fullPageTone.Color.B, 70 / 255.0f) ||
                !Mathf.IsEqualApprox(_fullPageTone.Color.A, 63 / 128.0f) ||
                _fullPageTone.GetIndex() <= _fullPagePanel.GetIndex())
                throw new InvalidDataException("Full-page native rectangle color, bounds or draw order differs");
            if (_characters.Any(character => character.Visible))
                throw new InvalidDataException("Full-page layout left portrait layers visible");
            SetMessageLayout(false);
            if (_characters.Any(character => !character.Visible))
                throw new InvalidDataException("Normal dialogue layout did not restore portrait layers");

            using (var font = new NativeGlyphFont())
            {
                var glyph = font.Get("文");
                if (glyph == null || glyph.Texture.GetWidth() > 24 || glyph.Texture.GetHeight() > 28)
                    throw new InvalidDataException("Original GDI font could not be rasterized");
            }
            probe.QueueFree();

            int voiced = _events.ToList().FindIndex(e => e.Voice == "com_0110_002.g");
            RestoreScene(voiced);
            _dialogue.RevealAll();
            _autoRunning = true;
            ProcessMessage(10);
            if (_eventIndex != voiced) throw new InvalidDataException("Auto mode interrupted the voice");
            Advance();
            if (_autoRunning || _eventIndex != voiced) throw new InvalidDataException("Click did not cancel auto without advancing");
            _voicePlayer.Stop();
            OpenSettings(0);
            float before = _dialogue.Revealed;
            ProcessMessage(10);
            if (_eventIndex != voiced || _dialogue.Revealed != before)
                throw new InvalidDataException("Dialogue progressed behind settings");
            CloseSystemUi();
            _readMessages.Clear();
            _skipRunning = true;
            RestoreScene(voiced);
            if (_skipRunning) throw new InvalidDataException("Read skip crossed an unread line");

            _surname = "测试";
            _givenName = "姓名";
            if (SpeakerName(0) != "测试" ||
                FormatMessageText("\ue000\ue001") != "姓名测试")
                throw new InvalidDataException("Player name placeholders did not use the confirmed input");
            WriteSlot(39);
            int saved = _activeMessageIndex;
            string savedText = _dialogue.Text;
            SmokeAdvance();
            _surname = _givenName = "错误";
            LoadSlot(39);
            if (_activeMessageIndex != saved || _dialogue.Text != savedText || _surname != "测试" || _givenName != "姓名")
                throw new InvalidDataException("Slot did not restore message and player name");
            for (int tab = 0; tab < 4; tab++)
            {
                OpenSettings(tab);
                if (!_systemLayer.Visible || _systemLayer.GetChildCount() < 10)
                    throw new InvalidDataException("Original settings UI was not assembled");
            }
            _savePage = 4;
            OpenSaveSlots(true);
            if (_systemLayer.GetChildren().OfType<TextureButton>().Count() != 13)
                throw new InvalidDataException("Save UI is not eight slots plus five page selectors");
            CloseSystemUi();
            OpenHistory();
            if (!_historyShowing || _historyText == null || !_historyText.Complete)
                throw new InvalidDataException("Backlog did not restore the completed text");
            CloseSystemUi();
            GD.Print($"Validated {lines} messages: fixed glyph bounds, max {maximumPages} pages, gradual reveal, click completion, voice-aware auto, modal pause, 40 save slots, name restore, backlog, original UI atlases; cursor frames={_waitFrames.Length}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
