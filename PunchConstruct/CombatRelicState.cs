using BaseLib.Utils;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct;

// Native multiplayer can rebuild another player's relics from serialized state.
// Private fields and weak-table-only flags would silently revert in that path.
internal static class CombatRelicState
{
    internal static readonly SavedSpireField<GuardStance, int> GuardNext = new(() => 1, "PunchConstruct_NextGrant");
    internal static readonly SavedSpireField<AncientGuardStance, int> AncientNext = new(() => 1, "PunchConstruct_NextGrant");
    internal static readonly SavedSpireField<ConstructHelmet, bool> HelmetUsed = new(() => false, "PunchConstruct_Used");
    internal static readonly SavedSpireField<RuinedHelmet, bool> NativeHelmetUsed = new(() => false, "PunchConstruct_Used");
    internal static readonly SavedSpireField<SpinningScrew, bool> ScrewDamageGiven = new(() => false, "PunchConstruct_DamageGiven");
    internal static readonly SavedSpireField<SpinningScrew, bool> ScrewDamageTaken = new(() => false, "PunchConstruct_DamageTaken");
    internal static readonly SavedSpireField<SpinningScrew, bool> ScrewDrawReady = new(() => false, "PunchConstruct_DrawReady");
    static CombatRelicState()
    {
        GuardNext.CopyOnClone(); AncientNext.CopyOnClone();
        HelmetUsed.CopyOnClone(); NativeHelmetUsed.CopyOnClone();
        ScrewDamageGiven.CopyOnClone(); ScrewDamageTaken.CopyOnClone(); ScrewDrawReady.CopyOnClone();
    }
    // Register save/network fields on both peers at mod initialization, before
    // lobby handshake and any character-specific UI or combat callback.
    internal static void Initialize() { }
}
