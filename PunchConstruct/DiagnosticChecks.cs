using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static void DiagnosticChecks()
    {
        Check(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.VolumeMaster == 0,
            "Isolated QA profile starts with master volume zero");
        string root = ProjectSettings.GlobalizePath("user://diagnostic-fixture");
        Directory.CreateDirectory(root);
        var sink = new DiagnosticSink(root, "rotation", 1024, 3);
        Parallel.For(0, 100, i => {
            if (!sink.Write(new { seq = i, message = "中文\n引号\"", padding = new string('x', 150) }))
                throw new InvalidOperationException("Concurrent diagnostic write failed");
        });
        var files = Directory.GetFiles(root, "pc-*.jsonl");
        Check(files.Length == 3 && files.All(p => new FileInfo(p).Length <= 1024), "Diagnostic rotation bounds file count and byte sizes under concurrent writes");
        var records = files.SelectMany(File.ReadAllLines).Select(s => JsonDocument.Parse(s)).ToArray();
        Check(records.Length > 0 && records.All(r => r.RootElement.GetProperty("message").GetString() == "中文\n引号\""),
            "Concurrent JSONL records preserve UTF-8 and escape embedded newlines");
        foreach (var r in records) r.Dispose();
        string blocked = Path.Combine(root, "file-instead-of-directory");
        File.WriteAllText(blocked, "preserved");
        var failed = new DiagnosticSink(blocked, "failure");
        Check(!failed.Write(new { value = 1 }) && failed.Disabled && !failed.Write(new { value = 2 }) && File.ReadAllText(blocked) == "preserved",
            "Unwritable diagnostic destination disables only logging and preserves existing files");
        Diagnostics.Write("info", "qa.diagnostic_probe", new { text = "中文\n引号\"", expected = true });
        Log.Warn("[PunchConstruct diagnostic probe] expected repeated warning");
        Log.Warn("[PunchConstruct diagnostic probe] expected repeated warning");
        Log.Warn("[PunchConstruct diagnostic probe] expected repeated warning");
    }
}
