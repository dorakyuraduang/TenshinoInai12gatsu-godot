using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private Archive _scripts = null!;
    private SceneEvent[] _baseEvents = [];
    private List<SceneEvent> _routeEvents = new();
    private ScenarioRuntime _runtime = null!;
    private Control _choicesLayer = null!;
    private readonly List<TextureButton> _choiceButtons = new();
    private bool _baseOnlySmoke => OS.GetCmdlineUserArgs().Any(arg =>
        arg is "--smoke-media" or "--smoke-game" or "--smoke-startup");

    private void ResetRoutes()
    {
        _runtime = new ScenarioRuntime(_scripts);
        _routeEvents = new List<SceneEvent>(_baseEvents);
        _events = _routeEvents;
        _choicesLayer.Visible = false;
    }

    private bool AppendRouteEvent()
    {
        if (_baseOnlySmoke || _runtime == null) return false;
        int before = _routeEvents.Count;
        while (_runtime.Next() is { } item)
        {
            _routeEvents.Add(item);
            if (item.Kind is SceneEventKind.Dialogue or SceneEventKind.Choice or SceneEventKind.Ending) break;
        }
        return _routeEvents.Count > before;
    }

    private void ShowChoices(SceneEvent item)
    {
        StopMessageModes();
        _voicePlayer.Stop();
        _waitCursor.Visible = false;
        foreach (Node child in _choicesLayer.GetChildren()) { _choicesLayer.RemoveChild(child); child.QueueFree(); }
        _choiceButtons.Clear();
        _choicesLayer.Visible = true;
        string[] options = item.Options ?? [];
        for (int i = 0; i < options.Length; i++)
        {
            int option = i + 1;
            var button = new TextureButton
            {
                TextureNormal = _commonFrames[31], TextureHover = _commonFrames[32],
                TextureFocused = _commonFrames[32], TexturePressed = _commonFrames[32],
                Position = new Vector2(0, 240 + 35 * i), FocusMode = FocusModeEnum.All
            };
            _choicesLayer.AddChild(button);
            Label label = UiLabel(button, FormatMessageText(options[i]), 0, 6, 800, 30, 24);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            button.Pressed += () => SelectChoice(option);
            _choiceButtons.Add(button);
        }
        if (_choiceButtons.Count > 0) _choiceButtons[0].GrabFocus();
    }

    private void SelectChoice(int option)
    {
        if (!_choicesLayer.Visible || !_runtime.WaitingForChoice) return;
        _runtime.Choose(option);
        _choicesLayer.Visible = false;
        AdvanceScene();
    }

    private int FindSavedRouteMessage(SaveRecord record)
    {
        var candidateRuntime = new ScenarioRuntime(_scripts);
        var candidateEvents = new List<SceneEvent>(_baseEvents);
        int decision = 0, scanned = 0, ordinal = 0;
        for (int guard = 0; guard < 100000; guard++)
        {
            int found = -1;
            for (; scanned < candidateEvents.Count; scanned++)
                if (candidateEvents[scanned].Kind == SceneEventKind.Dialogue && ordinal++ == record.DialogueOrdinal &&
                    candidateEvents[scanned].Source == record.Source && candidateEvents[scanned].Value == record.MessageId)
                { found = scanned; break; }
            if (found >= 0)
            {
                _runtime = candidateRuntime;
                _routeEvents = candidateEvents;
                _events = _routeEvents;
                _choicesLayer.Visible = false;
                return found;
            }
            if (candidateRuntime.WaitingForChoice)
            {
                if (decision >= record.Decisions.Length) return -1;
                candidateRuntime.Choose(record.Decisions[decision++]);
            }
            SceneEvent? next = candidateRuntime.Next();
            if (next == null) return -1;
            candidateEvents.Add(next);
        }
        throw new InvalidOperationException("Saved route exceeded the script replay limit");
    }
}
