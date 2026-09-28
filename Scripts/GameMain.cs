using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain : Control
{
    private Archive? _backgroundArchive;
    private Archive? _musicArchive;
    private Archive? _uiArchive;
    private Archive? _characterArchive;
    private Archive? _voiceArchive;
    private Archive? _soundArchive;
    private string[] _backgrounds = Array.Empty<string>();
    private string[] _tracks = Array.Empty<string>();
    private IReadOnlyList<SceneEvent> _events = Array.Empty<SceneEvent>();
    private int _prologueEventCount;
    private bool _firstLaunch;
    private string _openingPath = "";
    private int _eventIndex = -1;
    private int _speakerId = 255;
    private string[] _characterStrings = Array.Empty<string>();
    private TextureRect _picture = null!;
    private OriginalWeather _weather = null!;
    private int _currentBackground = -1;
    private readonly TextureRect[] _characters = new TextureRect[3];
    private readonly int[] _characterCodes = new int[3];
    private readonly int[] _previousCharacterCodes = new int[3];
    private readonly Vector2I[] _characterOrigins = new Vector2I[3];
    private int _characterSlotCount;
    private bool _restoringCharacters;
    private Label _speaker = null!;
    private MessageView _dialogue = null!;
    private Label _menuMessage = null!;
    private Control _menu = null!;
    private Control _splash = null!;
    private ColorRect _splashGround = null!;
    private int _logoDirections;
    private readonly float[] _logoDurations = new float[3];
    private OpeningMoviePlayer _opening = null!;
    private AudioStreamPlayer _menuMusic = null!;
    private AudioStreamPlayer _storyMusic = null!;
    private AudioStreamPlayer _fadingMusic = null!;
    private Tween? _musicTween;
    private AudioStreamPlayer _voicePlayer = null!;
    private int _storyTrack = -1;
    private TextureRect _titleBackground = null!;
    private TextureRect _titleOverlay = null!;
    private TextureRect _logo = null!;
    private readonly List<TextureRect> _logoLayers = new();
    private Texture2D[] _titleFrames = Array.Empty<Texture2D>();
    private Vector2I[] _titleFrameOrigins = Array.Empty<Vector2I>();
    private Texture2D[] _logoFrames = Array.Empty<Texture2D>();
    private Vector2 _logoCompositePosition = new(150, 180);
    private readonly List<TextureButton> _titleButtons = new();
    private float _splashElapsed;
    private Control _game = null!;
    private TextureButton _continue = null!;
    private TextureRect _dialoguePanel = null!;
    private TextureRect _speakerPanel = null!;
    private readonly List<TextureButton> _gameButtons = new();
    private Control _nameScreen = null!;
    private TextureRect _nameBackground = null!;
    private TextureRect _nameTopBorder = null!;
    private TextureRect _nameBottomBorder = null!;
    private TextureRect _namePanel = null!;
    private TextureButton _nameReset = null!;
    private TextureButton _nameAccept = null!;
    private LineEdit _surnameEdit = null!;
    private LineEdit _givenNameEdit = null!;
    private Control _nameConfirm = null!;
    private TextureRect _confirmPanel = null!;
    private Label _confirmText = null!;
    private TextureButton _confirmYes = null!;
    private TextureButton _confirmNo = null!;
    private string _surname = "木田";
    private string _givenName = "时纪";

    private static string DataPath(string name) => OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--smoke-"))
        ? ProjectSettings.GlobalizePath($"res://../artifacts/godot-tests/{name}")
        : ProjectSettings.GlobalizePath($"user://{name}");
    private static string SavePath => DataPath("save1.txt");
    private static string ProgressPath => DataPath("progress.txt");
    private static string NamePath => DataPath("player_name.txt");

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--smoke-platform")) { SmokePlatformResources(); return; }
        BuildUi();
        StartPlatformResources();
    }

    private void InitializeGameResources()
    {
        try
        {
            string gameDir = _gameDirectory;
            _openingPath = Path.Combine(gameDir, "openning.v");
            if (!File.Exists(_openingPath)) throw new FileNotFoundException("Original OP movie is missing", _openingPath);
            _backgroundArchive = new Archive(Path.Combine(gameDir, "egbg.a"));
            _musicArchive = new Archive(Path.Combine(gameDir, "music.a"));
            _uiArchive = new Archive(Path.Combine(gameDir, "sys.a"));
            _characterArchive = new Archive(Path.Combine(gameDir, "char.a"));
            _voiceArchive = new Archive(Path.Combine(gameDir, "voice.a"));
            _soundArchive = new Archive(Path.Combine(gameDir, "se.a"));
            var scripts = new Archive(Path.Combine(gameDir, "tenshi_dvd.a"));
            _scripts = scripts;
            LoadUnlocks();
            UiPxFrame[] titleFrames = UiPxDecoder.DecodeFrames(_uiArchive.Read("title.px").Data,
                Enumerable.Range(0, 28).ToArray());
            _titleFrames = titleFrames.Select(frame => ImageTexture.CreateFromImage(frame.Image)).ToArray();
            _titleFrameOrigins = titleFrames.Select(frame => frame.Origin).ToArray();
            UiPxFrame[] nameFrames = UiPxDecoder.DecodeFrames(_uiArchive.Read("inputName.px").Data, 0, 1, 2);
            UiPxFrame[] commonFrames = UiPxDecoder.DecodeFrames(_uiArchive.Read("common.px").Data,
                0, 8, 16, 21, 26, 68, 69, 70);
            Texture2D[] names = nameFrames.Select(frame => ImageTexture.CreateFromImage(frame.Image)).ToArray();
            Texture2D[] common = commonFrames.Select(frame => ImageTexture.CreateFromImage(frame.Image)).ToArray();
            _nameBackground.Texture = _titleFrames[5];
            _namePanel.Texture = names[0];
            _nameReset.TextureNormal = names[1];
            _nameAccept.TextureNormal = names[2];
            _dialoguePanel.Texture = common[0];
            _speakerPanel.Texture = common[1];
            for (int i = 0; i < _gameButtons.Count; i++)
                _gameButtons[i].TextureNormal = common[2 + i];
            _confirmPanel.Texture = common[5];
            _confirmYes.TextureNormal = common[6];
            _confirmNo.TextureNormal = common[7];
            InitializeSystemUi();
            _nameTopBorder.Texture = _systemFrames[40];
            _nameBottomBorder.Texture = _systemFrames[41];
            LoadPreferences();
            UiPxFrame[] logoFrames = UiPxDecoder.DecodeFrames(_uiArchive.Read("leaflogo.px").Data, 0, 1);
            _logoFrames = logoFrames.Select(frame => ImageTexture.CreateFromImage(frame.Image)).ToArray();
            _titleBackground.Texture = _titleFrames[5];
            _titleOverlay.Texture = _titleFrames[27];
            _titleOverlay.Position = new Vector2(400 + _titleFrameOrigins[27].X,
                200 + _titleFrameOrigins[27].Y);
            _logo.Texture = _logoFrames[1];
            _logo.Position = new Vector2(120 + logoFrames[1].Origin.X, 220 + logoFrames[1].Origin.Y);
            _logoCompositePosition = new Vector2(120 + logoFrames[0].Origin.X,
                220 + logoFrames[0].Origin.Y);
            foreach (TextureRect layer in _logoLayers)
                layer.Texture = _logoFrames[0];
            for (int row = 0; row < _titleButtons.Count; row++)
            {
                _titleButtons[row].TextureNormal = _titleFrames[8 + row * 2];
                _titleButtons[row].TextureHover = _titleFrames[7 + row * 2];
                _titleButtons[row].TexturePressed = _titleFrames[7 + row * 2];
                _titleButtons[row].TextureFocused = _titleFrames[7 + row * 2];
                _titleButtons[row].TextureDisabled = _titleFrames[8 + row * 2];
            }
            _backgrounds = _backgroundArchive.Names.Where(n => n.EndsWith(".px", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            _tracks = _musicArchive.Names.Where(n => n.EndsWith(".w", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!_musicArchive.Contains("024.w"))
                throw new FileNotFoundException("Original title BGM 024.w is missing from music.a");
            AudioStreamWav menuStream = AudioDecoder.Decode(_musicArchive.Read("024.w").Data);
            menuStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            menuStream.LoopBegin = 0;
            int sampleBytes = menuStream.Stereo ? 2 : 1;
            sampleBytes *= menuStream.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1;
            menuStream.LoopEnd = menuStream.Data.Length / sampleBytes;
            _menuMusic.Stream = menuStream;
            _characterStrings = ScenarioScript.Read(scripts, "%CharDef.p").Strings.ToArray();
            var sequence = new List<SceneEvent>();
            sequence.AddRange(ScenarioScript.Read(scripts, "com_0100.p").ReadLinearScene());
            _prologueEventCount = sequence.Count;
            foreach (string name in new[] { "com_0110.p", "com_0120.p" })
                sequence.AddRange(ScenarioScript.Read(scripts, name).ReadLinearScene());
            _events = sequence;
            _baseEvents = sequence.ToArray();
            ResetRoutes();
            _firstLaunch = !File.Exists(ProgressPath);
            if (File.Exists(NamePath))
            {
                string[] savedName = File.ReadAllLines(NamePath);
                if (savedName.Length >= 2)
                {
                    _surname = string.IsNullOrWhiteSpace(savedName[0]) ? "木田" : savedName[0];
                    _givenName = string.IsNullOrWhiteSpace(savedName[1]) ? "时纪" : savedName[1];
                }
            }

            foreach (SceneEvent item in _events.Where(e => e.Kind == SceneEventKind.Background))
            {
                string imageName = ImageName(item.Value);
                if (!_backgroundArchive.Contains(imageName))
                    throw new FileNotFoundException($"Scene image {imageName} is missing from egbg.a");
            }

            _resourcesReady = true;
            if (OS.GetCmdlineUserArgs().Contains("--validate-archives"))
            {
                ValidateResources();
                GD.Print($"Validated common-route script: {_events.Count(e => e.Kind == SceneEventKind.Dialogue)} dialogue lines");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-message"))
            {
                SmokeMessage();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-scene"))
            {
                SmokeScene();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-routes"))
            {
                SmokeRoutes(scripts);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-route-ui"))
            {
                SmokeRouteUi();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-render"))
            {
                SmokeRender();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-effects"))
            {
                SmokeEffects();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-startup"))
            {
                SmokeStartup();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-op"))
            {
                SmokeOpening();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-op-full"))
            {
                SmokeOpeningFull();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-op-sync"))
            {
                SmokeOpeningSync();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-game"))
            {
                if (_titleBackground.Texture.GetWidth() != 800 || _logo.Texture.GetWidth() != 564)
                    throw new InvalidDataException("Original startup resources did not decode");
                int[] dialogueIds = _events.Where(e => e.Kind == SceneEventKind.Dialogue).Select(e => e.Value).ToArray();
                if (dialogueIds[0] != 3 || Array.IndexOf(dialogueIds, 28) < 0 || Array.IndexOf(dialogueIds, 847) < 0)
                    throw new InvalidDataException("Common-route scene order is incomplete");
                _firstLaunch = true;
                ShowSplash();
                FinishSplash();
                if (_splash.Visible || !_game.Visible || _eventIndex < 0 ||
                    _events[_eventIndex].Value != 3 || _picture.Texture == null)
                    throw new InvalidDataException("Logo did not enter the first-launch prologue");
                SmokeAdvance();
                if (_events[_eventIndex].Value != 4 || _picture.Texture == null)
                    throw new InvalidDataException("Prologue did not advance to dialogue 4 with its event image");
                int savedIndex = _eventIndex;
                Save();
                ShowMenu();
                Load();
                if (!_game.Visible || _eventIndex != savedIndex || _dialogue.Text != _events[savedIndex].Text)
                    throw new InvalidDataException("Saved dialogue did not restore correctly");
                while (_game.Visible && _eventIndex < _prologueEventCount - 1)
                    SmokeAdvance();
                SmokeAdvance();
                if (!_opening.Visible || _game.Visible || _firstLaunch || !File.Exists(ProgressPath))
                    throw new InvalidDataException("First-launch prologue did not enter the OP movie");
                _opening.Skip();
                if (!_menu.Visible || !_menuMusic.Playing || _menuMusic.Stream != menuStream)
                    throw new InvalidDataException("Skipping OP did not start the original title BGM");
                Start();
                if (!_nameScreen.Visible || _game.Visible || _namePanel.Texture?.GetSize() != new Vector2(590, 227))
                    throw new InvalidDataException("Start did not enter the original name-setting screen");
                if (_namePanel.Position != new Vector2(105, 109) ||
                    _nameTopBorder.Texture?.GetSize().X != 800 ||
                    _nameBottomBorder.Position != new Vector2(0, 544) ||
                    _surnameEdit.Position != new Vector2(188, 192) ||
                    _givenNameEdit.Position != new Vector2(412, 191) ||
                    _nameAccept.TextureNormal?.GetSize() != new Vector2(129, 53) ||
                    _dialoguePanel.Texture?.GetSize() != new Vector2(800, 103) ||
                    _speakerPanel.Texture?.GetSize() != new Vector2(360, 40) ||
                    _gameButtons.Any(button => button.TextureNormal?.GetSize() != new Vector2(34, 34)))
                    throw new InvalidDataException("Original name or dialogue UI resources are not assembled at script coordinates");
                _surnameEdit.Text = "测试";
                _givenNameEdit.Text = "姓名";
                ConfirmName();
                if (!_nameConfirm.Visible || _confirmPanel.Texture?.GetSize() != new Vector2(590, 162) ||
                    _confirmText.GetThemeColor("font_color") != Colors.White ||
                    _confirmText.Text != "测试姓名这样可以吗？")
                    throw new InvalidDataException("Name confirmation did not use the original prompt frame");
                AcceptName();
                if (_menuMusic.Playing)
                    throw new InvalidDataException("Title BGM continued over the story");
                SceneEvent replayFirst = _events.Skip(_prologueEventCount)
                    .First(e => e.Kind == SceneEventKind.Dialogue);
                if (_dialogue.Text != replayFirst.Text || _picture.Texture == null)
                    throw new InvalidDataException("Replay did not skip the first-launch prologue");
                GD.Print("Validated logo-to-prologue-to-menu transition, replay branch, images, and save/load");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--smoke-media"))
            {
                SmokeMedia();
                return;
            }
            _continue.Disabled = !File.Exists(SavePath) && !Enumerable.Range(0, 40).Any(i => File.Exists(SlotPath(i)));
            ShowSplash();
        }
        catch (Exception error)
        {
            _resourcesReady = false;
            ShowStartupIssue($"资源加载失败：\n{error.Message}\n\n请检查原资源包后重试。");
            GD.PushError(error.ToString());
            if (OS.GetCmdlineUserArgs().Contains("--validate-archives") ||
                OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--smoke-")))
                GetTree().Quit(1);
        }
    }

    private static bool IsEscapeInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
            return key.Keycode == Key.Escape;
        return input is InputEventAction { Pressed: true } action &&
            action.Action.ToString() == "ui_cancel";
    }

    private void HandleAndroidBackRequest()
    {
        // Android sends a window notification rather than an Escape key event.
        // Reuse the same page priority and resource-readiness checks as Escape.
        using var cancel = new InputEventAction { Action = "ui_cancel", Pressed = true };
        _UnhandledInput(cancel);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!_resourcesReady) return;
        bool escape = IsEscapeInput(input);
        if (_ending.Visible)
        {
            if (_ending.CanSkip && (escape || input is InputEventKey { Pressed: true, Echo: false } endingKey &&
                endingKey.Keycode is Key.Space or Key.Enter)) _ending.Finish();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (HandleSystemInput(input)) { GetViewport().SetInputAsHandled(); return; }
        if (_splash.Visible && (escape || input is InputEventKey splashKey && splashKey.Pressed &&
            splashKey.Keycode is Key.Space or Key.Enter))
        {
            FinishSplash();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_opening.Visible && (escape || input is InputEventKey movieKey && movieKey.Pressed && !movieKey.Echo &&
            movieKey.Keycode is Key.Space or Key.Enter))
        {
            _opening.Skip();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_nameScreen.Visible && escape)
        {
            if (_nameConfirm.Visible) _nameConfirm.Visible = false;
            else ShowMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_game.Visible) return;
        if (escape)
        {
            OpenSettings(0);
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode is Key.Space or Key.Enter)
                Advance();
            else
                return;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_resourcesReady) return;
        UpdateGalleryMusic();
        if (_game.Visible && !_systemLayer.Visible) _weather.Tick(delta);
        ProcessMessage(delta);
        if (!_splash.Visible) return;
        _splashElapsed += (float)delta;
        UpdateSplashAnimation(_splashElapsed);
        if (_splashElapsed >= 9.002f) FinishSplash();
    }

    private void BuildUi()
    {
        _game = new Control { Visible = false };
        _game.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_game);

        var ground = new ColorRect { Color = new Color(0.02f, 0.025f, 0.03f), MouseFilter = MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        _game.AddChild(ground);

        _picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _picture.SetAnchorsPreset(LayoutPreset.FullRect);
        _game.AddChild(_picture);

        for (int i = 0; i < _characters.Length; i++)
        {
            _characters[i] = new TextureRect { MouseFilter = MouseFilterEnum.Ignore };
            _game.AddChild(_characters[i]);
        }
        BuildSceneTone();
        _weather = new OriginalWeather();
        _game.AddChild(_weather);

        var clickArea = new Control { MouseFilter = MouseFilterEnum.Stop };
        clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
        clickArea.GuiInput += input =>
        {
            if (input is not InputEventMouseButton mouse || !mouse.Pressed) return;
            if (mouse.ButtonIndex == MouseButton.Left) Advance();
            else if (mouse.ButtonIndex == MouseButton.Right) OpenSettings(0);
            else if (mouse.ButtonIndex == MouseButton.Middle) ToggleMessageWindow();
            else if (mouse.ButtonIndex == MouseButton.WheelUp) OpenHistory();
        };
        _game.AddChild(clickArea);

        _fullPagePanel = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _game.AddChild(_fullPagePanel);
        _fullPageTone = CreateFullPageTone();
        _fullPageTone.Visible = false;
        _game.AddChild(_fullPageTone);
        _dialoguePanel = new TextureRect { Position = new Vector2(0, 497), MouseFilter = MouseFilterEnum.Ignore };
        _game.AddChild(_dialoguePanel);
        _speakerPanel = new TextureRect { Position = new Vector2(0, 462), MouseFilter = MouseFilterEnum.Ignore };
        _game.AddChild(_speakerPanel);

        _speaker = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _speaker.Position = new Vector2(96, 468);
        _speaker.Size = new Vector2(170, 28);
        _speaker.HorizontalAlignment = HorizontalAlignment.Center;
        _speaker.AddThemeFontSizeOverride("font_size", 24);
        _speaker.AddThemeFontOverride("font", _uiFont);
        _speaker.ClipText = true;
        _speaker.AddThemeColorOverride("font_color", new Color(0.96f, 0.97f, 0.94f));
        _game.AddChild(_speaker);

        _dialogue = new MessageView();
        _dialogue.Position = new Vector2(154, 504);
        _dialogue.Size = new Vector2(494, 90);
        _game.AddChild(_dialogue);

        AddGameButton("回看", new Vector2(670, 561), OpenHistory);
        AddGameButton("自动播放", new Vector2(696, 535), ToggleAuto);
        AddGameButton("菜单", new Vector2(722, 509), () => OpenSettings(0));
        _choicesLayer = new Control { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _choicesLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        _game.AddChild(_choicesLayer);

        _menu = new Control { Visible = false };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        _titleBackground = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _titleBackground.SetAnchorsPreset(LayoutPreset.FullRect);
        _menu.AddChild(_titleBackground);

        _titleOverlay = new TextureRect
        {
            Position = new Vector2(400, 200),
            Size = new Vector2(480, 198),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _menu.AddChild(_titleOverlay);

        // _sysMenu.p places five 160x42 menu cells at x=310, y=377+43*n.
        AddTitleButton("开始", 0, Start);
        _continue = AddTitleButton("继续", 1, () => OpenSaveSlots(true));
        AddTitleButton("设置", 2, () => OpenSettings(2));
        AddTitleButton("鉴赏", 3, OpenGallery);
        AddTitleButton("退出", 4, () => GetTree().Quit());

        _menuMessage = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _menuMessage.SetAnchorsPreset(LayoutPreset.BottomWide);
        _menuMessage.OffsetLeft = 24;
        _menuMessage.OffsetRight = -24;
        _menuMessage.OffsetTop = -60;
        _menuMessage.OffsetBottom = -20;
        _menu.AddChild(_menuMessage);

        _nameScreen = new Control { Visible = false };
        _nameScreen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_nameScreen);
        _nameBackground = new TextureRect { MouseFilter = MouseFilterEnum.Ignore };
        _nameBackground.SetAnchorsPreset(LayoutPreset.FullRect);
        _nameBackground.Material = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            void fragment() {
                vec4 source = texture(TEXTURE, UV);
                COLOR = vec4(max(source.rgb - vec3(112.0 / 255.0), vec3(0.0)), source.a);
            }
            """ } };
        _nameScreen.AddChild(_nameBackground);
        _nameTopBorder = new TextureRect { Position = Vector2.Zero, MouseFilter = MouseFilterEnum.Ignore };
        _nameScreen.AddChild(_nameTopBorder);
        _nameBottomBorder = new TextureRect { Position = new Vector2(0, 544), MouseFilter = MouseFilterEnum.Ignore };
        _nameScreen.AddChild(_nameBottomBorder);
        _namePanel = new TextureRect { Position = new Vector2(105, 109), MouseFilter = MouseFilterEnum.Ignore };
        _nameScreen.AddChild(_namePanel);
        _surnameEdit = AddNameEdit(new Vector2(188, 192), "姓");
        _givenNameEdit = AddNameEdit(new Vector2(412, 191), "名");
        _nameReset = AddNameButton(new Vector2(420, 262), "恢复默认姓名", ResetName);
        _nameAccept = AddNameButton(new Vector2(251, 262), "确认姓名", ConfirmName);

        _nameConfirm = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _nameConfirm.SetAnchorsPreset(LayoutPreset.FullRect);
        _nameScreen.AddChild(_nameConfirm);
        _nameConfirm.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
        var confirmShade = new ColorRect { Color = Colors.White, MouseFilter = MouseFilterEnum.Stop,
            Material = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type canvas_item;
                uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
                void fragment() {
                    vec3 source = texture(screen_texture, SCREEN_UV).rgb;
                    float green = max(source.g - 112.0 / 255.0, 0.0);
                    float gray = min(green + 128.0 / 255.0, 1.0);
                    COLOR = vec4(vec3(gray), 1.0);
                }
                """ } } };
        confirmShade.SetAnchorsPreset(LayoutPreset.FullRect);
        _nameConfirm.AddChild(confirmShade);
        _confirmPanel = new TextureRect { Position = new Vector2(105, 174), MouseFilter = MouseFilterEnum.Ignore };
        _nameConfirm.AddChild(_confirmPanel);
        _confirmText = new Label
        {
            Position = new Vector2(150, 201), Size = new Vector2(500, 42),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore
        };
        _confirmText.AddThemeFontOverride("font", _uiFont);
        _confirmText.AddThemeFontSizeOverride("font_size", 24);
        _confirmText.AddThemeColorOverride("font_color", Colors.White);
        _nameConfirm.AddChild(_confirmText);
        _confirmYes = new TextureButton { Position = new Vector2(251, 262), TooltipText = "确定", FocusMode = FocusModeEnum.All };
        _confirmYes.Pressed += AcceptName;
        _nameConfirm.AddChild(_confirmYes);
        _confirmNo = new TextureButton { Position = new Vector2(420, 262), TooltipText = "返回修改", FocusMode = FocusModeEnum.All };
        _confirmNo.Pressed += () => _nameConfirm.Visible = false;
        _nameConfirm.AddChild(_confirmNo);

        _splash = new Control { Visible = false };
        _splash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_splash);
        _splashGround = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        _splashGround.SetAnchorsPreset(LayoutPreset.FullRect);
        _splash.AddChild(_splashGround);
        for (int layerIndex = 0; layerIndex < 3; layerIndex++)
        {
            _splash.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
            var layer = new TextureRect
            {
                Position = new Vector2(150, 180),
                Size = new Vector2(500, 148),
                MouseFilter = MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = new Shader { Code = """
                    shader_type canvas_item;
                    uniform sampler2D screen_texture : hint_screen_texture, filter_nearest, repeat_disable;
                    uniform vec3 channel_alpha = vec3(0.0);
                    void fragment() {
                        vec4 source = texture(TEXTURE, UV);
                        vec3 previous = texture(screen_texture, SCREEN_UV).rgb;
                        // Native sparse draw mode 6 interpolates each RGB channel independently.
                        COLOR = vec4(mix(previous, source.rgb, source.a * channel_alpha), 1.0);
                    }
                    """ } }
            };
            _logoLayers.Add(layer);
            _splash.AddChild(layer);
        }
        _logo = new TextureRect
        {
            Position = new Vector2(118, 148),
            Size = new Vector2(564, 212),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _splash.AddChild(_logo);
        _splash.GuiInput += input =>
        {
            if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                FinishSplash();
        };

        _opening = new OpeningMoviePlayer();
        AddChild(_opening);
        _menuMusic = new AudioStreamPlayer();
        AddChild(_menuMusic);
        _storyMusic = new AudioStreamPlayer();
        AddChild(_storyMusic);
        _fadingMusic = new AudioStreamPlayer();
        AddChild(_fadingMusic);
        _voicePlayer = new AudioStreamPlayer();
        AddChild(_voicePlayer);
        _ending = new EndingPlayer();
        AddChild(_ending);
        _ending.Finished += ShowMenu;
        BuildSystemUi();
        _opening.Failed += reason =>
        {
            ShowMenu();
            _menuMessage.Text = $"OP 播放失败：{reason}";
        };
    }

    private void AddGameButton(string label, Vector2 position, Action action)
    {
        var button = new TextureButton
        {
            Position = position, Size = new Vector2(34, 34), TooltipText = label,
            FocusMode = FocusModeEnum.None
        };
        button.Pressed += action;
        _game.AddChild(button);
        _gameButtons.Add(button);
    }

    private LineEdit AddNameEdit(Vector2 position, string hint)
    {
        var edit = new LineEdit { Position = position, Size = new Vector2(200, 49), MaxLength = 12, PlaceholderText = hint };
        edit.AddThemeFontOverride("font", _uiFont);
        edit.AddThemeFontSizeOverride("font_size", 25);
        edit.AddThemeColorOverride("font_color", new Color(0.08f, 0.17f, 0.17f));
        edit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        edit.TextSubmitted += _ => ConfirmName();
        _nameScreen.AddChild(edit);
        return edit;
    }

    private TextureButton AddNameButton(Vector2 position, string label, Action action)
    {
        var button = new TextureButton
        {
            Position = position, Size = new Vector2(129, 53),
            TooltipText = label, FocusMode = FocusModeEnum.All
        };
        button.Pressed += action;
        _nameScreen.AddChild(button);
        return button;
    }

    private TextureButton AddTitleButton(string label, int row, Action action)
    {
        var button = new TextureButton
        {
            TooltipText = label,
            Position = new Vector2(310, 377 + row * 43),
            Size = new Vector2(160, 42),
            FocusMode = FocusModeEnum.All
        };
        button.Pressed += action;
        _menu.AddChild(button);
        _titleButtons.Add(button);
        return button;
    }

    private void ShowSplash()
    {
        _menuMusic.Stop();
        _menu.Visible = false;
        _game.Visible = false;
        _nameScreen.Visible = false;
        _splash.Visible = true;
        _splashElapsed = 0;
        float[][] durations = [[2.2f,2.2f,2.2f],[2.2f,1.1f,1.65f],[2.2f,1.65f,1.1f],
            [1.1f,2.2f,1.65f],[1.65f,2.2f,1.1f],[1.1f,1.65f,2.2f],[1.65f,1.1f,2.2f],[2.2f / 3,2.2f,2.2f]];
        Array.Copy(durations[Random.Shared.Next(8)], _logoDurations, 3);
        _logoDirections = Random.Shared.Next(8);
        UpdateSplashAnimation(0);
    }

    private void UpdateSplashAnimation(float time)
    {
        // _LeafLogo:13: 88*50 + 6*100 + 2 + 80*50 = 9002 ms. The two
        // independent random choices select durations and quadratic approach paths.
        int gray = time < 4.4f ? Math.Min(255, ((int)(time / 0.05f) + 1) * 3) :
            time < 5.002f ? 255 : Math.Max(0, 255 - ((int)((time - 5.002f) / 0.05f) + 1) * 4);
        _splashGround.Color = new Color(gray / 255f, gray / 255f, gray / 255f);
        Vector2[] starts = [new((_logoDirections & 1) != 0 ? -700 : 800, 200),
            new(50, (_logoDirections & 2) != 0 ? -240 : 800),
            (_logoDirections & 4) != 0 ? new(50, 600) : new(800, -700)];
        Vector2[] controls = [new(120, 140), new(220, 220),
            (_logoDirections & 4) != 0 ? new(-80, 220) : new(170, 220)];
        Color[] colors = [new(32f / 128, 0, 111f / 128), new(0, 111f / 128, 32f / 128), new(111f / 128, 32f / 128, 0)];
        for (int i = 0; i < _logoLayers.Count; i++)
        {
            TextureRect layer = _logoLayers[i];
            float t = Mathf.Clamp((time - 2.2f) / Math.Max(0.001f, _logoDurations[i]), 0, 1);
            layer.Position = (1 - t) * (1 - t) * starts[i] + 2 * (1 - t) * t * controls[i] + t * t * _logoCompositePosition;
            layer.Visible = time >= 2.2f && time < 4.4f || i == 0 && time >= 4.4f && time < 4.7f;
            layer.Modulate = Colors.White;
            ((ShaderMaterial)layer.Material).SetShaderParameter("channel_alpha", time >= 4.4f ? Vector3.One :
                new Vector3(colors[i].R, colors[i].G, colors[i].B) * t);
        }
        _logo.Visible = time >= 4.7f && time < 5.902f;
        float phase = Mathf.Clamp((time - 4.7f) / 0.3f, 0, 4);
        float[] levels = [128,48,16,3,0];
        int part = Math.Min(3, (int)phase);
        float finalAlpha = Mathf.Lerp(levels[part], levels[part + 1], phase - part) / 128;
        // The native 50/54/55 recursive filters still require a separate pixel kernel.
        _logo.Modulate = new Color(1, 1, 1, finalAlpha);
    }

    private async void SmokeStartup()
    {
        try
        {
            _firstLaunch = true;
            ShowSplash();
            await ToSignal(GetTree().CreateTimer(9.25), SceneTreeTimer.SignalName.Timeout);
            if (_splash.Visible || !_game.Visible || _eventIndex < 0 ||
                _events[_eventIndex].Value != 3 || _picture.Texture == null)
                throw new InvalidDataException("Timed logo did not enter the first-launch prologue");
            while (_game.Visible && _eventIndex < _prologueEventCount - 1)
                SmokeAdvance();
            SmokeAdvance();
            if (!_opening.Visible || _game.Visible || _firstLaunch || !File.Exists(ProgressPath))
                throw new InvalidDataException("First-launch prologue did not enter the OP movie");
            await WaitForOpeningFrame();
            _opening.Skip();
            if (!_menu.Visible) throw new InvalidDataException("Skipping first-launch OP did not enter the menu");

            ShowSplash();
            await ToSignal(GetTree().CreateTimer(9.25), SceneTreeTimer.SignalName.Timeout);
            if (_splash.Visible || !_opening.Visible || _game.Visible)
                throw new InvalidDataException("Timed logo did not enter the OP movie on return launch");
            await WaitForOpeningFrame();
            _opening.Skip();
            if (!_menu.Visible) throw new InvalidDataException("Skipping return OP did not enter the menu");
            GD.Print("Validated timed logo and OP: first launch enters prologue; return launch enters movie and menu");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async void SmokeOpening()
    {
        try
        {
            ShowOpening();
            await WaitForOpeningFrame();
            _opening.Skip();
            if (!_menu.Visible || _opening.Visible)
                throw new InvalidDataException("OP skip did not enter the title menu");
            GD.Print("Validated original OP video frame, audio playback, and skip to title menu");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async void SmokeOpeningFull()
    {
        try
        {
            ShowOpening();
            await WaitForOpeningFrame();
            for (int i = 0; i < 200 && !_menu.Visible; i++)
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            if (!_menu.Visible || _opening.Visible || !string.IsNullOrEmpty(_menuMessage.Text))
                throw new InvalidDataException("OP did not finish cleanly at the title menu");
            GD.Print("Validated full original OP playback and automatic title menu transition");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async void SmokeOpeningSync()
    {
        try
        {
            ShowOpening();
            await WaitForOpeningFrame();
            await ToSignal(GetTree().CreateTimer(6), SceneTreeTimer.SignalName.Timeout);
            double audioTime = _opening.AudioTime;
            double videoTime = _opening.VideoTime;
            if (audioTime < 5 || videoTime < 5 || Math.Abs(audioTime - videoTime) > 0.25)
                throw new InvalidDataException($"OP desynchronized: audio={audioTime:F2}s video={videoTime:F2}s");
            _opening.Skip();
            if (!_menu.Visible) throw new InvalidDataException("Synchronized OP did not enter the menu");
            GD.Print($"Validated OP synchronization: audio={audioTime:F2}s video={videoTime:F2}s");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task WaitForOpeningFrame()
    {
        for (int i = 0; i < 100; i++)
        {
            if (_opening.HasFrame && _opening.HasAudio) return;
            if (!_opening.Visible) throw new InvalidDataException("OP playback ended before its first frame");
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        }
        throw new TimeoutException("OP video or audio did not start within 10 seconds");
    }

    private void FinishSplash()
    {
        if (!_splash.Visible) return;
        _splash.Visible = false;
        if (_firstLaunch) BeginStory();
        else ShowOpening();
    }

    private void Start()
    {
        _menu.Visible = false;
        _game.Visible = false;
        _surnameEdit.Text = _surname;
        _givenNameEdit.Text = _givenName;
        _nameConfirm.Visible = false;
        _nameScreen.Visible = true;
        _surnameEdit.GrabFocus();
    }

    private void ResetName()
    {
        _surnameEdit.Text = "木田";
        _givenNameEdit.Text = "时纪";
    }

    private void ConfirmName()
    {
        _surnameEdit.Text = string.IsNullOrWhiteSpace(_surnameEdit.Text) ? "木田" : _surnameEdit.Text.Trim();
        _givenNameEdit.Text = string.IsNullOrWhiteSpace(_givenNameEdit.Text) ? "时纪" : _givenNameEdit.Text.Trim();
        _confirmText.Text = $"{_surnameEdit.Text}{_givenNameEdit.Text}这样可以吗？";
        _nameConfirm.Visible = true;
        _confirmYes.GrabFocus();
    }

    private void AcceptName()
    {
        _surname = _surnameEdit.Text;
        _givenName = _givenNameEdit.Text;
        Directory.CreateDirectory(Path.GetDirectoryName(NamePath)!);
        File.WriteAllLines(NamePath, new[] { _surname, _givenName });
        _nameScreen.Visible = false;
        BeginStory();
    }

    private void BeginStory()
    {
        if (_events.Count == 0) return;
        ResetRoutes();
        _menuMusic.Stop();
        StopStoryMusic();
        _voicePlayer.Stop();
        _eventIndex = _firstLaunch ? -1 : _prologueEventCount - 1;
        _speakerId = 255;
        _picture.Texture = null;
        _currentBackground = -1;
        ClearCharacters();
        Array.Clear(_previousCharacterCodes);
        _menu.Visible = false;
        _nameScreen.Visible = false;
        _game.Visible = true;
        ResetMessageState();
        AdvanceScene();
    }

    private void AdvanceScene()
    {
        if (!_game.Visible) return;
        if (_eventIndex + 1 >= _events.Count) AppendRouteEvent();
        CaptureSceneTransition();
        for (int next = _eventIndex + 1; next < _events.Count || AppendRouteEvent(); next++)
        {
            if (next == _prologueEventCount && _firstLaunch)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProgressPath)!);
                File.WriteAllText(ProgressPath, "1");
                _firstLaunch = false;
                ShowOpening();
                return;
            }
            SceneEvent item = _events[next];
            _eventIndex = next;
            if (item.Kind == SceneEventKind.Choice) { ShowChoices(item); return; }
            if (item.Kind == SceneEventKind.Ending) { ShowEnding(item.Value); return; }
            if (item.Kind == SceneEventKind.Transition)
            {
                if (BeginSceneTransition(item.Value, item.Slot)) return;
                continue;
            }
            if (item.Kind == SceneEventKind.ScreenEffect)
            {
                if (BeginScreenEffect(item.Value)) return;
                continue;
            }
            if (item.Kind == SceneEventKind.PauseFrames && !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke-")))
            {
                _scriptPause = ((item.Value == 0 ? 90 : item.Value) + 1) / 60.0;
                return;
            }
            if (item.Kind == SceneEventKind.Pause && !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke-")))
            {
                _scriptPause = (item.Value == 0 ? 10 : item.Value) / 10.0;
                return;
            }
            if (item.Kind == SceneEventKind.WaitMessage)
            {
                _messageWaiting = true;
                _clearAfterMessage = item.Value != 0;
                _messageTimer = 0;
                return;
            }
            if (item.Kind == SceneEventKind.Dialogue)
            {
                ShowDialogue(item);
                return;
            }
            ApplySceneEvent(item);
            if (item.Kind == SceneEventKind.MessageLayout && BeginSceneTransition(3)) return;
        }
        _speaker.Text = "";
        if (!_baseOnlySmoke && _runtime.Finished) { ShowMenu(); return; }
        _dialogue.Text = "未完";
        _dialogue.RevealAll();
        _storyEnded = true;
    }

    private void ShowDialogue(SceneEvent item)
    {
        _speaker.Text = SpeakerName(_speakerId);
        _speakerPanel.Visible = !string.IsNullOrEmpty(_speaker.Text);
        BeginMessage(item);
        if (_preferences.InterruptVoice || item.Voice.Length > 0) _voicePlayer.Stop();
        if (!string.IsNullOrEmpty(item.Voice))
        {
            if (_voiceArchive == null) throw new InvalidOperationException("Voice archive is unavailable");
            _voicePlayer.Stream = VoiceDecoder.Decode(_voiceArchive.Read(item.Voice).Data);
            if (!_skipRunning) _voicePlayer.Play();
        }
    }

    private string SpeakerName(int speakerId)
    {
        if (speakerId == 0) return _surname;
        int nameIndex = speakerId * 2 + 1;
        return speakerId == 255 || nameIndex >= _characterStrings.Length ? "" : _characterStrings[nameIndex];
    }

    private void ApplySceneEvent(SceneEvent item)
    {
        switch (item.Kind)
        {
            case SceneEventKind.Background:
                if ((item.Flags & 1) == 0) ClearCharacters(preserveLayout: true);
                _currentBackground = item.Value;
                if (!_restoringCharacters) ShowBackground(item.Value);
                break;
            case SceneEventKind.Speaker: _speakerId = item.Value; break;
            case SceneEventKind.Music: PlayStoryMusic(item.Value, item.Slot); break;
            case SceneEventKind.ClearCharacters:
                ClearCharacters();
                _characterSlotCount = item.Value;
                break;
            case SceneEventKind.Character: ShowCharacter(item.Value, item.Slot); break;
            case SceneEventKind.HideCharacter: HideCharacter(item.Value); break;
            case SceneEventKind.TextMode: _textMode = item.Value; break;
            case SceneEventKind.MessageWindow:
                SetMessageWindow(item.Value != 0);
                if (item.Value == 0) { _speakerId = 255; _speaker.Text = ""; }
                break;
            case SceneEventKind.ClearMessage: ClearMessage(); break;
            case SceneEventKind.MessageLayout: SetMessageLayout(item.Value != 0); break;
            case SceneEventKind.Sound: PlaySound(item.Value, item.Slot != 0); break;
            case SceneEventKind.StopSound: StopSound(item.Value, item.Slot, item.Flags); break;
            case SceneEventKind.Weather: SetWeather(item.Value); break;
            case SceneEventKind.Transition:
                if (item.Value is 40 or 42 or 43) SetSceneTone(item.Value == 40 ? 0 : item.Value);
                break;
            case SceneEventKind.UnlockMemoir:
                if (!_runtime.Replay) _unlocksDirty |= _unlocks.Memoirs.Add(item.Value * 32 + item.Slot);
                break;
        }
    }

    private void StopStoryMusic()
    {
        _musicTween?.Kill();
        _musicTween = null;
        _storyMusic.Stop();
        _fadingMusic.Stop();
        _storyTrack = -1;
    }

    private void PlayStoryMusic(int track, int fadeUnits = -1)
    {
        if (track == _storyTrack) return;
        _musicTween?.Kill();
        _fadingMusic.Stop();
        _storyTrack = track;
        if (track > 0 && !_runtime.Replay) _unlocksDirty |= _unlocks.Music.Add(track);
        if (track == 0)
        {
            AudioStreamPlayer stopping = _storyMusic;
            _musicTween = CreateTween();
            _musicTween.TweenProperty(stopping, "volume_db", -80.0f, Math.Max(1, fadeUnits) * 0.01);
            _musicTween.TweenCallback(Callable.From(stopping.Stop));
            return;
        }
        if (_musicArchive == null) throw new InvalidOperationException("Music archive is unavailable");
        (_storyMusic, _fadingMusic) = (_fadingMusic, _storyMusic);
        AudioStreamPlayer fading = _fadingMusic;
        _musicTween = CreateTween();
        _musicTween.TweenProperty(fading, "volume_db", -80.0f, 0.3);
        _musicTween.TweenCallback(Callable.From(fading.Stop));
        AudioStreamWav stream = AudioDecoder.Decode(_musicArchive.Read($"{track:000}.w").Data);
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        int frameBytes = (stream.Stereo ? 2 : 1) *
            (stream.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1);
        stream.LoopEnd = stream.Data.Length / frameBytes;
        _storyMusic.Stream = stream;
        _storyMusic.VolumeDb = MusicVolumeDb;
        _storyMusic.Play();
    }

    private void ClearCharacters(bool preserveLayout = false)
    {
        // !SysDisp:1787 clears image/code slots and resets the active layout.
        // :2483 instead replaces the layout and uses each old slot's clothing in :1555.
        if (preserveLayout) Array.Clear(_previousCharacterCodes);
        else
        {
            Array.Copy(_characterCodes, _previousCharacterCodes, _characterCodes.Length);
        }
        _characterSlotCount = 0;
        for (int i = 0; i < _characters.Length; i++)
        {
            _characterCodes[i] = 0;
            _characters[i].Texture = null;
        }
    }

    private void ShowCharacter(int code, int slot)
    {
        if (code == 0) return;
        if (slot < 0)
        {
            slot = Array.FindIndex(_characterCodes,
                existing => existing != 0 && (existing & 0xf00) == (code & 0xf00));
            if (slot < 0) return;
        }
        if (slot >= _characters.Length)
            throw new NotSupportedException("More than three character layers are not supported");
        if ((code & 15) == 0)
            code |= (_characterCodes[slot] != 0 ? _characterCodes[slot] : _previousCharacterCodes[slot]) & 15;
        _characterCodes[slot] = code;
        if (_restoringCharacters) return;
        int character = (code >> 8) & 15;
        int prefixIndex = character * 2;
        if (prefixIndex >= _characterStrings.Length || string.IsNullOrEmpty(_characterStrings[prefixIndex]))
            throw new InvalidDataException($"Unknown character {character}");
        string name = $"{_characterStrings[prefixIndex]}{(code & 15) * 100 + ((code >> 4) & 15):000}.px";
        if (_characterArchive == null) throw new InvalidOperationException("Character archive is unavailable");
        UiPxFrame frame = UiPxDecoder.DecodeCharacter(_characterArchive.Read(name).Data);
        _characterCodes[slot] = code;
        _characterOrigins[slot] = frame.Origin;
        using (frame.Image)
        {
            _characters[slot].Texture = ImageTexture.CreateFromImage(frame.Image);
            _characters[slot].Size = frame.Image.GetSize();
        }
        LayoutCharacters();
    }

    private void LayoutCharacters()
    {
        int[] centers = _characterSlotCount switch
        {
            1 => [399],
            2 => [209, 589],
            _ => [150, 400, 650]
        };
        for (int i = 0; i < _characters.Length; i++)
        {
            if (_characterCodes[i] == 0 || _characters[i].Texture == null) continue;
            // A malformed/legacy scene can contain a stale slot after a one- or
            // two-person layout. Native skips that slot while rebuilding the
            // display list; never index past the selected center table.
            if (i >= centers.Length) continue;
            Vector2I origin = _characterOrigins[i];
            _characters[i].Position = new Vector2(centers[i] + origin.X, 599 + origin.Y);
        }
    }

    private void HideCharacter(int character)
    {
        if (character == 0) { ClearCharacters(); return; }
        for (int i = 0; i < _characters.Length; i++)
        {
            if ((_characterCodes[i] >> 8) != character) continue;
            _characterCodes[i] = 0;
            _characters[i].Texture = null;
        }
        int target = 0;
        for (int i = 0; i < _characters.Length; i++)
        {
            if (_characterCodes[i] == 0) continue;
            if (i != target)
            {
                _characterCodes[target] = _characterCodes[i];
                _characterOrigins[target] = _characterOrigins[i];
                _characters[target].Texture = _characters[i].Texture;
                _characters[target].Size = _characters[i].Size;
                _characterCodes[i] = 0;
                _characters[i].Texture = null;
            }
            target++;
        }
        _characterSlotCount = target;
        LayoutCharacters();
    }

    private string ImageName(int imageId)
    {
        int value = imageId & 0x7fff;
        if (value < 0x1000) return $"bg{value:000}.px";
        int character = value >> 12;
        int prefixIndex = character * 2;
        if (prefixIndex >= _characterStrings.Length || string.IsNullOrEmpty(_characterStrings[prefixIndex]))
            throw new InvalidDataException($"Unknown event-image character {character}");
        string prefix = _characterStrings[prefixIndex];
        int pose = (value >> 4) & 0xff;
        int variation = value & 0xf;
        return $"eg{prefix}{pose:00}{variation}.px";
    }

    private void ShowBackground(int imageId)
    {
        if (_backgroundArchive == null) return;
        string name = ImageName(imageId);
        var (data, kind) = _backgroundArchive.Read(name);
        using Image image = PxDecoder.DecodeBackground(data, kind);
        _picture.Texture = ImageTexture.CreateFromImage(image);
        _currentBackground = imageId;
        if ((imageId & 0x7fff) >= 0x1000 && !_runtime.Replay) _unlocksDirty |= _unlocks.Images.Add(name);
    }

    private void Save()
    {
        if (_activeMessageIndex < 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        // Dialogue ordinal remains stable as support for more script commands is added.
        int ordinal = _events.Take(_activeMessageIndex + 1).Count(item => item.Kind == SceneEventKind.Dialogue) - 1;
        File.WriteAllText(SavePath, $"v2:{ordinal}");
        _continue.Disabled = false;
    }

    private void Load()
    {
        try
        {
            string saved = File.ReadAllText(SavePath).Trim();
            bool current = saved.StartsWith("v2:");
            if (!int.TryParse(current ? saved[3..] : saved, out int target) || target < 0)
                throw new InvalidDataException("存档数据无效");
            int position = -1, index = 0;
            for (int i = 0; i < _events.Count; i++)
            {
                SceneEventKind kind = _events[i].Kind;
                bool counted = current ? kind == SceneEventKind.Dialogue :
                    kind is SceneEventKind.Background or SceneEventKind.Speaker or SceneEventKind.Dialogue;
                if (counted && index++ == target) { position = i; break; }
            }
            if (position < 0 || _events[position].Kind != SceneEventKind.Dialogue)
                throw new InvalidDataException("存档数据无效");
            RestoreScene(position);
            _menuMusic.Stop();
            _menu.Visible = false;
            _nameScreen.Visible = false;
            _game.Visible = true;
        }
        catch (Exception error)
        {
            _menuMessage.Text = error.Message;
            GD.PushError(error.ToString());
            ShowMenu();
        }
    }

    private void RestoreScene(int position)
    {
        ResetMessageState();
        _picture.Texture = null;
        _currentBackground = -1;
        _speakerId = 255;
        ClearCharacters();
        Array.Clear(_previousCharacterCodes);
        StopStoryMusic();
        int music = 0;
        int background = -1;
        string preceding = "";
        // Rebuild visual state, but do not play every historical song or voice.
        _restoringCharacters = true;
        try
        {
            for (int i = position < _prologueEventCount ? 0 : _prologueEventCount; i < position; i++)
            {
                SceneEvent item = _events[i];
                if (item.Kind == SceneEventKind.Music) music = item.Value;
                else if (item.Kind == SceneEventKind.Background)
                {
                    background = item.Value;
                    ApplySceneEvent(item);
                }
                else if (item.Kind == SceneEventKind.Dialogue)
                    preceding = (item.Flags & 2) != 0 ? "" : preceding + item.Text;
                else if (item.Kind == SceneEventKind.ClearMessage ||
                    (item.Kind == SceneEventKind.WaitMessage && item.Value != 0)) preceding = "";
                else if (item.Kind == SceneEventKind.MessageLayout) { SetMessageLayout(item.Value != 0); preceding = ""; }
                else if (item.Kind is SceneEventKind.Sound or SceneEventKind.StopSound or SceneEventKind.Pause) { }
                else ApplySceneEvent(item);
            }
        }
        finally { _restoringCharacters = false; }
        for (int i = 0; i < _characters.Length; i++)
            if (_characterCodes[i] != 0) ShowCharacter(_characterCodes[i], i);
        if (background >= 0) ShowBackground(background);
        PlayStoryMusic(music);
        _eventIndex = position;
        if (preceding.Length > 0)
        {
            _dialogue.Text = FormatMessageText(preceding);
            _dialogue.RevealAll();
            _newMessage = false;
        }
        ShowDialogue(_events[position]);
    }

    private void ShowMenu()
    {
        _weather.Stop();
        _ending.Visible = false;
        EndSceneTransition();
        CloseSystemUi();
        StopMessageModes();
        _opening.Stop();
        StopStoryMusic();
        _voicePlayer.Stop();
        foreach (AudioStreamPlayer player in _soundPlayers) player.Stop();
        _game.Visible = false;
        _splash.Visible = false;
        _nameScreen.Visible = false;
        _titleOverlay.Texture = _titleFrames.Length > 27 ? _titleFrames[27] : null;
        _menu.Visible = true;
        if (_menuMusic.Stream != null && !_menuMusic.Playing)
            _menuMusic.Play();
        if (_titleButtons.Count > 0)
            _titleButtons[0].GrabFocus();
    }

    private void ShowOpening()
    {
        _menuMusic.Stop();
        StopStoryMusic();
        _voicePlayer.Stop();
        _game.Visible = false;
        _menu.Visible = false;
        _splash.Visible = false;
        _nameScreen.Visible = false;
        _opening.Play(_openingPath, ShowMenu);
    }

    private void ValidateResources()
    {
        if (UiPxDecoder.FrameCount(_uiArchive!.Read("title.px").Data) != 28 ||
            UiPxDecoder.FrameCount(_uiArchive.Read("leaflogo.px").Data) != 2 ||
            _titleBackground.Texture?.GetSize() != new Vector2(800, 600) ||
            _logoFrames[0].GetSize() != new Vector2(500, 148) ||
            _logo.Texture?.GetSize() != new Vector2(564, 212) ||
            _titleFrameOrigins[27] != new Vector2I(-251, -25) ||
            _titleOverlay.Position != new Vector2(149, 175) ||
            _titleFrames.Skip(7).Take(20).Any(t => t.GetSize() != new Vector2(160, 42)))
            throw new InvalidDataException("Original title or logo resource layout changed");
        foreach (Texture2D texture in _logoFrames.Concat(_titleFrames.Skip(7).Take(21)))
        {
            byte[] pixels = texture.GetImage().GetData();
            bool hasVisiblePixel = false;
            for (int i = 3; i < pixels.Length; i += 4)
                hasVisiblePixel |= pixels[i] > 0;
            if (!hasVisiblePixel)
                throw new InvalidDataException("Original logo or title button decoded transparent");
        }
        foreach (string name in _backgrounds)
        {
            try
            {
                var (data, kind) = _backgroundArchive!.Read(name);
                Image image = PxDecoder.DecodeBackground(data, kind);
                if (image.GetWidth() <= 0 || image.GetHeight() <= 0)
                    throw new InvalidDataException("Invalid background dimensions");
                (byte R, byte G, byte B)? firstPixel = name switch
                {
                    "bg001.px" => (255, 254, 255),
                    "bg003.px" => (254, 249, 250),
                    "bg018.px" => (64, 69, 117),
                    "bg025.px" => (250, 238, 179),
                    _ => null
                };
                if (firstPixel is { } expected)
                {
                    byte[] pixels = image.GetData();
                    if (pixels[0] != expected.R || pixels[1] != expected.G || pixels[2] != expected.B)
                        throw new InvalidDataException("First pixel does not match the original predictor output");
                }
                if (name == "bg001.px")
                {
                    byte[] pixels = image.GetData();
                    long difference = 0;
                    long channels = 0;
                    for (int y = 1; y < image.GetHeight(); y += 3)
                    for (int x = 1; x < image.GetWidth(); x += 3)
                    {
                        int pixel = (y * image.GetWidth() + x) * 4;
                        for (int channel = 0; channel < 3; channel++)
                        {
                            difference += Math.Abs(pixels[pixel + channel] - pixels[pixel + channel - image.GetWidth() * 4]);
                            channels++;
                        }
                    }
                    if (difference / channels > 20)
                        throw new InvalidDataException("Decoded bg001 has discontinuous rows");
                }
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"Background {name}: {error.Message}", error);
            }
        }
        foreach (string name in _tracks)
            AudioDecoder.Decode(_musicArchive!.Read(name).Data);
        GD.Print($"Validated {_backgrounds.Length} backgrounds and {_tracks.Length} music tracks from original archives");
    }
}
