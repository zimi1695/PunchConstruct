using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static class CardAcquisition
{
    [ThreadStatic] internal static bool SeaGlassSelection;
    // Trial values, deliberately centralized; design rules do not fix these numbers yet.
    internal const int GroupMultiplier = 2;
    internal const float AncientShopChance = .05f;
    internal const decimal AncientPriceMultiplier = 1.5m;
    internal static readonly Type[][] Groups = [
        [typeof(Bulwark), typeof(Parry), typeof(ForgeWorkshop), typeof(RoyalBearing), typeof(StrikeWhileHot)],
        [typeof(SicEm), typeof(Bodyguard), typeof(Unleash), typeof(BoneShards), typeof(UnarmToArms), typeof(DoomResonance)],
        [typeof(FinalJudgment), typeof(NegativePulse), typeof(Deathbringer), typeof(Shroud), typeof(DoomResonance)],
        [typeof(Hailstorm), typeof(ColdSnap), typeof(Rainbow)], [typeof(ObserverProstrate)]
    ];
    internal static readonly Type[] ShopAncients = [typeof(Suppress), typeof(MeteorShower), typeof(Protector), typeof(Whistle)];
    internal static bool IsAncient(CardModel card) => ShopAncients.Contains(card.GetType()) || card is BoxingChampionForm;
    internal static bool IsPhilosopher(Player player) => player.RunState.CurrentRoom is EventRoom room && room.CanonicalEvent is ColorfulPhilosophers;
    internal static bool Eligible(Player player, CardModel card)
    {
        // The ordinary wave is a starter only; Soup keeps its separately defined
        // fixed transformation. Even native-pool challenges must not reoffer it.
        if (card is IronWave || BoxingGeneration.Restricted(card)) return false;
        if (SeaGlassSelection) return IsSelectedImport(card);
        if (IsPhilosopher(player)) return IsPhilosopherCard(card);
        if (ChallengeAllows(player, card)) return true;
        if (!ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(card.Id)) return false;
        if (IsAncient(card)) return false;
        bool Own(Type t) => player.Deck.Cards.Any(c => c.GetType() == t);
        bool Group(int i) => Groups[i].Any(Own);
        return card switch {
            Parry or ForgeWorkshop or RoyalBearing => Group(0),
            Unleash or BoneShards => Own(typeof(Bodyguard)),
            Shroud or FinalJudgment => Group(2),
            Hailstorm => Group(3), _ => true
        };
    }
    // Explicit native character-pool challenges override the normal whitelist
    // for that selected native pool only. No global pool mutation or new RNG draw.
    internal static bool ChallengeAllows(Player player, CardModel card) => CharacterScope.IsConstruct(player) &&
        player.RunState.Modifiers.OfType<CharacterCards>().Any(modifier => {
            var character = ModelDb.GetById<CharacterModel>(modifier.CharacterModel);
            return character.GetType().Assembly == typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad).Assembly &&
                character.CardPool.AllCardIds.Contains(card.Id);
        });
    internal static bool IsSelectedImport(CardModel card) =>
        ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(card.Id) &&
        card.GetType().Assembly != typeof(ConstructCharacter).Assembly && !IsAncient(card) &&
        card.Pool is RegentCardPool or NecrobinderCardPool or DefectCardPool;
    internal static bool IsPhilosopherCard(CardModel card) =>
        ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(card.Id) && !IsAncient(card) &&
        card.Pool is RegentCardPool or NecrobinderCardPool or DefectCardPool;
    internal static IEnumerable<CardModel> IncludeDesignedColorless(Player player, IEnumerable<CardModel> source)
    {
        var cards = source.ToArray();
        if (cards.Length == 0 || cards.Any(c => c.Pool is not ColorlessCardPool)) return cards;
        var types = cards.Select(c => c.Type).ToHashSet();
        var rarities = cards.Select(c => c.Rarity).ToHashSet();
        var unlocked = ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint).ToArray();
        if (cards.Select(c => c.Id).ToHashSet().SetEquals(unlocked.Select(c => c.Id)))
            return cards.Concat(ModelDb.CardPool<ConstructCardPool>().AllCards.Where(c => c.Pool is ColorlessCardPool)).DistinctBy(c => c.Id).ToArray();
        var native = unlocked.Where(c => types.Contains(c.Type) && rarities.Contains(c.Rarity));
        // Expand complete native pool queries, including type/rarity filters.
        // A one-off explicit list of cards is not a pool query.
        if (!cards.Select(c => c.Id).ToHashSet().SetEquals(native.Select(c => c.Id))) return cards;
        return cards.Concat(ModelDb.CardPool<ConstructCardPool>().AllCards.Where(c => c.Pool is ColorlessCardPool &&
            types.Contains(c.Type) && rarities.Contains(c.Rarity))).DistinctBy(c => c.Id).ToArray();
    }
    internal static int Weight(Player player, CardModel card) => SeaGlassSelection || IsPhilosopher(player) ? 1 :
        Groups.Any(g => g.Contains(card.GetType()) && player.Deck.Cards.Any(c => g.Contains(c.GetType()))) ? GroupMultiplier : 1;
    internal static CardModel Pick(Rng rng, IEnumerable<CardModel> cards, Player player)
    {
        if (!CharacterScope.IsConstruct(player)) return rng.NextItem(cards)!;
        // Weight at the final draw, after the native Except/Distinct and rarity roll.
        var weighted = cards.SelectMany(c => Enumerable.Repeat(c, Weight(player, c) *
            (CardFactoryScope.RugReward && c.Pool is ColorlessCardPool && player.Relics.Any(r => r is DingyRug)
                ? ConstructDingyRug.ColorlessMultiplier : 1))).ToArray();
        return rng.NextItem(weighted)!;
    }
}

// Random combat generation bypasses CardCreationOptions (potions, Toolbox,
// Orange Dough and similar relics). Fixed event/token/status cards are not a
// general reward pool and keep their native generation behavior.
internal static class ConstructCombatCardSources
{
    internal static IEnumerable<MethodBase> Targets() => typeof(CardFactory).GetMethods()
        .Where(m => m.Name is nameof(CardFactory.GetDistinctForCombat) or nameof(CardFactory.GetForCombat));
    internal static void Apply(Player player, ref IEnumerable<CardModel> __1, ref int __2, MethodBase __originalMethod, out Player? __state)
    {
        __state = CardFactoryScope.Player;
        if (!CharacterScope.IsConstruct(player)) return;
        CardFactoryScope.Player = player;
        var options = CardAcquisition.IncludeDesignedColorless(player, __1).Where(c => c.Rarity is CardRarity.Event or CardRarity.Token or CardRarity.Status or CardRarity.Curse or CardRarity.Quest ||
            CardAcquisition.Eligible(player, c)).DistinctBy(c => c.Id).ToArray();
        __1 = options;
        if (options.Length == 0) __2 = 0;
        else if (__originalMethod.Name == nameof(CardFactory.GetDistinctForCombat))
            __2 = Math.Min(__2, options.Select(c => c.Id).Distinct().Count());
    }
    internal static void Resolve(Player player, ref IEnumerable<CardModel> __result)
    {
        // Resolve native lazy enumeration before restoring the character scope.
        if (CharacterScope.IsConstruct(player)) __result = __result.ToArray();
    }
    internal static void Restore(Player? __state) => CardFactoryScope.Player = __state;
}

// Reward-domain adapter: see NativeBridge/RewardBridge.cs.
internal static class GroupCardGates
{
    internal static void Apply(CardCreationOptions __instance, Player player, ref IEnumerable<CardModel> __result)
    {
        if (!CharacterScope.IsConstruct(player)) return;
        if (CardAcquisition.SeaGlassSelection || CardAcquisition.IsPhilosopher(player))
        {
            // These selected imports are already available in the character's own pool.
            // Native foreign-character epoch locks can otherwise leave fewer than 3 options.
            var rebuilt = __instance.CardPools.SelectMany(p => p.AllCards)
                .Concat(CardAcquisition.SeaGlassSelection ? [] : ModelDb.CardPool<ConstructCardPool>().AllCards)
                .DistinctBy(c => c.Id)
                .Where(c => __instance.CardPools.Contains(c.Pool))
                .Where(c => CardAcquisition.SeaGlassSelection ? CardAcquisition.IsSelectedImport(c) : CardAcquisition.IsPhilosopherCard(c))
                .Where(c => __instance.CardPoolFilter?.Invoke(c) ?? true)
                .ToArray();
            // Keep candidates appended by other mods when they still satisfy the
            // selected import or philosopher rules.
            __result = rebuilt.Concat(__result).DistinctBy(c => c.Id)
                .Where(c => __instance.CardPools.Contains(c.Pool))
                .Where(c => CardAcquisition.SeaGlassSelection ? CardAcquisition.IsSelectedImport(c) : CardAcquisition.IsPhilosopherCard(c))
                .Where(c => __instance.CardPoolFilter?.Invoke(c) ?? true);
        }
        else
        {
            var additions = __instance.CardPools.Any(p => p is ColorlessCardPool)
                ? ModelDb.CardPool<ConstructCardPool>().AllCards.Where(c => c.Pool is ColorlessCardPool &&
                    (__instance.CardPoolFilter?.Invoke(c) ?? true)) : [];
            __result = __result.Concat(additions).DistinctBy(c => c.Id)
                .Where(c => c.Rarity is CardRarity.Event or CardRarity.Token or CardRarity.Status or CardRarity.Curse or CardRarity.Quest ||
                    CardAcquisition.Eligible(player, c)).ToArray();
        }
    }
}

// Three selections of three candidates, no rarity bands or repeated candidates.
// Reward-domain adapter: see NativeBridge/RewardBridge.cs.
internal static class PhilosopherRandomRewards
{
    internal static bool Apply(ColorfulPhilosophers __instance, CardPoolModel pool, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance.Owner)) return true;
        __result = Offer(__instance, pool);
        return false;
    }
    private static async Task Offer(ColorfulPhilosophers philosopher, CardPoolModel pool)
    {
        var shown = new HashSet<Type>();
        var rewards = new List<Reward>();
        for (int i = 0; i < 3; i++)
        {
            // Populate before building the next row, excluding every offered card,
            // not just the card the player eventually chooses.
            var excluded = shown.ToHashSet();
            var options = CardCreationOptions.ForNonCombatWithUniformOdds([pool],
                    card => CardAcquisition.IsPhilosopherCard(card) && !excluded.Contains(card.GetType()))
                .WithFlags((CardCreationFlags)17); // Retain the original event's flags.
            var reward = new CardReward(options, 3, philosopher.Owner!, null);
            reward.Populate();
            shown.UnionWith(reward.Cards.Select(card => card.GetType()));
            rewards.Add(reward);
        }
        await RewardsCmd.OfferCustom(philosopher.Owner!, rewards);
        AccessTools.Method(typeof(EventModel), "SetEventFinished").Invoke(philosopher,
            [new LocString("events", "COLORFUL_PHILOSOPHERS.pages.DONE.description")]);
    }
}

internal static class CardFactoryScope
{
    [ThreadStatic] internal static Player? Player;
    [ThreadStatic] internal static bool RugReward;
    internal static IEnumerable<MethodBase> Targets() => typeof(CardFactory).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Where(m => m.Name is "CreateForReward" or "CreateForMerchant");
    internal static void Apply(Player player, MethodBase __originalMethod, object[] __args, out (Player?, bool) __state)
    {
        __state = (Player, RugReward);
        Player = player;
        var options = __args.OfType<CardCreationOptions>().FirstOrDefault();
        RugReward = __originalMethod.Name == "CreateForReward" && options != null &&
            options.Flags.HasFlag(CardCreationFlags.IsCardReward) &&
            !options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications) &&
            !options.Flags.HasFlag(CardCreationFlags.NoModifyHooks) &&
            !CardAcquisition.SeaGlassSelection && !CardAcquisition.IsPhilosopher(player);
    }
    internal static void Restore((Player?, bool) __state) { Player = __state.Item1; RugReward = __state.Item2; }
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        foreach (var instruction in original)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Rng) && method.Name == nameof(Rng.NextItem) && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual([typeof(CardModel)]))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                rewritten.Add(load);
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CardAcquisition), nameof(CardAcquisition.Pick))));
            }
            else rewritten.Add(instruction);
        }
        return rewritten;
    }
}

internal static class ConstructCardRarity
{
    internal static void Apply(CardModel __instance, ref CardRarity __result)
    {
        bool owned = RegentAdaptations.Owns(__instance);
        bool canonicalInFactory = !__instance.IsMutable && CharacterScope.IsConstruct(CardFactoryScope.Player);
        if (!owned && !canonicalInFactory) return;
        if (__instance is IronWave) __result = CardRarity.Basic;
        if (__instance is Bodyguard or Neutralize or FallingStar or Unleash or Prowess) __result = CardRarity.Common;
        if (CardAcquisition.IsAncient(__instance)) __result = CardRarity.Ancient;
    }
}

internal static class MerchantCardGates
{
    internal static IEnumerable<MethodBase> Targets() => typeof(CardFactory).GetMethods().Where(m => m.Name == nameof(CardFactory.CreateForMerchant));
    internal static void Apply(Player player, ref IEnumerable<CardModel> __1, MethodBase __originalMethod)
    {
        if (CharacterScope.IsConstruct(player))
        {
            bool coloredSlot = __originalMethod.GetParameters().Last().ParameterType == typeof(CardType);
            __1 = CardAcquisition.IncludeDesignedColorless(player, __1)
                .Where(c => CardAcquisition.Eligible(player, c) && (!coloredSlot || c.Pool is not ColorlessCardPool)).ToArray();
        }
    }
}

// Reward-domain adapter: see NativeBridge/RewardBridge.cs.

internal static class ConstructColorlessShop
{
    internal static bool Apply(MerchantInventory __instance)
    {
        var player = __instance.Player;
        if (!CharacterScope.IsConstruct(player)) return true;
        var options = player.Character.CardPool.AllCards.Where(c => c.Pool is ColorlessCardPool && CardAcquisition.Eligible(player, c)).ToArray();
        var entries = (List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory), "_colorlessCardEntries").GetValue(__instance)!;
        foreach (var rarity in new[] { CardRarity.Uncommon, CardRarity.Rare })
        {
            var entry = new MerchantCardEntry(player, __instance, options, rarity);
            entry.Populate();
            entries.Add(entry);
        }
        return false;
    }
}

// Reward-domain adapter: see NativeBridge/RewardBridge.cs.
internal static class PhilosopherColors
{
    // Native options are generated first; only this character's three pool
    // choices are replaced, preserving earlier option work from other mods.
    internal static void Apply(ColorfulPhilosophers __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (!CharacterScope.IsConstruct(__instance.Owner)) return;
        __result = new CardPoolModel[] { ModelDb.CardPool<RegentCardPool>(), ModelDb.CardPool<NecrobinderCardPool>(), ModelDb.CardPool<DefectCardPool>() }
            .Select(pool => new EventOption(__instance,
                () => (Task)AccessTools.Method(typeof(ColorfulPhilosophers), "OfferRewards").Invoke(__instance, [pool])!,
                "COLORFUL_PHILOSOPHERS.pages.INITIAL.options." + pool.EnergyColorName.ToUpperInvariant(), [])).ToArray();
    }
}

// Reward-domain adapter: see NativeBridge/RewardBridge.cs.
internal static class ConstructDustyTome
{
    internal static bool Apply(DustyTome __instance, Player player)
    {
        if (!CharacterScope.IsConstruct(player)) return true;
        AccessTools.PropertySetter(typeof(DustyTome), nameof(DustyTome.AncientCard)).Invoke(__instance, [ModelDb.GetId<BoxingChampionForm>()]);
        return false;
    }
}
