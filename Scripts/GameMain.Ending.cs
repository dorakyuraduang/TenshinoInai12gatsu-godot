using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private EndingPlayer _ending = null!;
    private HashSet<int> _clearedEndings = new();

    private void ShowEnding(int ending)
    {
        StopMessageModes();
        _voicePlayer.Stop();
        _game.Visible = false;
        var archive = new Archive(Path.Combine(_gameDirectory, "ed.a"));
        if (_ending.Credits.Length == 0)
        {
            byte[] data = archive.Read("ending.px").Data;
            _ending.Credits = UiPxDecoder.DecodeFrames(data, Enumerable.Range(0, UiPxDecoder.FrameCount(data)).ToArray())
                .Select(frame => { using (frame.Image) return (Texture2D)ImageTexture.CreateFromImage(frame.Image); }).ToArray();
        }
        string[] names = ["toh", "sin", "asu", "yuk", "mah"];
        using Image strip = UiPxDecoder.DecodeStandalone(archive.Read($"ed_{names[Math.Clamp(ending - 1, 0, 4)]}.px").Data);
        _ending.Strip = ImageTexture.CreateFromImage(strip);
        if (File.Exists(DataPath("endings.json")))
            _clearedEndings = JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(DataPath("endings.json"))) ?? new();
        bool cleared = !_clearedEndings.Add(ending);
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath("endings.json"))!);
        File.WriteAllText(DataPath("endings.json"), JsonSerializer.Serialize(_clearedEndings));
        PlayStoryMusic(2);
        if (_storyMusic.Stream is AudioStreamWav music) music.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        string[] lines = ScenarioScript.Read(_scripts, "@ending.p").Strings.Take(3).ToArray();
        _ending.Play(lines, cleared);
    }
}
