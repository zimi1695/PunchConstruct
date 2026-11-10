using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Simulated third-party conflicts against the bridge contract. This is a real
// in-engine check: Harmony patches are installed on private fixture methods,
// the bridge fallback paths are exercised, and no gameplay state is touched.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class CompatibilityChecks
{
    private static readonly List<string> Log = [];
    private static int _checks;

    private static void Postfix(NMainMenu __instance)
    {
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-compatcheck")) return;
        Callable.From(() => { _ = Run(__instance.GetTree()); }).CallDeferred();
    }

    private static async Task Run(SceneTree tree)
    {
        try
        {
            await QaStartupPreload.Wait(tree);
            await Frames(tree, 2);

            PostfixChain();
            PrefixSkip();
            GuardFallback();
            AdapterIsolation();
            PolicyTable();
            TranspilerDegradation();
            PatchHealth();
            SaveMigration();
            NullScope();
            IdentityScope();
            StructuralBoundary();
            CharacterScopeCoverage();
            RequiredPolicies();

            GD.Print($"[Construct COMPAT PASS] {_checks} checks; third-party postfix/prefix/IL/boundary simulations");
            tree.Quit();
        }
        catch (Exception error)
        {
            GD.Print("[Construct QA FAIL] " + error);
            GD.PrintErr(error.ToString());
            tree.Quit(1);
        }
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        GD.Print($"[Construct QA {_checks++:D3}] {description}");
    }

    private static async Task Frames(SceneTree tree, int count)
    {
        for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static int NativeFixture(int value) => value;

    private static void ThirdPartyPostfix() => Log.Add("third-party");

    private static void AdapterPostfix() =>
        AdaptationRegistry.Run("qa.compat", "postfix", () => Log.Add("adapter"));

    // A third-party postfix appended its own result first; ours must keep it.
    private static void PostfixChain()
    {
        Log.Clear();
        var harmony = new Harmony("PunchConstruct.Compat.Postfix");
        harmony.Patch(AccessTools.Method(typeof(CompatibilityChecks), nameof(NativeFixture)),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CompatibilityChecks), nameof(ThirdPartyPostfix))));
        harmony.Patch(AccessTools.Method(typeof(CompatibilityChecks), nameof(NativeFixture)),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CompatibilityChecks), nameof(AdapterPostfix))));
        Check(NativeFixture(7) == 7, "native fixture keeps its value after chained postfixes");
        Check(Log.SequenceEqual(["third-party", "adapter"]), "adapter runs after and preserves a third-party postfix result");
    }

    private static int SkipFixture(int value) => value;

    private static bool ThirdPartyPrefix(ref int __result)
    {
        __result = 42;
        return false;
    }

    private static bool AdapterPrefix(ref int __result) =>
        AdaptationRegistry.Guard("qa.compat", "prefix", () =>
        {
            Log.Add("adapter-prefix");
            return true;
        });

    private static void PrefixSkip()
    {
        Log.Clear();
        var harmony = new Harmony("PunchConstruct.Compat.Prefix");
        harmony.Patch(AccessTools.Method(typeof(CompatibilityChecks), nameof(SkipFixture)),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(CompatibilityChecks), nameof(ThirdPartyPrefix))));
        harmony.Patch(AccessTools.Method(typeof(CompatibilityChecks), nameof(SkipFixture)),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(CompatibilityChecks), nameof(AdapterPrefix))));
        // Harmony may stop calling lower-priority prefixes after a foreign skip.
        // The contract is only that the foreign result survives and nothing throws.
        Check(SkipFixture(5) == 42, "a third-party prefix that skips the original still wins");
    }

    private static int FallbackFixture(int value) => value;

    private static bool ThrowingPrefix(ref int __result) =>
        AdaptationRegistry.Guard("qa.compat.fail", "throwing", () => throw new InvalidOperationException("simulated conflict"), true);

    private static void GuardFallback()
    {
        var harmony = new Harmony("PunchConstruct.Compat.Fallback");
        harmony.Patch(AccessTools.Method(typeof(CompatibilityChecks), nameof(FallbackFixture)),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(CompatibilityChecks), nameof(ThrowingPrefix))));
        Check(FallbackFixture(11) == 11, "a throwing optional adapter falls back to native behavior");
        Check(!AdaptationRegistry.Enabled("qa.compat.fail", "throwing"), "a failed hook is disabled after the first error");
        Check(AdaptationRegistry.Enabled("qa.compat.fail", "value"), "a sibling hook of the same adapter stays enabled");
        Check(FallbackFixture(11) == 11, "a disabled hook no longer runs");
        Check(AdaptationRegistry.Run("qa.compat.fail", "throwing", 3, () => 9) == 3, "disabled hooks return the native fallback value");
        Check(AdaptationRegistry.Run("qa.compat.fail", "value", 3, () => 9) == 9, "the sibling hook still returns its own value");
    }

    private static void AdapterIsolation()
    {
        Check(AdaptationRegistry.Enabled("qa.compat.iso1", "throw") && AdaptationRegistry.Enabled("qa.compat.iso2", "throw"),
            "test adapters start enabled");
        AdaptationRegistry.Run("qa.compat.iso1", "throw",
            () => throw new InvalidOperationException("simulated optional failure"));
        Check(!AdaptationRegistry.Enabled("qa.compat.iso1", "throw"), "the failing hook disables only itself");
        Check(AdaptationRegistry.Enabled("qa.compat.iso2", "throw"), "a sibling adapter remains enabled");
        Check(AdaptationRegistry.Run("qa.compat.iso2", "value", 7, () => 9) == 9,
            "the sibling adapter still returns its own value");
    }

    private static void PolicyTable()
    {
        foreach (var adapter in new[] { "reward", "relic", "event", "combat" })
            Check(AdaptationRegistry.Policy(adapter) == AdapterPolicy.Required,
                "core adapter is Required: " + adapter);
        Check(AdaptationRegistry.Policy("presentation") == AdapterPolicy.Optional,
            "presentation adapter is Optional");
        Check(AdaptationRegistry.Policy("qa.unknown") == AdapterPolicy.Optional,
            "unregistered adapters default to Optional");
    }

    private static void PatchHealth()
    {
        Check(PatchRegistry.Applied > 0, "patch registry applied at least one class: " + PatchRegistry.Applied);
        Check(PatchRegistry.RequiredFailures.Count == 0,
            "no required bridge patch failed at startup: " + string.Join(", ", PatchRegistry.RequiredFailures));
        var applied = PatchRegistry.Applied;
        PatchRegistry.ApplyAll();
        Check(PatchRegistry.Applied == applied, "a repeated patch-registry call does not double-apply patches");
        ModEntry.Initialize();
        Check(PatchRegistry.Applied == applied, "a repeated mod initializer call is a no-op");
    }

    private static void SaveMigration()
    {
        var removed = ModelDb.Card<SmallWave>().ToMutable().ToSerializable();
        removed.Id = new ModelId("CARD", "PUNCHCONSTRUCT-GIANT_IRON_WAVE");
        ObserverSaveMigration.Apply(removed);
        Check(removed.Id == ModelDb.GetId<ScorchingIronWave>(),
            "a removed ancient card id migrates to its replacement");

        var watcher = ModelDb.Card<SmallWave>().ToMutable().ToSerializable();
        watcher.Id = new ModelId("CARD", "WATCHER_JUST_LUCKY");
        ObserverSaveMigration.Apply(watcher);
        Check(watcher.Id == ModelDb.GetId<ObserverJustLucky>(),
            "a legacy Watcher card id migrates to the retained observer card");

        var valid = ModelDb.Card<SmallWave>().ToMutable().ToSerializable();
        var validId = valid.Id;
        ObserverSaveMigration.Apply(valid);
        Check(valid.Id == validId, "a current card id is left untouched");
    }

    private static void NullScope()
    {
        Check(!CharacterScope.IsConstruct((CharacterModel?)null), "null character is not the construct");
        Check(!CharacterScope.IsConstruct((Player?)null), "null player is not the construct");
        Check(!CharacterScope.IsConstruct((Creature?)null), "null creature is not the construct");
        Check(!CharacterScope.IsConstruct((RelicModel?)null), "null relic is not the construct");
        Check(!CharacterScope.IsConstruct((CardModel?)null), "null card is not the construct");
        Check(!CharacterScope.IsConstruct((CardPoolModel?)null), "null card pool is not the construct");
        Check(!CharacterScope.IsConstruct((EventModel?)null), "null event is not the construct");
        Check(!CharacterScope.IsConstruct((StartRunLobbyPlayer?)null), "null lobby player is not the construct");
        Check(!CharacterScope.IsConstruct((IPlayerCollection?)null), "null player collection is not the construct");
        Check(!CharacterScope.AnyConstruct(null), "null player list has no construct");
    }

    private static void IdentityScope()
    {
        var construct = ModelDb.GetId<ConstructCharacter>();
        var native = ModelDb.GetId<Ironclad>();
        Check(CharacterScope.IsConstructId(construct), "construct ModelId is recognised");
        Check(CharacterScope.IsConstructId(construct.Entry), "construct id string is recognised");
        Check(!CharacterScope.IsConstructId(native), "native ModelId is rejected");
        Check(!CharacterScope.IsConstructId(native.Entry), "native id string is rejected");
        Check(!CharacterScope.IsConstructId((ModelId?)null), "null ModelId is rejected");
        Check(!CharacterScope.IsConstructId((string?)null), "null id string is rejected");
        Check(!CharacterScope.IsConstructId(""), "empty id string is rejected");
    }

    private static void TranspilerDegradation()
    {
        var target = AccessTools.Method(typeof(CompatibilityChecks), nameof(NativeFixture));
        var noCall = new List<CodeInstruction> { new(OpCodes.Ldc_I4_1), new(OpCodes.Ret) };
        var missing = IlBridge.ReplaceCall(noCall, target,
            call => new[] { new CodeInstruction(OpCodes.Nop) }, "qa.no-target-call");
        Check(ReferenceEquals(missing, noCall) && missing.Count == 2,
            "IL without the target call is returned unchanged");

        var twice = new List<CodeInstruction>
        {
            new(OpCodes.Call, target), new(OpCodes.Call, target), new(OpCodes.Ret)
        };
        var duplicate = IlBridge.ReplaceCall(twice, target,
            call => new[] { new CodeInstruction(OpCodes.Nop) }, "qa.duplicate-call");
        Check(ReferenceEquals(duplicate, twice) && duplicate.Count == 3,
            "IL with an ambiguous call count is returned unchanged");

        var once = new List<CodeInstruction> { new(OpCodes.Call, target), new(OpCodes.Ret) };
        var replaced = IlBridge.ReplaceCall(once, target,
            call => new[] { new CodeInstruction(OpCodes.Nop) }, "qa.single-call");
        Check(replaced.Count == 2 && replaced[0].opcode == OpCodes.Nop,
            "a single target call is replaced exactly once");

        var throwing = new List<CodeInstruction> { new(OpCodes.Call, target), new(OpCodes.Ret) };
        var guarded = IlBridge.ReplaceCall(throwing, target,
            _ => throw new InvalidOperationException("simulated rewrite failure"), "qa.rewrite-failure");
        Check(ReferenceEquals(guarded, throwing) && guarded.Count == 2,
            "a throwing rewrite returns the original IL");
    }

    private static void StructuralBoundary()
    {
        bool Infrastructure(string? name) =>
            name is not null && (name.EndsWith("Checks", StringComparison.Ordinal) ||
                name.Contains("Diagnostic", StringComparison.Ordinal) ||
                name.Contains("Playtest", StringComparison.Ordinal) ||
                name.Contains("Probe", StringComparison.Ordinal) ||
                name is "PunchConstruct.SmokeCheck" or "PunchConstruct.QaStartupPreload");
        var offenders = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.Namespace != "PunchConstruct.NativeBridge")
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), true).Any())
            .Where(type => !Infrastructure(type.FullName))
            .Select(type => type.FullName)
            .ToArray();
        Check(offenders.Length == 0, "business Harmony patches live only in NativeBridge: " + string.Join(", ", offenders));
    }

    private static void CharacterScopeCoverage()
    {
        string[] required = ["Player", "Creature", "RelicModel", "CardModel", "CharacterModel", "EventModel", "IPlayerCollection"];
        foreach (var name in required)
            Check(typeof(CharacterScope).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .Any(method => method.Name == "IsConstruct" && method.GetParameters().Length == 1 &&
                        method.GetParameters()[0].ParameterType.Name == name),
                "CharacterScope covers " + name);
        Check(typeof(CharacterScope).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Count(method => method.Name == "IsConstruct") >= 8,
            "CharacterScope exposes the lobby and run-state overloads too");
        Check(typeof(CharacterScope).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Count(method => method.Name == "IsConstructId") >= 2,
            "CharacterScope also covers ModelId and string identity");
    }

    private static void RequiredPolicies()
    {
        string[] required = ["BridgeUnarmBlock", "BridgePhilosopherRandomRewards", "BridgeConstructColorlessShop", "BridgeConstructRelicVariants"];
        var types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (var name in required)
        {
            var type = types.SingleOrDefault(candidate => candidate.Name == name && candidate.Namespace == "PunchConstruct.NativeBridge");
            Check(type != null && type.GetCustomAttribute<PatchPolicyAttribute>()?.Required == true,
                "critical bridge patch carries a Required policy: " + name);
        }
    }
}
