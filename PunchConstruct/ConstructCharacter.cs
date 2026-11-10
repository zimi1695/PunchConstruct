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
        // Keep the authored die pose but force the root scale positive every frame,
        // so the character finishes the death animation facing the same way it
        // started. This is the 0.4.24 behaviour, without any timeline stretching.
        var root = controller.GetSkeleton()?.FindBone("root");
        if (root != null)
        {
            controller.ConnectBeforeWorldTransformsChange(Callable.From<GodotObject>(_ =>
            {
                if (controller.GetAnimationState().GetCurrentAnimationName() == "die")
                    root.SetScaleX(Math.Abs(root.BoundObject.Call("get_scale_x").AsSingle()));
            }));
        }
        var animator = SetupAnimationState(controller, "idle_loop", deadName: "die", hitName: "hurt", attackName: "attack", castName: "block");
        animator.AddAnyState("DoubleAttack", new AnimState("attack_double", false) { NextState = new AnimState("idle_loop", true) }, null);
        // Native power cards call the PowerUp trigger; use the same cast pose so
        // borrowed powers still animate without any native patch.
        animator.AddAnyState("PowerUp", new AnimState("block", false) { NextState = new AnimState("idle_loop", true) }, null);
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
