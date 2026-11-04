using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static void TreasureRelicChecks()
    {
        string[] Generate(ulong seed, bool native, bool mixed = false)
        {
            var unlocks = new UnlockState([], [], 0);
            var first = native ? Player.CreateForNewRun<Ironclad>(unlocks, 1)
                : Player.CreateForNewRun<ConstructCharacter>(unlocks, 1);
            Player[] players = mixed ? [first, Player.CreateForNewRun<Ironclad>(unlocks, 2)] : [first];
            var run = RunState.CreateForTest(players, seed: "TREASUREEXCLUSIONS");
            var bag = new RelicGrabBag();
            // Force the real shared chest path to choose between banned and valid relics.
            bag.Populate([ModelDb.Relic<Orichalcum>(), ModelDb.Relic<BronzeScales>(),
                ModelDb.Relic<Anchor>(), ModelDb.Relic<Bread>(), ModelDb.Relic<ChemicalX>()], new Rng(seed));
            var sync = new TreasureRoomRelicSynchronizer(run, first.NetId,
                RunManager.Instance.ActionQueueSynchronizer, bag, new Rng(seed));
            sync.BeginRelicPicking();
            return (sync.CurrentRelics ?? throw new InvalidOperationException("Native treasure list was not initialized"))
                .Select(r => r.Id.Entry).ToArray();
        }
        int nativeOrichalcum = 0, mixedNativeOrichalcum = 0;
        bool singleAllowed = true, mixedAllowed = true, deterministic = true;
        for (ulong seed = 0; seed < 64; ++seed)
        {
            var solo = Generate(seed, native: false);
            var native = Generate(seed, native: true);
            var mixed = Generate(seed, native: false, mixed: true);
            singleAllowed &= solo.Length == 1 && solo.All(id => id is not ("ORICHALCUM" or "CHEMICAL_X"));
            mixedAllowed &= mixed.Length == 2 && mixed[0] is not ("ORICHALCUM" or "CHEMICAL_X");
            if (native.Contains("ORICHALCUM")) ++nativeOrichalcum;
            if (mixed.Length == 2 && mixed[1] == "ORICHALCUM") ++mixedNativeOrichalcum;
            deterministic &= solo.SequenceEqual(Generate(seed, native: false))
                && mixed.SequenceEqual(Generate(seed, native: false, mixed: true));
        }
        Check(singleAllowed, "Native shared treasure generation excludes Orichalcum for the construct across 64 seeds");
        Check(mixedAllowed, "The construct's treasure contribution is filtered in mixed-character parties");
        Check(nativeOrichalcum > 0 && mixedNativeOrichalcum > 0,
            "Native-only runs and native teammates retain their original Orichalcum treasure access");
        Check(deterministic, "Identical shared treasure seeds produce identical solo and mixed-party results");
        Check(RelicFactoryScope.CurrentPlayer == null && RelicFactoryScope.CurrentRng == null,
            "Treasure draw scope restores both player and RNG before subsequent shops or rewards");
    }
}
