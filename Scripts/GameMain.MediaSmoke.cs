using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private async void SmokeMedia()
    {
        try
        {
            _firstLaunch = false;
            BeginStory();
            int voices = 0, lines = 0, maximumCharacters = 0, savedPosition = -1;
            int[] savedCharacters = [];
            int savedMusic = 0;
            while (_eventIndex < _events.Count && _dialogue.Text != "未完")
            {
                SceneEvent current = _events[_eventIndex];
                if (current.Kind != SceneEventKind.Dialogue)
                    throw new InvalidDataException("Story stopped on a non-dialogue event");
                if (current.Voice.Length > 0)
                {
                    voices++;
                    if (!_voicePlayer.Playing || _voicePlayer.Stream == null || _voicePlayer.Stream.GetLength() <= 0)
                        throw new InvalidDataException($"Voice did not play: {current.Voice}");
                }
                else if (_voicePlayer.Playing)
                    throw new InvalidDataException("Previous voice continued into an unvoiced line");
                if (_storyTrack > 0 && (!_storyMusic.Playing || _storyMusic.Stream == null))
                    throw new InvalidDataException("Script BGM did not play");
                int characterCount = _characterCodes.Count(code => code != 0);
                maximumCharacters = Math.Max(maximumCharacters, characterCount);
                if (savedPosition < 0 && characterCount == 2 && current.Voice.Length > 0 && _storyTrack > 0)
                {
                    savedPosition = _eventIndex;
                    savedCharacters = (int[])_characterCodes.Clone();
                    savedMusic = _storyTrack;
                    Save();
                }
                if (current.Voice == "com_0110_002.g")
                {
                    if (_storyTrack != 8 || _characterCodes[0] != 0x733 ||
                        _characters[0].Position != new Vector2(0, 5))
                        throw new InvalidDataException($"First portrait scene differs: music={_storyTrack}, code={_characterCodes[0]:x}, position={_characters[0].Position}");
                    ValidatePixels(_characters[0].Texture!.GetImage(), "emi303.px");
                    using AudioStreamPlayback playback = _voicePlayer.Stream!.InstantiatePlayback();
                    playback.Start();
                    Vector2[] samples = playback.MixAudio(1.0f, 44100);
                    if (!samples.Any(sample => sample.LengthSquared() > 0.000001f))
                        throw new InvalidDataException("First voice decoded as silence");
                    playback.Stop();
                    await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                    if (_voicePlayer.GetPlaybackPosition() < 0.3 || _storyMusic.GetPlaybackPosition() < 0.3)
                        throw new InvalidDataException("Voice or BGM playback clock did not advance");
                    // Native !SysSound:1150 with argument 10 requests a 100 ms fade.
                    PlayStoryMusic(0, 10);
                    await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
                    if (_storyMusic.Playing || _fadingMusic.Playing)
                        throw new InvalidDataException("BGM fade-out did not stop its player");
                    PlayStoryMusic(8);
                }
                lines++;
                SmokeAdvance();
                if (lines % 100 == 0)
                {
                    GD.Print($"Media smoke: {lines} lines, {voices} voices");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            if (voices < 400 || maximumCharacters < 2 || savedPosition < 0)
                throw new InvalidDataException("Media smoke did not cover voices and multiple characters");
            ShowMenu();
            if (_storyMusic.Playing || _fadingMusic.Playing || _voicePlayer.Playing)
                throw new InvalidDataException("Story audio continued into the title menu");
            Load();
            if (_eventIndex != savedPosition || !_characterCodes.SequenceEqual(savedCharacters) ||
                _storyTrack != savedMusic || !_voicePlayer.Playing || !_storyMusic.Playing)
                throw new InvalidDataException("Saved BGM, voice, and characters did not restore");
            // The previous release stored indexes of only background/speaker/dialogue events.
            int legacy = _events.Take(savedPosition + 1).Count(item => item.Kind is
                SceneEventKind.Background or SceneEventKind.Speaker or SceneEventKind.Dialogue) - 1;
            File.WriteAllText(SavePath, legacy.ToString());
            ShowMenu();
            Load();
            if (_eventIndex != savedPosition || !_characterCodes.SequenceEqual(savedCharacters))
                throw new InvalidDataException("Legacy save did not migrate to the same dialogue");
            Save();
            ShowMenu();
            GD.Print($"Validated {lines} story lines, {voices} voices, up to {maximumCharacters} characters, BGM and both save formats");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"Media smoke event {_eventIndex}: {error}");
            GetTree().Quit(1);
        }
    }

    private static void ValidatePixels(Image image, string name)
    {
        using (image)
        {
            byte[] rgba = image.GetData();
            int visible = 0, transparent = 0;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] > 0) visible++;
                else transparent++;
            }
            if (visible < 100 || transparent < 100)
                throw new InvalidDataException($"Character alpha is invalid: {name}");
        }
    }
}
