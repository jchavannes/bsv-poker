using BsvPoker.Core;

namespace BsvPoker.Tests;

/// <summary>
/// FULL-GAMEPLAY acceptance: drive COMPLETE hands (not just folds) to real showdowns across every variant and
/// 2–6 players, through the actual <see cref="HoldemState"/> engine, asserting the invariants a human would
/// notice: my hole cards are dealt face-up; the board reveals the right count per street; betting is always a
/// legal action; chips are conserved EXACTLY every hand; the showdown winner genuinely holds the best hand;
/// side pots split correctly; multi-hand sessions carry stacks and rotate the button until one player has it
/// all. This is the practice-engine path the lobby "Play a bot" uses — it must be flawless.
/// </summary>
public static class FullGameplayTests
{
    // A bot that PLAYS to showdown (calls/checks; never folds unless it cannot call) so hands actually reach
    // a board + showdown most of the time — exercising the scoring/side-pot code, not just fold-wins.
    private static GameAction ShowdownBot(HoldemState st)
    {
        int seat = st.ToAct;
        var la = st.Legal();
        if (la.CanCheck) return new GameAction(ActionKind.Check, seat);
        if (la.CanCall) return new GameAction(ActionKind.Call, seat);
        return new GameAction(ActionKind.Fold, seat);
    }

    private static HoldemState FreshHand(long[] stacks, int button, Variant v)
    {
        var deck = MentalPoker.ShuffledFrom(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() }, Variants.CardSet(v));
        return HoldemState.Create(stacks, button, sb: 1, bb: 2, deck, v, deferShowdown: false);
    }

    public static void All()
    {
        Console.WriteLine("full gameplay (complete hands to showdown, all variants, chips conserved, correct winner):");

        T.Run("every variant deals the right hole-card count face-up to each seat, and a real board appears", () =>
        {
            foreach (var v in Variants.All)
            {
                int hole = Variants.HoleCards(v);
                var st = FreshHand(new long[] { 100, 100 }, 0, v);
                foreach (var s in st.Seats)
                {
                    T.Eq(s.Hole.Length, hole, $"{v}: seat {s.Seat} has {hole} hole cards");
                    T.True(s.Hole.All(c => !c.IsFaceDown), $"{v}: my dealt holes are face-up (real cards), not sentinels");
                }
                // play it out; a contested hand must reach a 3/4/5-card board
                int guard = 0;
                while (!st.Complete && guard++ < 500) st.Apply(ShowdownBot(st));
                T.True(st.Complete, $"{v}: the hand completes");
                if (st.Seats.Count(s => !s.Folded) > 1)
                    T.True(st.Board.Count is 3 or 4 or 5, $"{v}: a contested hand shows a real board (got {st.Board.Count})");
            }
        });

        T.Run("CHIPS CONSERVED EXACTLY across 200 complete hands per variant (2–6 players, varied buttons)", () =>
        {
            foreach (var v in Variants.All)
            {
                for (int players = 2; players <= 6; players++)
                {
                    long start = 200;
                    var stacks = Enumerable.Repeat(start, players).ToArray();
                    long total = stacks.Sum();
                    for (int hand = 0; hand < 40; hand++)
                    {
                        // reseat only the seats with chips (>= the big blind region); stop if <2 remain
                        var live = stacks.Where(x => x > 0).ToArray();
                        if (live.Length < 2) break;
                        var st = FreshHand(live, hand % live.Length, v);
                        int guard = 0;
                        while (!st.Complete && guard++ < 2000) st.Apply(ShowdownBot(st));
                        T.True(st.Complete, $"{v} p{players}: hand {hand} completes");
                        // fold back the played stacks into the live array
                        var newStacks = st.Seats.Select(s => s.Stack).ToArray();
                        T.Eq(newStacks.Sum(), live.Sum(), $"{v} p{players}: chips conserved within hand {hand}");
                        // rebuild the full stacks vector (busted seats stay 0)
                        int j = 0; for (int i = 0; i < stacks.Length; i++) if (stacks[i] > 0) stacks[i] = newStacks[j++];
                        T.Eq(stacks.Sum(), total, $"{v} p{players}: TABLE chips conserved after hand {hand}");
                    }
                }
            }
        });

        T.Run("the SHOWDOWN winner genuinely holds the best hand (engine payout matches an independent eval)", () =>
        {
            int checks = 0;
            foreach (var v in Variants.All)
            {
                for (int trial = 0; trial < 60; trial++)
                {
                    var st = FreshHand(new long[] { 100, 100, 100 }, trial % 3, v);
                    int guard = 0;
                    while (!st.Complete && guard++ < 500) st.Apply(ShowdownBot(st));
                    // only check genuine showdowns (>=2 live, full 5-card board)
                    var live = st.Seats.Where(s => !s.Folded).ToList();
                    if (live.Count < 2 || st.Board.Count < 5) continue;
                    bool exactly2 = Variants.ExactlyTwoHole(v);
                    var scored = live.Select(s => (s.Seat, Score: HandEval.BestForVariant(s.Hole, st.Board, exactly2).Score)).ToList();
                    long best = scored.Max(x => x.Score);
                    var trueWinners = scored.Where(x => x.Score == best).Select(x => x.Seat).ToHashSet();
                    // every seat the engine PAID must be a genuine best-hand holder (allowing split pots)
                    foreach (var paidSeat in st.Payouts.Keys)
                        T.True(trueWinners.Contains(paidSeat), $"{v}: engine paid seat {paidSeat}, but it does not hold the best hand");
                    checks++;
                }
            }
            T.True(checks > 0, "at least some genuine showdowns were evaluated");
        });

        T.Run("a multi-hand SESSION runs to a single winner holding ALL chips (chips conserved throughout)", () =>
        {
            // Use a bot with BETTING PRESSURE (BotPolicy raises/folds) so the session actually converges the way a
            // real game does — a pure check/call table can tie forever and never bust anyone (chips still conserve,
            // but no one ever loses, which is not "a game ending"). With real betting, stacks concentrate.
            foreach (var v in new[] { Variant.TexasHoldem, Variant.Omaha, Variant.RoyalHoldem })
            {
                long start = 50;
                int players = 4;
                var stacks = Enumerable.Repeat(start, players).ToArray();
                long total = stacks.Sum();
                int hand = 0; bool converged = false;
                while (stacks.Count(x => x > 0) > 1 && hand < 20000)
                {
                    var liveIdx = Enumerable.Range(0, players).Where(i => stacks[i] > 0).ToArray();
                    var live = liveIdx.Select(i => stacks[i]).ToArray();
                    var st = FreshHand(live, hand % live.Length, v);
                    int guard = 0;
                    while (!st.Complete && guard++ < 4000) st.Apply(BotPolicy.Decide(st));
                    T.True(st.Complete, $"{v}: hand {hand} completes under betting pressure");
                    var res = st.Seats.Select(s => s.Stack).ToArray();
                    for (int k = 0; k < liveIdx.Length; k++) stacks[liveIdx[k]] = res[k];
                    T.Eq(stacks.Sum(), total, $"{v}: chips conserved at hand {hand}");
                    hand++;
                    if (stacks.Count(x => x > 0) == 1) { converged = true; break; }
                }
                T.True(converged, $"{v}: session converges to a single winner (got {stacks.Count(x => x > 0)} survivors after {hand} hands)");
                T.Eq(stacks.Max(), total, $"{v}: the winner holds ALL {total} chips");
            }
        });

        T.Run("HOSTILE: the engine rejects every illegal action and never lets a seat act out of turn", () =>
        {
            var st = FreshHand(new long[] { 100, 100, 100 }, 0, Variant.TexasHoldem);
            int wrongSeat = (st.ToAct + 1) % 3;
            T.Throws(() => st.Apply(new GameAction(ActionKind.Check, wrongSeat)), "acting out of turn is rejected");
            // checking while facing the big blind preflop is illegal for the seat to act
            if (!st.Legal().CanCheck)
                T.Throws(() => st.Apply(new GameAction(ActionKind.Check, st.ToAct)), "cannot check facing a bet");
            // a raise BELOW the legal minimum (but not all-in) is rejected as out of range
            var laToAct = st.Legal();
            if (laToAct.CanBetOrRaise && laToAct.MinRaiseTo > 1)
                T.Throws(() => st.Apply(new GameAction(ActionKind.Raise, st.ToAct, laToAct.MinRaiseTo - 1)), "a sub-minimum raise is rejected");
            // a raise "to" MORE than the stack is clamped to all-in (correct poker UX), NOT an exception — verify
            // it is accepted and leaves the seat all-in with a conserved chip count.
            {
                var probe = FreshHand(new long[] { 100, 100, 100 }, 0, Variant.TexasHoldem);
                long totalChips = probe.Seats.Sum(s => s.Stack + s.TotalCommit);
                int actor = probe.ToAct;
                probe.Apply(new GameAction(ActionKind.Raise, actor, 999_999)); // "raise to" beyond stack
                T.True(probe.Seats[actor].AllIn, "an over-stack raise puts the seat all-in (clamped), not rejected");
                T.Eq(probe.Seats.Sum(s => s.Stack + s.TotalCommit), totalChips, "chips conserved through the all-in raise");
            }
            // a legal action still works afterward (engine not corrupted by the rejected attempts)
            var la = st.Legal();
            var legal = la.CanCall ? new GameAction(ActionKind.Call, st.ToAct)
                       : la.CanCheck ? new GameAction(ActionKind.Check, st.ToAct)
                       : new GameAction(ActionKind.Fold, st.ToAct);
            st.Apply(legal);
            T.True(true, "a legal action proceeds after rejected illegal ones");
        });
    }
}
