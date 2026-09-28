using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private sealed class UnlockRecord
    {
        public HashSet<string> Images { get; set; } = new();
        public HashSet<int> Music { get; set; } = new();
        public HashSet<int> Memoirs { get; set; } = new();
    }
    private UnlockRecord _unlocks = new();
    private bool _unlocksDirty;
    private Texture2D[] _omakeFrames = [], _memoirFrames = [];
    private readonly Dictionary<int, Texture2D[]> _thumbnailPages = new();
    private string[] _cgNames = [];
    private int _galleryPage;
    private bool _galleryOpen;
    private readonly List<int> _galleryPlaylist = new();
    private bool _galleryMusicScreen, _galleryPlaying, _galleryRepeat, _galleryShuffle;
    private int _lastMusicClick;
    private ulong _lastMusicClickTime;

    private void StartGalleryTrack(int track)
    {
        _menuMusic.Stop();
        if (!_storyMusic.Playing) _storyTrack = -1;
        PlayStoryMusic(track);
        if (_storyMusic.Stream is AudioStreamWav stream) stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _galleryPlaying = true;
    }

    private void UpdateGalleryMusic()
    {
        if (!_galleryMusicScreen || !_galleryPlaying || _storyMusic.Playing) return;
        if (_galleryPlaylist.Count == 0) { _galleryPlaying = false; return; }
        int current = _galleryPlaylist.IndexOf(_storyTrack);
        if (_galleryShuffle) StartGalleryTrack(_galleryPlaylist[Random.Shared.Next(_galleryPlaylist.Count)]);
        else if (current + 1 < _galleryPlaylist.Count) StartGalleryTrack(_galleryPlaylist[current + 1]);
        else if (_galleryRepeat) StartGalleryTrack(_galleryPlaylist[0]);
        else _galleryPlaying = false;
    }

    private void LoadUnlocks()
    {
        string path = DataPath("unlocks.json");
        if (File.Exists(path))
            try { _unlocks = JsonSerializer.Deserialize<UnlockRecord>(File.ReadAllText(path)) ?? new(); }
            catch (JsonException error) { GD.PushWarning(error.Message); }
        _cgNames = ScenarioScript.Read(_scripts, "%FlagsDef.p").Strings.ToArray();
        if (File.Exists(DataPath("endings.json")))
            try { _clearedEndings = JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(DataPath("endings.json"))) ?? new(); }
            catch (JsonException error) { GD.PushWarning(error.Message); }
    }

    private void SaveUnlocks()
    {
        if (!_unlocksDirty) return;
        string path = DataPath("unlocks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_unlocks));
        _unlocksDirty = false;
    }

    private void OpenGallery()
    {
        EmptySystemUi();
        _galleryOpen = true;
        foreach (TextureButton button in _titleButtons) button.Visible = false;
        int[] frames = [18, 22, 20];
        string[] hints = ["CG 鉴赏", "回想", "音乐鉴赏"];
        Action[] actions = [OpenCgGallery, OpenMemoirs, OpenMusicGallery];
        for (int i = 0; i < 3; i++)
        {
            TextureButton button = UiButton(_titleFrames[frames[i]], 310, 377 + i * 43, hints[i], actions[i]);
            button.TextureHover = button.TextureFocused = _titleFrames[frames[i] - 1];
        }
        if (_clearedEndings.Count >= 5)
        {
            TextureButton button = UiButton(_titleFrames[24], 310, 506, "声优留言", OpenBonusVoices);
            button.TextureHover = _titleFrames[23];
        }
    }

    private void GalleryBoard(int heading)
    {
        EmptySystemUi();
        _galleryMusicScreen = false;
        _galleryOpen = true;
        if (_omakeFrames.Length == 0) _omakeFrames = DecodeUiTextures("omake.px");
        SystemBackdrop();
        UiImage(_systemLayer, _omakeFrames[0], 11, 69);
        UiImage(_systemLayer, _omakeFrames[heading], 20, 94);
    }

    private void OpenCgGallery()
    {
        GalleryBoard(2);
        if (!_thumbnailPages.TryGetValue(_galleryPage, out Texture2D[]? thumbnails))
            _thumbnailPages[_galleryPage] = thumbnails = DecodeUiTextures($"thumbnail{_galleryPage}.px");
        for (int i = 0; i < thumbnails.Length / 2; i++)
        {
            int index = _galleryPage * 30 + i;
            string name = index < _cgNames.Length ? $"eg{_cgNames[index]}.px" : "";
            bool unlocked = _unlocks.Images.Contains(name);
            TextureButton button = UiButton(thumbnails[i * 2 + (unlocked ? 0 : 1)], 68 + i % 6 * 120, 90 + i / 6 * 88,
                unlocked ? "查看 CG" : "", () => ShowGalleryImage(name));
            button.Disabled = !unlocked;
        }
        for (int i = 0; i < 5; i++)
        {
            int page = i;
            UiButton(_omakeFrames[12 + 2 * i + (i == _galleryPage ? 1 : 0)], 22, 320 + 40 * i,
                $"第 {i + 1} 页", () => { _galleryPage = page; OpenCgGallery(); });
        }
    }

    private void ShowGalleryImage(string name)
    {
        if (!_unlocks.Images.Contains(name) || _backgroundArchive == null) return;
        EmptySystemUi();
        _galleryOpen = true;
        var (bytes, kind) = _backgroundArchive.Read(name);
        using Image image = PxDecoder.DecodeBackground(bytes, kind);
        TextureButton picture = UiButton(ImageTexture.CreateFromImage(image), 0, 0, "", OpenCgGallery);
        picture.Size = new Vector2(800, 600);
    }

    private void OpenMusicGallery()
    {
        GalleryBoard(3);
        _galleryMusicScreen = true;
        for (int i = 0; i < 24; i++)
        {
            int track = i + 1;
            int x = 74 + i / 12 * 362, y = 74 + i % 12 * 38;
            bool unlocked = _unlocks.Music.Contains(track) || track == 24;
            TextureRect? marker = null;
            TextureButton cell = UiButton(_omakeFrames[26], x, y, $"单击加入播放列表，双击播放 BGM {track:00}", () =>
            {
                ulong now = Time.GetTicksMsec();
                if (_lastMusicClick == track && now - _lastMusicClickTime <= 500)
                {
                    if (!_galleryPlaylist.Contains(track)) _galleryPlaylist.Add(track);
                    StartGalleryTrack(track);
                }
                else if (!_galleryPlaylist.Remove(track)) _galleryPlaylist.Add(track);
                _lastMusicClick = track;
                _lastMusicClickTime = now;
                if (marker != null) marker.Visible = _galleryPlaylist.Contains(track);
            });
            cell.Disabled = !unlocked;
            UiImage(cell, _omakeFrames[28 + i], 40, 5).Modulate = unlocked ? Colors.White : new Color(0.2f, 0.2f, 0.2f);
            marker = UiImage(cell, _omakeFrames[25], 9, 6);
            marker.Visible = _galleryPlaylist.Contains(track);
        }
        UiButton(_omakeFrames[6], 28, 320, "播放／停止", () =>
        {
            if (_galleryPlaying) { _galleryPlaying = false; _storyMusic.Stop(); }
            else if (_galleryPlaylist.Count > 0) StartGalleryTrack(_galleryPlaylist[0]);
        });
        UiButton(_omakeFrames[8], 28, 380, "循环播放列表", () =>
            { _galleryRepeat = !_galleryRepeat; OpenMusicGallery(); }, _galleryRepeat);
        UiButton(_omakeFrames[10], 28, 440, "随机播放", () =>
            { _galleryShuffle = !_galleryShuffle; OpenMusicGallery(); }, _galleryShuffle);
    }

    private void OpenMemoirs()
    {
        GalleryBoard(1);
        if (_memoirFrames.Length == 0) _memoirFrames = DecodeUiTextures("memoirs.px");
        for (int i = 0; i < _memoirFrames.Length / 2; i++)
        {
            int index = i;
            bool unlocked = _unlocks.Memoirs.Contains(32 + i + 1);
            UiButton(_memoirFrames[i * 2 + (unlocked ? 0 : 1)], 68 + i % 6 * 120, 90 + i / 6 * 88,
                "", () => PlayMemoir(index)).Disabled = !unlocked;
        }
    }

    private void PlayMemoir(int index)
    {
        ushort[] code = ScenarioScript.Read(_scripts, "_omakeMenu.p").Code;
        // _omakeMenu:4124 switch table contains the original replay entrypoints.
        int switchIp = Array.IndexOf(code, (ushort)0x7753, 4124);
        int table = code[switchIp + 1], count = code[table + 1];
        for (int i = 0; i < count; i++)
        {
            int target = code[table + 2 + i * 2];
            if (target == 0 || code[table + 3 + i * 2] != index) continue;
            if (code[target] != 0x6143) throw new InvalidDataException("Replay table target is not a script call");
            CloseSystemUi();
            _runtime = new ScenarioRuntime(_scripts, code[target + 1]) { Replay = true };
            _routeEvents = new List<SceneEvent>();
            _events = _routeEvents;
            _eventIndex = -1;
            _firstLaunch = false;
            _menu.Visible = false;
            _game.Visible = true;
            _menuMusic.Stop();
            StopStoryMusic();
            ClearCharacters();
            ResetMessageState();
            AdvanceScene();
            return;
        }
    }

    private void OpenBonusVoices()
    {
        GalleryBoard(4);
        string[] names = ["toh", "sin", "asu", "yuk", "mah", "emi"];
        for (int i = 0; i < names.Length; i++)
        {
            string voice = $"!{names[i]}_bonus.g";
            TextureButton button = UiButton(_omakeFrames[26], 230, 105 + i * 60, "播放留言", () => ReplayVoice(voice));
            UiImage(button, _omakeFrames[54 + i], 4, 5);
        }
    }
}
