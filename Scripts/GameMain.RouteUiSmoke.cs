using System;
using System.IO;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private void SmokeRouteUi()
    {
        try
        {
            _firstLaunch = false;
            BeginStory();
            _eventIndex = _baseEvents.Length - 1;
            AdvanceScene();
            for (int i = 0; i < 100 && !_choicesLayer.Visible; i++) SmokeAdvance();
            if (!_choicesLayer.Visible || _choiceButtons.Count != 3 || !_runtime.WaitingForChoice)
                throw new InvalidDataException("First native route choice was not reached");
            for (int branch = 1; branch <= 3; branch++)
            {
                if (branch > 1)
                {
                    ResetRoutes();
                    _eventIndex = _baseEvents.Length - 1;
                    _game.Visible = true;
                    AdvanceScene();
                    for (int i = 0; i < 100 && !_choicesLayer.Visible; i++) SmokeAdvance();
                }
                SelectChoice(branch);
                string expected = branch switch { 1 => "toh_0140.p", 2 => "yuk_0140.p", _ => "mah_0140.p" };
                if (_activeMessage?.Source != expected)
                    throw new InvalidDataException($"Choice {branch} reached {_activeMessage?.Source}, expected {expected}");
                for (int i = 0; i < 4; i++) SmokeAdvance();
                int index = _activeMessageIndex;
                string text = _dialogue.Text;
                WriteSlot(38);
                SmokeAdvance();
                LoadSlot(38);
                if (_activeMessageIndex != index || _dialogue.Text != text || !_runtime.Decisions.SequenceEqual([branch]))
                    throw new InvalidDataException("Branched save did not restore the original route");
            }
            for (int ending = 1; ending <= 5; ending++)
            {
                ShowEnding(ending);
                if (_ending.Credits.Length < 85 || _ending.Strip == null || _ending.Strip.GetHeight() < 5940 ||
                    !_storyMusic.Playing || !_ending.Visible)
                    throw new InvalidDataException($"Original ending {ending} was not initialized");
                _ending._Process(30);
                if (_ending.Intro.Visible) throw new InvalidDataException("Ending did not advance from prologue to credits");
                _ending._Process(244);
                if (!_menu.Visible || _ending.Visible) throw new InvalidDataException("Ending did not return to title");
            }
            OpenGallery();
            for (int page = 0; page < 5; page++) { _galleryPage = page; OpenCgGallery(); }
            OpenMemoirs();
            OpenMusicGallery();
            OpenBonusVoices();
            CloseSystemUi();
            foreach (int flags in new[] { 1, 2, 3, 9, 18, 26, 35, 43, 52 })
            {
                _preferences.Effects = true;
                _preferences.Particles = true;
                SetWeather(flags);
                _weather.Tick(20);
                if (_weather.Textures.Length < 40 || _weather.ActiveParticles < 1 || _weather.Flags != flags)
                    throw new InvalidDataException($"Original particle mode {flags} did not run");
                SetWeather(0);
                if (_weather.Visible) throw new InvalidDataException("Particle stop did not remove the layer");
            }
            int characters = 0;
            foreach (string name in _characterArchive!.Names.Where(n => n.EndsWith(".px")))
            {
                UiPxFrame frame = UiPxDecoder.DecodeCharacter(_characterArchive.Read(name).Data);
                using (frame.Image)
                    if (frame.Image.GetWidth() < 1 || frame.Image.GetHeight() < 1)
                        throw new InvalidDataException($"Invalid original character {name}");
                characters++;
            }
            GD.Print($"Validated original three-way choice, route dispatch, branched save/load, five credit strips/music/timed title returns, all gallery atlases, snow particle lifecycles and {characters} character resources");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
