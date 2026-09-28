using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private void SmokeRoutes(Archive scripts)
    {
        try
        {
            var queue = new Queue<(ScenarioRuntime Runtime, bool Full, string Previous)>();
            queue.Enqueue((new ScenarioRuntime(scripts), false, ""));
            var probe = new MessageView();
            AddChild(probe);
            int maxPages = 0;
            var extraPages = new SortedSet<string>();
            var states = new HashSet<string>();
            var messages = new HashSet<string>();
            var voices = new HashSet<string>();
            var choices = new HashSet<string>();
            var endings = new HashSet<int>();
            var unsupported = new SortedSet<string>();
            var missingSounds = new SortedSet<int>();
            var pendingTransitions = new SortedSet<int>();
            int completed = 0, executed = 0;
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                ScenarioRuntime runtime = state.Runtime;
                bool full = state.Full;
                string previous = state.Previous;
                probe.ConfigureLayout(full);
                while (runtime.Next() is { } item)
                {
                    if (++executed > 1000000) throw new InvalidDataException("Route coverage exceeded one million events");
                    if (item.Kind == SceneEventKind.Dialogue)
                    {
                        probe.SetMessage(FormatMessageText(previous + item.Text).TrimStart('\u007f'));
                        maxPages = Math.Max(maxPages, probe.PageCount);
                        if (probe.PageCount > 1) extraPages.Add($"{item.Source}:{item.Value} full={full} chars={probe.Text.Length}");
                        foreach (var page in probe.Pages)
                        foreach (MessageView.Glyph glyph in page)
                            if (glyph.Position.X < 0 || glyph.Position.X + glyph.Advance > probe.Size.X ||
                                glyph.Position.Y > probe.Size.Y)
                                throw new InvalidDataException($"Route text overflow at {item.Source}:{item.Value} glyph={glyph.Text} x={glyph.Position.X} advance={glyph.Advance} y={glyph.Position.Y} size={probe.Size}");
                        previous = (item.Flags & 2) != 0 ? "" : previous + item.Text;
                        messages.Add($"{item.Source}:{item.Value}");
                        if (item.Voice.Length > 0)
                        {
                            voices.Add(item.Voice);
                            if (!_voiceArchive!.Contains(item.Voice)) throw new InvalidDataException($"Missing route voice {item.Voice}");
                        }
                    }
                    if (item.Kind == SceneEventKind.MessageLayout)
                    { full = item.Value != 0; previous = ""; probe.ConfigureLayout(full); }
                    if (item.Kind == SceneEventKind.ClearMessage ||
                        (item.Kind == SceneEventKind.WaitMessage && item.Value != 0)) previous = "";
                    if (item.Kind == SceneEventKind.Background && !_backgroundArchive!.Contains(ImageName(item.Value)))
                        throw new InvalidDataException($"Missing route image {ImageName(item.Value)}");
                    if (item.Kind == SceneEventKind.Sound && !_soundArchive!.Contains($"se{item.Value:000}.w"))
                        missingSounds.Add(item.Value);
                    if (item.Kind == SceneEventKind.Transition && item.Value is not (1 or 2 or 3 or 30 or 31 or 32 or 33 or 34 or 35 or 40 or 42 or 43))
                        pendingTransitions.Add(item.Value);
                    if (item.Kind == SceneEventKind.Choice)
                    {
                        choices.Add($"{item.Source}:{item.Value}");
                        if (states.Add(runtime.StateKey))
                            for (int i = 1; i <= item.Options!.Length; i++)
                            {
                                ScenarioRuntime branch = runtime.Fork();
                                branch.Choose(i);
                                queue.Enqueue((branch, full, previous));
                            }
                    }
                    if (item.Kind == SceneEventKind.Ending) endings.Add(item.Value);
                }
                foreach (string key in runtime.Unsupported.Keys) unsupported.Add(key);
                if (runtime.Finished) completed++;
            }
            File.WriteAllText(DataPath("route-coverage.json"), JsonSerializer.Serialize(new {
                Messages = messages.Count, Voices = voices.Count, Choices = choices.Count,
                Endings = endings.OrderBy(v => v), CompletedPaths = completed, ExecutedEvents = executed,
                MaximumTextPages = maxPages, ExtraPages = extraPages, UnsupportedSystemCalls = unsupported
                , MissingOriginalSounds = missingSounds, PendingTransitionEffects = pendingTransitions
            }, new JsonSerializerOptions { WriteIndented = true }));
            probe.QueueFree();
            GD.Print($"Route VM coverage: {messages.Count} messages, {voices.Count} voice references, {choices.Count} choices, {completed} completed paths, endings {string.Join(',', endings.OrderBy(v => v))}, max text pages {maxPages}; pending system calls: {string.Join(',', unsupported)}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
