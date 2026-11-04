using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// One bounded JSONL stream per process/session. No gameplay data, RNG or network
// messages are modified. All I/O failures stay inside the diagnostics boundary.
internal sealed class DiagnosticSink(string directory, string session, long limit = 2 * 1024 * 1024, int retained = 12)
{
    private readonly object _gate = new();
    private int _part;
    private long _bytes;
    internal bool Disabled { get; private set; }
    internal bool Write(object record)
    {
        lock (_gate)
        {
            if (Disabled) return false;
            try
            {
                Directory.CreateDirectory(directory);
                string line = JsonSerializer.Serialize(record) + "\n";
                int length = Encoding.UTF8.GetByteCount(line);
                if (_bytes > 0 && _bytes + length > limit) { _part++; _bytes = 0; }
                string path = Path.GetFullPath(Path.Combine(directory, $"pc-{session}-{_part:D3}.jsonl"));
                bool opened = _bytes == 0;
                File.AppendAllText(path, line, new UTF8Encoding(false));
                _bytes += length;
                // Only our own flat log files. Never recurse or touch game saves.
                if (opened) foreach (var stale in new DirectoryInfo(directory).GetFiles("pc-*.jsonl")
                    .Where(f => f.FullName != path).OrderByDescending(f => f.LastWriteTimeUtc).Skip(Math.Max(0, retained - 1)))
                    stale.Delete();
                return true;
            }
            catch { Disabled = true; return false; }
        }
    }
}

internal static class Diagnostics
{
    internal static readonly string Session = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
    internal static string DirectoryPath { get; private set; } = "";
    private static DiagnosticSink? _sink;
    private static long _sequence;
    private static int _run;
    private static bool _warned;
    private static readonly object Gate = new();
    private static object _context = new { mode = "menu", role = "none" };
    private static RunState? _state;
    private static readonly ConditionalWeakTable<GameAction, Stopwatch> Timers = new();
    private static readonly Dictionary<string, (DateTime At, int Count)> Repeats = [];
    private static string Clip(string value, int max = 8192) => value.Length > max ? value[..max] + "…[truncated]" : value;
    internal static void Initialize()
    {
        try
        {
            DirectoryPath = ProjectSettings.GlobalizePath("user://PunchConstruct/logs");
            _sink = new DiagnosticSink(DirectoryPath, Session);
            var assembly = typeof(ModEntry).Assembly;
            Write("info", "session.start", new {
                mod = assembly.GetName().Version?.ToString(),
                dllSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                game = typeof(RunManager).Assembly.GetName().Version?.ToString(), baseline = "0.111.0",
                baseLib = typeof(BaseLib.Abstracts.CustomRelicModel).Assembly.GetName().Version?.ToString(),
                qa = OS.GetCmdlineArgs().Any(a => a.StartsWith("--punchconstruct-")),
                schema = 1, maxFileBytes = 2 * 1024 * 1024, retainedFiles = 12 });
            Log.LogCallback += NativeLog;
            RunManager.Instance.RoomEntered += () => RoomChanged(RunManager.Instance);
            CombatManager.Instance.CombatBegan += _ => Observe(() => { Refresh(RunManager.Instance); Write("info", "combat.begin", Summary()); });
            CombatManager.Instance.CombatEnded += _ => Observe(() => { Refresh(RunManager.Instance); Write("info", "combat.end", Summary()); });
            CombatManager.Instance.TurnStarted += _ => Observe(() => { Refresh(RunManager.Instance); Write("info", "turn.begin", Summary()); });
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Error("runtime.unhandled", args.ExceptionObject as Exception);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (Gate) { FlushRepeats(); Write("info", "session.end"); } };
            GD.Print("[PunchConstruct LOG] " + DirectoryPath);
        }
        catch (Exception ex) { Fallback(ex.GetType().Name); }
    }
    private static void Fallback(string reason)
    {
        if (_warned) return;
        _warned = true;
        GD.Print("[PunchConstruct LOG UNAVAILABLE] " + reason + "; native game log remains available");
    }
    internal static void Write(string level, string name, object? data = null)
    {
        lock (Gate)
        {
            try
            {
                if (_sink != null && !_sink.Write(new { schema = 1, utc = DateTime.UtcNow.ToString("O"),
                    seq = ++_sequence, session = Session, run = _run, level, @event = name, context = _context, data }))
                    Fallback("write failed");
            }
            catch (Exception ex) { Fallback(ex.GetType().Name); }
        }
    }
    internal static void Error(string name, Exception? error, object? data = null)
        => Write("error", name, new { details = data, exception = error?.GetType().FullName,
            message = error == null ? null : Clip(error.Message), stack = error == null ? null : Clip(error.ToString(), 24576) });
    private static void NativeLog(LogLevel level, string text, int _)
    {
        if (level is not (LogLevel.Warn or LogLevel.Error)) return;
        // Native diagnostics are observations, not an attribution to this mod.
        lock (Gate)
        {
            string key = level + ":" + Clip(text);
            if (Repeats.TryGetValue(key, out var previous) && (DateTime.UtcNow - previous.At).TotalSeconds < 5)
            {
                Repeats[key] = (previous.At, previous.Count + 1);
                return;
            }
            if (Repeats.Count >= 128) { FlushRepeats(); previous = default; }
            if (previous.Count > 0) Write("info", "native.repeated", new { message = Clip(LogSanitizer.Sanitize(text)), suppressed = previous.Count });
            Repeats[key] = (DateTime.UtcNow, 0);
            Write(level == LogLevel.Error ? "error" : "warn", "native.message", new { message = Clip(LogSanitizer.Sanitize(text)) });
        }
    }
    private static void FlushRepeats()
    {
        foreach (var entry in Repeats.Where(e => e.Value.Count > 0))
            Write("info", "native.repeated", new { message = Clip(LogSanitizer.Sanitize(entry.Key)), suppressed = entry.Value.Count });
        Repeats.Clear();
    }
    internal static int Slot(ulong owner) => _state?.Players.ToList().FindIndex(p => p.NetId == owner) ?? -1;
    internal static object ActionData(GameAction action) => new {
        actionId = action.Id, action = action.GetType().Name, owner = Slot(action.OwnerId), state = action.State.ToString(),
        card = (action as PlayCardAction)?.CardModelId.ToString(), target = (action as PlayCardAction)?.TargetId };
    private static void Refresh(RunManager manager)
    {
        var state = manager.DebugOnlyGetState();
        _state = state;
        if (state == null) return;
        var net = manager.NetService;
        _context = new { mode = net.Type is NetGameType.Host or NetGameType.Client ? "multiplayer" : net.Type.ToString().ToLowerInvariant(),
            role = net.Type.ToString().ToLowerInvariant(), local = Slot(net.NetId), players = state.Players.Count,
            floor = state.TotalFloor, room = state.CurrentRoom?.RoomType.ToString(), roomId = state.CurrentRoom?.Id,
            model = state.CurrentRoom?.ModelId?.ToString(),
            turns = state.Players.Select(p => p.PlayerCombatState?.TurnNumber).ToArray() };
    }
    internal static void Observe(Action action)
    {
        try { action(); } catch (Exception ex) { Error("diagnostics.observer_failed", ex); }
    }
    internal static void Attach(RunManager manager)
    {
        _run++;
        Refresh(manager);
        Write("info", "run.setup", new { seed = _state?.Rng.StringSeed, gameMode = _state?.GameMode.ToString(),
            modifiers = _state?.Modifiers.Select(m => new { id = m.Id.ToString(), character = (m as MegaCrit.Sts2.Core.Models.Modifiers.CharacterCards)?.CharacterModel.ToString() }).ToArray(),
            roster = _state?.Players.Select((p, i) => new { slot = i, character = p.Character.Id.ToString() }).ToArray() });
        manager.ActionExecutor.BeforeActionExecuted += a => Observe(() => {
            Timers.Remove(a); Timers.Add(a, Stopwatch.StartNew());
            Refresh(manager); Write("info", "action.begin", ActionData(a));
        });
        manager.ActionExecutor.AfterActionExecuted += a => Observe(() => {
            if (a.Exception != null) Error("action.failed", a.Exception, ActionData(a));
            else Write("info", "action.end", new { action = ActionData(a), elapsedMs = Timers.TryGetValue(a, out var timer) ? timer.ElapsedMilliseconds : (long?)null });
            Timers.Remove(a);
        });
        manager.PlayerChoiceSynchronizer.PlayerChoiceReceived += (p, id, _) => Observe(() => Write("info", "choice.received", new { player = Slot(p.NetId), choiceId = id }));
        manager.ChecksumTracker.StateDiverged += (peer, _) => Observe(() => {
            Refresh(manager); Write("error", "network.divergence", new { remote = Slot(peer), snapshot = Summary() });
        });
        manager.NetService.Disconnected += info => Observe(() => Write("warn", "network.disconnected", new { reason = info.GetReason().ToString(), info.SelfInitiated }));
    }
    internal static object? Summary() => _state?.Players.Select((p, i) => new { slot = i, p.Creature.CurrentHp, p.Creature.Block,
        powers = p.Creature.Powers.Select(v => new { id = v.Id.ToString(), v.Amount }).ToArray(),
        relics = p.Relics.Select(r => new { id = r.Id.ToString(), next = r.DisplayAmount }).ToArray(),
        hand = p.PlayerCombatState?.Hand.Cards.Count, discard = p.PlayerCombatState?.DiscardPile.Cards.Count }).ToArray();
    internal static void RoomChanged(RunManager manager) => Observe(() => { Refresh(manager); Write("info", "room.enter", Summary()); });
    internal static void Cleanup()
    {
        lock (Gate) { FlushRepeats(); Write("info", "run.cleanup"); _state = null; _context = new { mode = "menu", role = "none" }; }
    }
}

[HarmonyPatch(typeof(RunManager), "EnterRoomInternal")]
internal static class DiagnosticRoomFailure
{
    private static void Postfix(ref Task __result) => __result = ObserveCompletion(__result);
    private static async Task ObserveCompletion(Task original)
    {
        try { await original; }
        catch (Exception ex) { Diagnostics.Error("room.failed", ex); throw; }
    }
}

[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class DiagnosticRunSetup
{
    private static void Postfix(RunManager __instance) => Diagnostics.Observe(() => Diagnostics.Attach(__instance));
}
[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class DiagnosticCleanup
{
    private static void Prefix() => Diagnostics.Observe(Diagnostics.Cleanup);
}
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice))]
internal static class DiagnosticChoice
{
    private static void Prefix(Player player, uint choiceId) => Diagnostics.Observe(() => Diagnostics.Write("info", "choice.sent", new { player = Diagnostics.Slot(player.NetId), choiceId }));
}
[HarmonyPatch(typeof(GameAction), nameof(GameAction.PauseForPlayerChoice))]
internal static class DiagnosticChoiceWait
{
    private static void Prefix(GameAction __instance) => Diagnostics.Observe(() => Diagnostics.Write("info", "action.choice_wait", Diagnostics.ActionData(__instance)));
}
[HarmonyPatch(typeof(RunManager), "RemotePlayerDisconnected")]
internal static class DiagnosticRemoteDisconnect
{
    private static void Prefix(ulong playerId) => Diagnostics.Observe(() => Diagnostics.Write("warn", "network.peer_left", new { player = Diagnostics.Slot(playerId) }));
}
