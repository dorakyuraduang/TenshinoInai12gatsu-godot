using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tenshi;

// Scenario-only VM. Native system modules are implemented by the Godot front end.
// Control-flow layout follows Script_ExecuteStatement and Script_Transfer (IDA).
public sealed class ScenarioRuntime
{
    private sealed record Frame(int Module, int Ip, bool Switch);
    private readonly Archive _archive;
    private readonly string[] _modules;
    private readonly Dictionary<int, ScenarioScript> _scripts = new();
    private readonly Dictionary<int, int> _variables = new();
    private readonly Stack<Frame> _stack = new();
    private readonly Queue<SceneEvent> _pending = new();
    private readonly Dictionary<int, int> _voices = new();
    public Dictionary<string, int> Unsupported { get; } = new();
    public List<int> Decisions { get; } = new();
    public bool WaitingForChoice { get; private set; }
    public bool Finished { get; private set; }
    public bool Replay { get; set; }
    private int _module = 40, _ip;
    public string ModuleName => _modules[_module];

    public ScenarioRuntime(Archive archive, int module = 40)
    {
        _archive = archive;
        _module = module;
        byte[] info = PxDecoder.DecodeLzss(archive.Read("projectInfo").Data);
        if ((info.Length - 68) % 28 != 0) throw new InvalidDataException("Invalid original module table");
        _modules = new string[1 + (info.Length - 68) / 28];
        _modules[0] = "";
        for (int i = 1; i < _modules.Length; i++)
        {
            int start = 68 + (i - 1) * 28, length = 0;
            while (length < 28 && info[start + length] != 0) length++;
            _modules[i] = Encoding.ASCII.GetString(info, start, length) + ".p";
        }
    }

    private ScenarioScript Script => GetScript(_module);
    private ScenarioScript GetScript(int module)
    {
        if (!_scripts.TryGetValue(module, out ScenarioScript? script))
            _scripts[module] = script = ScenarioScript.Read(_archive, _modules[module]);
        return script;
    }

    public void Choose(int option)
    {
        if (!WaitingForChoice || option < 1 || option > _choiceCount) throw new InvalidOperationException("Invalid scenario choice");
        _variables[218] = option;
        Decisions.Add(option);
        WaitingForChoice = false;
    }
    private int _choiceCount;

    public string StateKey => $"{_module}:{_ip}:" + string.Join(',', _variables.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}")) +
        ":" + string.Join(',', _stack.Select(f => $"{f.Module}/{f.Ip}/{f.Switch}"));

    public ScenarioRuntime Fork()
    {
        var copy = new ScenarioRuntime(_archive) { _module = _module, _ip = _ip, _choiceCount = _choiceCount,
            WaitingForChoice = WaitingForChoice, Finished = Finished, Replay = Replay };
        foreach (var pair in _variables) copy._variables.Add(pair.Key, pair.Value);
        foreach (var pair in _voices) copy._voices.Add(pair.Key, pair.Value);
        foreach (var pair in _scripts) copy._scripts.Add(pair.Key, pair.Value);
        foreach (Frame frame in _stack.Reverse()) copy._stack.Push(frame);
        foreach (SceneEvent item in _pending) copy._pending.Enqueue(item);
        copy.Decisions.AddRange(Decisions);
        return copy;
    }

    public SceneEvent? Next()
    {
        if (_pending.Count > 0) return _pending.Dequeue();
        if (Finished || WaitingForChoice) return null;
        for (int guard = 0; guard < 100000; guard++)
        {
            ushort[] code = Script.Code;
            if (_ip >= code.Length) { Return(); if (Finished) return null; continue; }
            int start = _ip;
            ushort op = code[_ip++];
            switch (op)
            {
                case 0x6e46: _ip++; break; // Fn + local count
                case 0x007b: _ip++; break; // { + end metadata
                case 0x007d: _ip++; break; // } + start metadata
                case 0x203a: break;
                case 0x7452:
                    Return();
                    if (Finished) return null;
                    break;
                case 0x7254: // Tr: tail call; unwind a switch frame, preserve caller.
                {
                    int module = code[_ip++], entry = code[_ip++];
                    while (_stack.Count > 0 && _stack.Peek().Switch) _stack.Pop();
                    _module = module; _ip = entry;
                    break;
                }
                case 0x7342: // Bs: end the selected switch arm.
                    _ip++;
                    if (_stack.Count == 0 || !_stack.Peek().Switch) throw Error(start, "Switch return without frame");
                    Pop();
                    break;
                case 0x7753:
                {
                    int table = code[_ip++], value = Evaluate(ReadWords());
                    if (table >= code.Length || code[table] != 0x7345) throw Error(start, "Invalid switch table");
                    int count = code[table + 1], target = -1, fallback = -1;
                    for (int i = 0; i < count; i++)
                    {
                        int ip = code[table + 2 + i * 2], match = code[table + 3 + i * 2];
                        if (ip == 0) fallback = match;
                        else if (match == value) target = ip;
                    }
                    _ip = table + 2 + count * 2;
                    if (target < 0) target = fallback;
                    if (target >= 0) { _stack.Push(new Frame(_module, _ip, true)); _ip = target; }
                    break;
                }
                case 0x6649:
                {
                    bool condition = Evaluate(ReadWords()) != 0;
                    int block = _ip;
                    int end = SkipAt(code, block);
                    if (!condition) { _ip = end; if (_ip < code.Length && code[_ip] == 0x6c45) _ip++; }
                    break;
                }
                case 0x6c45: _ip = SkipAt(code, _ip); break;
                case 0x6143:
                {
                    int module = code[_ip++], entry = code[_ip++];
                    List<ushort> args = ReadWords();
                    if (module >= 21 && !_modules[module].StartsWith("~"))
                    {
                        _stack.Push(new Frame(_module, _ip, false));
                        _module = module; _ip = entry;
                    }
                    else if (module == 13 && entry == 13767)
                    {
                        string[] options = ParseStrings(args);
                        _choiceCount = options.Length;
                        WaitingForChoice = true;
                        return new SceneEvent(SceneEventKind.Choice, start, Source: ModuleName, Options: options);
                    }
                    else if (module == 20 && entry == 1188)
                        return new SceneEvent(SceneEventKind.Ending, Evaluate(args), Source: ModuleName);
                    else
                    {
                        if (Replay && module == 8 && entry == 1426) { Finished = true; return null; }
                        var statement = new List<ushort> { op, (ushort)module, (ushort)entry };
                        statement.AddRange(args);
                        EmitStatement(statement);
                    }
                    break;
                }
                case 0x6924:
                {
                    int variable = code[_ip++]; ushort operation = code[_ip++];
                    int value = Evaluate(ReadWords()), previous = _variables.GetValueOrDefault(variable);
                    _variables[variable] = operation switch
                    {
                        0x003d => value, 0x3d2b => previous + value, 0x3d2d => previous - value,
                        0x3d7c => previous | value, 0x3d26 => previous & value,
                        0x2b2b => previous + 1, 0x2d2d => previous - 1,
                        _ => throw Error(start, $"Unsupported assignment {operation:x4}")
                    };
                    break;
                }
                case 0x7345: _ip += code[_ip] * 2 + 1; break;
                case 0x7953:
                {
                    List<ushort> system = ReadWords();
                    if (Evaluate(system) == 0x051b6eef)
                    {
                        Finished = true; // Original returns to the title task.
                        return null;
                    }
                    throw Error(start, "Unsupported scenario system command");
                }
                default:
                    if ((op & 255) == 1)
                    {
                        List<ushort> words = ReadWords();
                        words.Insert(0, op);
                        EmitStatement(words);
                    }
                    else throw Error(start, $"Unsupported scenario instruction {op:x4}");
                    break;
            }
            if (_pending.Count > 0) return _pending.Dequeue();
        }
        throw Error(_ip, "Scenario instruction limit reached");
    }

    private InvalidDataException Error(int ip, string message) => new($"{ModuleName}:{ip}: {message}");
    private void Return()
    {
        while (_stack.Count > 0 && _stack.Peek().Switch) _stack.Pop();
        if (_stack.Count == 0) Finished = true;
        else Pop();
    }
    private void Pop() { Frame frame = _stack.Pop(); _module = frame.Module; _ip = frame.Ip; }

    private List<ushort> ReadWords()
    {
        ushort[] code = Script.Code;
        var words = new List<ushort>();
        while (_ip < code.Length && code[_ip] != 0x203a)
        {
            ushort token = code[_ip++]; words.Add(token);
            int operands = token switch
            {
                0x6923 or 0x7323 or 0x6924 or 0x7324 or 0x6d24 or 0x737e or 0x6d40 or 0x7340 => 1,
                0x6c23 or 0x6143 or 0x6643 or 0x6c43 => 2,
                _ => 0
            };
            for (int i = 0; i < operands; i++) words.Add(code[_ip++]);
        }
        if (_ip < code.Length) _ip++;
        return words;
    }

    private int SkipAt(ushort[] code, int ip)
    {
        if (code[ip] == 0x7b) return code[ip + 1] + 2;
        int saved = _ip; _ip = ip; ReadWords(); int end = _ip; _ip = saved;
        return end;
    }

    private int Evaluate(List<ushort> words)
    {
        int p = 0;
        int Atom()
        {
            if (p >= words.Count) return 0;
            ushort token = words[p++];
            return token switch
            {
                0x6923 => words[p++], 0x6924 => _variables.GetValueOrDefault(words[p++]),
                0x6c23 => words[p++] | words[p++] << 16,
                0x21 => Atom() == 0 ? 1 : 0, 0x7e => ~Atom(), 0x2d => -Atom(),
                0x28 => Parenthesis(), _ => throw Error(_ip, $"Unsupported expression token {token:x4}")
            };
        }
        int Parenthesis() { int v = Expression(0); if (p >= words.Count || words[p++] != 0x29) throw Error(_ip, "Unclosed expression"); return v; }
        int Priority(ushort op) => op switch
        {
            0x7c7c => 1, 0x2626 => 2, 0x7c => 3, 0x5e => 4, 0x26 => 5,
            0x3d3d or 0x3d21 => 6, 0x3e or 0x3c or 0x3d3e or 0x3d3c => 7,
            0x3c3c or 0x3e3e => 8, 0x2b or 0x2d => 9, 0x2a or 0x2f or 0x25 => 10, _ => -1
        };
        int Expression(int minimum)
        {
            int left = Atom();
            while (p < words.Count && Priority(words[p]) >= minimum)
            {
                ushort op = words[p++]; int right = Expression(Priority(op) + 1);
                left = op switch
                {
                    0x7c7c => left != 0 || right != 0 ? 1 : 0, 0x2626 => left != 0 && right != 0 ? 1 : 0,
                    0x7c => left | right, 0x26 => left & right, 0x5e => left ^ right,
                    0x3d3d => left == right ? 1 : 0, 0x3d21 => left != right ? 1 : 0,
                    0x3e => left > right ? 1 : 0, 0x3c => left < right ? 1 : 0,
                    0x3d3e => left >= right ? 1 : 0, 0x3d3c => left <= right ? 1 : 0,
                    0x3c3c => left << right, 0x3e3e => left >> right,
                    0x2b => left + right, 0x2d => left - right, 0x2a => left * right,
                    0x2f => left / right, 0x25 => left % right, _ => left
                };
            }
            return left;
        }
        int result = Expression(0);
        if (p != words.Count) throw Error(_ip, "Trailing expression operands");
        return result;
    }

    private string[] ParseStrings(List<ushort> words)
    {
        var result = new List<string>(); var text = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            ushort token = words[i];
            if (token == 0x2c) { result.Add(text.ToString()); text.Clear(); }
            else if (token == 0x23) continue;
            else if (token == 0x7323) text.Append(Script.Strings[words[++i]]);
            else if (token == 0x7324) text.Append(words[++i] == 59 ? "\ue000" : "\ue001");
            else throw Error(_ip, "Unsupported choice text");
        }
        result.Add(text.ToString());
        return result.ToArray();
    }

    private void EmitStatement(List<ushort> words)
    {
        int voice = _voices.GetValueOrDefault(_module);
        var events = new List<SceneEvent>();
        Script.AddStatement(words, events, ref voice);
        _voices[_module] = voice;
        foreach (SceneEvent item in events) _pending.Enqueue(item);
        if (events.Count == 0 && words[0] == 0x6143 && !(words[1] == 7 && words[2] == 2090))
        {
            string key = $"{_modules[words[1]]}:{words[2]}";
            Unsupported[key] = Unsupported.GetValueOrDefault(key) + 1;
        }
    }
}
