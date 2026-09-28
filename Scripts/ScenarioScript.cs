using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tenshi;

public enum SceneEventKind { Background, Speaker, Dialogue, Music, ClearCharacters, Character, HideCharacter,
    TextMode, MessageWindow, WaitMessage, ClearMessage, MessageLayout, Pause, PauseFrames, ScreenEffect, Weather,
    Sound, StopSound, Transition, Choice, Ending, UnlockMemoir }

public sealed record SceneEvent(SceneEventKind Kind, int Value, string Text = "", string Voice = "", int Slot = -1,
    int Flags = 3, string Source = "", string[]? Options = null);

public sealed class ScenarioScript
{
    private readonly ushort[] _code;
    private readonly string _name;
    public IReadOnlyList<string> Strings { get; }
    internal ushort[] Code => _code;

    private ScenarioScript(string name, ushort[] code, string[] strings)
    {
        _name = name;
        _code = code;
        Strings = strings;
    }

    public static ScenarioScript Read(Archive archive, string name)
    {
        byte[] bytes = archive.Read(name).Data;
        if (bytes.Length < 4)
            throw new InvalidDataException($"Script {name} is truncated");
        int wordCount = BitConverter.ToUInt16(bytes, 0);
        int table = checked(2 + wordCount * 2);
        if (table + 2 > bytes.Length)
            throw new InvalidDataException($"Script {name} code exceeds its entry");

        ushort[] code = new ushort[wordCount];
        for (int i = 0; i < code.Length; i++)
            code[i] = BitConverter.ToUInt16(bytes, 2 + i * 2);

        int count = BitConverter.ToUInt16(bytes, table);
        int stringBase = checked(table + count * 2);
        if (stringBase > bytes.Length)
            throw new InvalidDataException($"Script {name} string table exceeds its entry");
        int[] offsets = new int[count + 1];
        for (int i = 1; i < count; i++)
            offsets[i] = BitConverter.ToUInt16(bytes, table + i * 2);
        offsets[count] = bytes.Length - stringBase;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = Encoding.GetEncoding("GB18030");
        string[] strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = stringBase + offsets[i];
            int end = stringBase + offsets[i + 1];
            if (start > end || end > bytes.Length)
                throw new InvalidDataException($"Script {name} has invalid string offset {i}");
            while (end > start && (bytes[end - 1] == 0 || bytes[end - 1] == 0x7f)) end--;
            strings[i] = encoding.GetString(bytes, start, end - start);
        }
        return new ScenarioScript(name, code, strings);
    }

    public IReadOnlyList<SceneEvent> ReadLinearScene()
    {
        // This covers the straight-line function-0 scenes. Other script control
        // flow must be interpreted before it can be exposed as a playable route.
        if (_code.Length < 4 || _code[0] != 0x6e46 || _code[1] != 0)
            throw new InvalidDataException("Scene has no function-0 entry");

        var events = new List<SceneEvent>();
        var statement = new List<ushort>();
        int voiceIndex = 0;
        int depth = 0;
        for (int i = 2; i < _code.Length; i++)
        {
            ushort token = _code[i];
            if (token is 0x6923 or 0x7323 or 0x6924 or 0x7324 or 0x6d24 or 0x737e or 0x6d40 or 0x7340)
            {
                if (i + 1 >= _code.Length) throw new InvalidDataException("Script operand is truncated");
                statement.Add(token);
                statement.Add(_code[++i]);
                continue;
            }
            if (token is 0x6143 or 0x6c43)
            {
                if (i + 2 >= _code.Length) throw new InvalidDataException("Script call is truncated");
                statement.Add(token);
                statement.Add(_code[++i]);
                statement.Add(_code[++i]);
                continue;
            }
            if (token == 0x7b)
            {
                depth++;
                i++; // opening brace carries a block marker
                continue;
            }
            if (token == 0x7d)
            {
                if (--depth <= 0) break;
                continue;
            }
            if (token is 0x6649 or 0x6857 or 0x7753 or 0x6c45)
                throw new NotSupportedException("Scene contains branching script control flow");
            if (token == 0x7452) break;
            if (token == 0x203a)
            {
                AddStatement(statement, events, ref voiceIndex);
                statement.Clear();
            }
            else
                statement.Add(token);
        }
        if (events.TrueForAll(item => item.Kind != SceneEventKind.Dialogue))
            throw new InvalidDataException("Scene has no dialogue");
        return events;
    }

    internal void AddStatement(List<ushort> words, List<SceneEvent> events, ref int voiceIndex)
    {
        if (words.Count == 0) return;
        ushort opcode = words[0];
        if (opcode == 0x6143 && words.Count >= 3 && words[1] == 8 && words[2] is 1444 or 1607)
            events.Add(new SceneEvent(SceneEventKind.Weather, words[2] == 1607 ? 0 : ReadEffectFlags(words)));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 8 && words[2] == 1637)
            events.Add(new SceneEvent(SceneEventKind.PauseFrames, words.Count >= 5 ? words[4] : 90));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 8 &&
            words[2] is 4959 or 5251 or 6036 or 6200)
            events.Add(new SceneEvent(SceneEventKind.ScreenEffect, words[2]));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 8 &&
            words[2] is 4905 or 4930 or 1393)
            events.Add(new SceneEvent(SceneEventKind.MessageLayout, words[2] == 4930 ? 1 : 0));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 7 &&
            words[2] is 1945 or 1956 or 1967)
            events.Add(new SceneEvent(SceneEventKind.TextMode, words[2] == 1967 ? 0 : words[2] == 1945 ? 1 : 3));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 7 && words[2] is 1978 or 2044)
            events.Add(new SceneEvent(SceneEventKind.MessageWindow, words[2] == 2044 ? 1 : 0));
        else if (opcode is 0xfe01 or 0xfd01)
            events.Add(new SceneEvent(SceneEventKind.WaitMessage, opcode == 0xfe01 ? 1 : 0));
        else if (opcode == 0xfc01)
            events.Add(new SceneEvent(SceneEventKind.ClearMessage, 0));
        else if (opcode == 0xfa01 && words.Count >= 3 && words[1] == 0x6923)
            events.Add(new SceneEvent(SceneEventKind.Transition, words[2],
                Slot: words.Count >= 6 && words[4] == 0x6923 ? words[5] : 0));
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 7 && words[2] == 2213)
            events.Add(new SceneEvent(SceneEventKind.Pause, words.Count >= 5 ? words[4] : 10));
        else if (opcode == 0x6143 && words.Count >= 8 && words[1] == 5 && words[2] == 4 &&
            words[3] == 0x6923 && words[6] == 0x6923)
            events.Add(new SceneEvent(SceneEventKind.Sound, words[7], Slot: words[4]));
        else if (opcode == 0x6143 && words.Count >= 5 && words[1] == 5 && words[2] == 156)
            events.Add(new SceneEvent(SceneEventKind.StopSound, words[4],
                Slot: words.Count >= 8 ? words[7] : 0, Flags: words.Count >= 11 ? words[10] : 0));
        else if (opcode == 0x6143 && words.Count >= 5 && words[1] == 5 && words[2] == 675)
            events.Add(new SceneEvent(SceneEventKind.StopSound, words[4]));
        else if (opcode == 0x6143 && words.Count >= 8 && words[1] == 12 && words[2] == 604)
            events.Add(new SceneEvent(SceneEventKind.UnlockMemoir, words[4], Slot: words[7]));
        else if (opcode == 0x6143 && words.Count >= 8 && words[1] == 12 && words[2] == 733 &&
            words[3] == 0x6923 && words[4] == 1 && words[5] == 0x2c && words[6] == 0x6923)
        {
            events.Add(new SceneEvent(SceneEventKind.Music, words[7]));
        }
        else if (opcode == 0x6143 && words.Count >= 3 && words[1] == 5 && words[2] == 1150)
        {
            // !SysSound.p:1150 fades out the active music channel (10 ms units).
            int duration = words.Count >= 5 && words[3] == 0x6923 ? words[4] : 1;
            events.Add(new SceneEvent(SceneEventKind.Music, 0, Slot: duration));
        }
        else if (opcode == 0x6143 && words.Count == 11 && words[1] == 7 && words[2] == 2090 &&
            words[9] == 0x6923)
        {
            // !SysMsg.p initializes the next voiced-message number here.
            voiceIndex = words[10] - 1;
        }
        else if (opcode == 0xf801 && words.Count >= 3 && words[1] == 0x6923)
        {
            var characters = new List<int>(ReadLiteralExpressions(words, 4));
            events.Add(new SceneEvent(SceneEventKind.ClearCharacters, characters.Count));
            int slot = 0;
            foreach (int image in characters)
                events.Add(new SceneEvent(SceneEventKind.Character, image, Slot: slot++));
        }
        else if (opcode == 0xf601 && words.Count >= 3 && words[1] == 0x6923)
        {
            foreach (int image in ReadLiteralExpressions(words, 1))
                events.Add(new SceneEvent(SceneEventKind.Character, image));
        }
        else if (opcode == 0xf701 && words.Count >= 3 && words[1] == 0x6923)
        {
            events.Add(new SceneEvent(SceneEventKind.HideCharacter, words[2]));
        }
        else if (opcode == 0xf901)
        {
            int[] args;
            try { args = new List<int>(ReadLiteralExpressions(words, 1)).ToArray(); }
            catch (NotSupportedException error)
            {
                throw new NotSupportedException($"Background image expression is unsupported ({string.Join(' ', words.Select(value => value.ToString("x4")))})", error);
            }
            if (args.Length is < 1 or > 2)
                throw new NotSupportedException("Background command requires an image and optional flags");
            // !SysDisp:952 dispatches the background layer rebuild. Image bit
            // 0x8000 selects the CG variant; an optional second flag is kept for
            // the native caller's layer-preserve branch.
            events.Add(new SceneEvent(SceneEventKind.Background, args[0], Flags: args.Length == 2 ? args[1] : 0));
        }
        else if (opcode == 0xfb01)
        {
            if (words.Count < 3 || words[1] != 0x6923)
                throw new NotSupportedException($"Nonliteral scene command 0x{opcode:x4}");
            events.Add(new SceneEvent(SceneEventKind.Speaker, words[2]));
        }
        else if (opcode == 0xff01)
        {
            if (words.Count < 3 || words[1] != 0x6923)
                throw new NotSupportedException("Dialogue has no literal message ID");
            if (words.Count < 8 || words[3] != 0x2c || words[4] != 0x6923 || words[6] != 0x2c)
                throw new NotSupportedException($"Dialogue {words[2]} has no text argument: {string.Join(',', words.GetRange(0, Math.Min(words.Count, 18)).ConvertAll(value => value.ToString("x4")))}");
            var text = new StringBuilder();
            for (int i = 7; i < words.Count; i++)
            {
                if (words[i] == 0x23) continue;
                if (i + 1 >= words.Count)
                    throw new NotSupportedException($"Dialogue {words[2]} has incomplete text");
                int value = words[++i];
                if (words[i - 1] == 0x7323 && value < Strings.Count)
                    text.Append(Strings[value]);
                else if (words[i - 1] == 0x7324 && value is 59 or 60)
                    text.Append(value == 59 ? "\ue000" : "\ue001");
                else
                    throw new NotSupportedException($"Dialogue {words[2]} has unsupported text expression 0x{words[i - 1]:x4} 0x{value:x4}");
            }
            string voice = (words[5] & 0x80) != 0
                ? $"{Path.GetFileNameWithoutExtension(_name)}_{++voiceIndex:000}.g" : "";
            events.Add(new SceneEvent(SceneEventKind.Dialogue, words[2],
                text.ToString(), voice, Flags: words[5], Source: _name));
        }
    }

    private static int ReadEffectFlags(List<ushort> words)
    {
        int result = 0;
        for (int i = 3; i < words.Count; i++)
        {
            if (words[i] is 0x28 or 0x29 or 0x2b) continue;
            if (words[i] != 0x6923 || i + 1 == words.Count)
                throw new NotSupportedException("Nonliteral particle parameters");
            result += words[++i];
        }
        return result;
    }

    private static IEnumerable<int> ReadLiteralExpressions(List<ushort> words, int index)
    {
        while (index < words.Count)
        {
            if (index + 1 >= words.Count || (words[index] == 0x6c23 && index + 2 >= words.Count) ||
                (words[index] != 0x6923 && words[index] != 0x6c23))
                throw new NotSupportedException("Character image has a nonliteral identifier");
            bool wide = words[index] == 0x6c23;
            int value = wide
                ? words[index + 1] | (words[index + 2] << 16)
                : words[index + 1];
            index += wide ? 3 : 2;
            while (index < words.Count && words[index] != 0x2c)
            {
                if (index + 2 >= words.Count || (words[index + 1] == 0x6c23 && index + 3 >= words.Count) ||
                    (words[index + 1] != 0x6923 && words[index + 1] != 0x6c23) ||
                    words[index] is not (0x2b or 0x2d))
                    throw new NotSupportedException("Character image expression is unsupported");
                int operand = words[index + 1] == 0x6c23
                    ? words[index + 2] | (words[index + 3] << 16)
                    : words[index + 2];
                value += words[index] == 0x2b ? operand : -operand;
                index += words[index + 1] == 0x6c23 ? 4 : 3;
            }
            yield return value;
            if (index < words.Count) index++;
        }
    }
}
