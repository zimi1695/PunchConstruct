using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

public sealed class ConstructCharacter : CustomCharacterModel
{
    // QA observes the fixed-time death-cut transition through this counter.
    internal static int DeathCutTransitions;

    public override int StartingHp => ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Defect>().StartingHp;
    public override int BaseOrbSlotCount => ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>().BaseOrbSlotCount;
    public override string CustomAttackSfx => ConstructPresentation.SingleSfx;
    public override string CustomCastSfx => ConstructPresentation.GuardSfx;
    public override string CustomDeathSfx => "event:/sfx/characters/defect/defect_die";
    public override string CharacterSelectSfx => CharacterSelectAudio.Cue;
    public override string CharacterTransitionSfx => ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Defect>().CharacterTransitionSfx;
    public override CharacterGender Gender => CharacterGender.Neutral;
    public override Color NameColor => new("9ec9c7");
    public override CardPoolModel CardPool => ModelDb.CardPool<ConstructCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<ConstructRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<ConstructPotionPool>();
    public override IEnumerable<CardModel> StartingDeck => [
        ..Enumerable.Repeat(ModelDb.Card<SmallWave>(), 5),
        ModelDb.Card<IronWave>(), ModelDb.Card<TonyWave>(), ModelDb.Card<SmallShockwave>()
    ];
    public override IReadOnlyList<RelicModel> StartingRelics => [ModelDb.Relic<GuardStance>()];
    public override List<string> GetArchitectAttackVfx() => ["vfx/vfx_flying_slash"];
    public override string CustomVisualPath => "res://PunchConstruct/scenes/construct.tscn";
    public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
    {
        // The monster's die track flips the root bone and hard-cuts every limb to
        // its mirrored pose at 0.133 s. This character's scene is already mirrored
        // to face enemies; keep that facing while it falls, and replay the 4 ms
        // cut over a fixed real-time transition so the pose interpolates instead
        // of flipping for a single frame.
        var root = controller.GetSkeleton()?.FindBone("root");
        if (root != null)
        {
            bool timing = false, counted = false;
            ulong start = 0;
            controller.ConnectBeforeWorldTransformsChange(Callable.From<GodotObject>(_ =>
            {
                var state = controller.GetAnimationState();
                if (state.GetCurrentAnimationName() != "die")
                {
                    timing = false;
                    counted = false;
                    return;
                }
                var entry = state.GetCurrent(0);
                if (entry != null)
                {
                    if (!timing)
                    {
                        // Drive the whole death animation against real time so a
                        // long frame cannot jump over the 0.129-0.133 pose cut.
                        timing = true;
                        counted = false;
                        start = Time.GetTicksMsec();
                        Diagnostics.Write("info", "death.cut.stretch", new { seconds = .120 });
                    }
                    entry.SetTimeScale(0f);
                    float real = (float)((Time.GetTicksMsec() - start) / 1000.0 * Engine.TimeScale);
                    float time = real <= .110f ? real
                        : real <= .230f ? .110f + .023f * ((real - .110f) / .120f)
                        : .133f + (real - .230f);
                    if (real >= .230f && !counted)
                    {
                        counted = true;
                        DeathCutTransitions++;
                    }
                    if (time >= 2.06f) entry.SetTimeScale(1f);
                    else entry.SetTrackTime(time);
                }
                root.SetScaleX(Math.Abs(root.BoundObject.Call("get_scale_x").AsSingle()));
            }));
        }
        var animator = SetupAnimationState(controller, "idle_loop", deadName: "die", hitName: "hurt", attackName: "attack", castName: "block");
        animator.AddAnyState("DoubleAttack", new AnimState("attack_double", false) { NextState = new AnimState("idle_loop", true) }, null);
        return animator;
    }
    public override string CustomCharacterSelectBg => "res://PunchConstruct/scenes/character_select.tscn";
    public override string CustomCharacterSelectIconPath => "res://PunchConstruct/art/character_select_icon.ctex";
    public override string CustomCharacterSelectLockedIconPath => CustomCharacterSelectIconPath;
    public override string CustomIconTexturePath => "res://PunchConstruct/art/portrait.ctex";
    public override string CustomIconOutlineTexturePath => CustomIconTexturePath;
    public override string CustomIconPath => "res://PunchConstruct/scenes/character_icon.tscn";
    public override string CustomEnergyCounterPath => "res://scenes/combat/energy_counters/defect_energy_counter.tscn";
    public override string CustomTrailPath => "res://scenes/vfx/card_trail_defect.tscn";
    public override string CustomCharacterSelectTransitionPath => "res://materials/transitions/defect_transition_mat.tres";
    public override string CustomMapMarkerPath => "res://images/packed/map/icons/map_marker_defect.png";
    public override string CustomRestSiteAnimPath => "res://PunchConstruct/scenes/rest.tscn";
    public override string CustomMerchantAnimPath => "res://PunchConstruct/scenes/merchant.tscn";
    public override string CustomArmPointingTexturePath => "res://images/ui/hands/multiplayer_hand_defect_point.png";
    public override string CustomArmRockTexturePath => "res://images/ui/hands/multiplayer_hand_defect_rock.png";
    public override string CustomArmPaperTexturePath => "res://images/ui/hands/multiplayer_hand_defect_paper.png";
    public override string CustomArmScissorsTexturePath => "res://images/ui/hands/multiplayer_hand_defect_scissors.png";
    public override List<(string, string)> Localization => [
        ("title", Loc.Pick("拳击构装体")), ("titleObject", Loc.Pick("拳击构装体")),
        ("description", Loc.Pick("来自暗港的拳击构装体。\n在每一次严密的防守之后，送还一记重拳。")),
        ("pronounObject", Loc.Pick("它")), ("pronounSubject", Loc.Pick("它")),
        ("pronounPossessive", Loc.Pick("它的")), ("possessiveAdjective", Loc.Pick("它的")),
        ("cardsModifierTitle", Loc.Pick("拳击构装体卡牌")), ("cardsModifierDescription", Loc.Pick("拳击构装体的卡牌加入奖励和商店。"))
    ];
}
