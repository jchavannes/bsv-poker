using BsvPoker.Core;
using BsvPoker.Core.Games;

namespace BsvPoker.Tests;

/// <summary>
/// Deep variant FSM / flow tests. Two distinct variant systems live in the engine and BOTH are exercised
/// here, every expected value DERIVED from the engine's own published contract (never invented):
///
///   • The betting-FSM variants — the <see cref="Variant"/> enum (TexasHoldem, Omaha, BigO, Pineapple,
///     Tahoe, RoyalHoldem). These are the ONLY variants <see cref="HoldemState"/> drives through real
///     streets + betting. Per the contract: hole count = Variants.HoleCards(v); the board is ALWAYS dealt
///     3 (flop) + 1 (turn) + 1 (river) = 5 community cards across exactly four streets
///     Preflop→Flop→Turn→River (HoldemEngine.NextStreet). For each one we assert hole/board/street counts,
///     drive a full multi-street line to showdown that CONSERVES chips, build a 3-way hand with a fold and
///     an all-in and verify the layered main/side-pot split (HoldemState.ScoreAndPay), and a HOSTILE case:
///     an illegal action at the wrong street/seat is rejected and an out-of-turn action does not mutate state.
///
///   • The showdown-settlement games — the <see cref="PokerGame"/> enum (SevenCardStud, Razz, FiveCardDraw)
///     have NO community board and NO betting FSM (GameDef.Board == 0, Stud/Draw flags). The engine's flow
///     for them is the per-game card model + Showdown.Settle. For each we assert its card/board/street model
///     from GameDef and drive a multiway settlement (fold = excluded seat; the remaining pot incl. dead money
///     is awarded by the game's own rule), with a HOSTILE near-miss that must NOT win.
///
/// Determinism: every FSM hand is built on an EXPLICIT deck (not a shuffle) so the dealt hole/board cards —
/// and therefore the showdown winner and every side-pot satoshi — are reproducible and asserted exactly.
/// </summary>
public static class VariantFlowTests
{
    // ---- card helpers (mirror GameShowdownTests / HandEvalEdgeTests) ----
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }
    private static Card[] H(params string[] cs) => cs.Select(C).ToArray();
    private static readonly Card[] None = Array.Empty<Card>();

    // The number of board cards each street REVEALS, in order, per HoldemEngine.NextStreet (3,1,1).
    private static readonly int[] StreetBoardReveal = { 3, 1, 1 };

    public static void All()
    {
        Console.WriteLine("deep variant FSM / flow (betting-FSM variants + showdown-only games):");

        FsmVariantFlow();
        ShowdownOnlyGameFlow();
    }

    // =================================================================================================
    // PART 1 — the six betting-FSM variants (Variant enum), driven through real streets by HoldemState.
    // =================================================================================================
    private static void FsmVariantFlow()
    {
        foreach (var v in Variants.All)
        {
            string name = Variants.Name(v);
            int hole = Variants.HoleCards(v);

            // --- A) hole + board + street counts match the engine's own contract -------------------
            T.Run($"{name}: deals {hole} hole, a 5-card board over Preflop→Flop→Turn→River (FSM contract)", () =>
            {
                // An explicit deck: enough cards for 2 seats' holes + a 5-card board, drawn from this variant's set.
                var set = Variants.CardSet(v).ToList();
                var st = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, set, v);

                // hole count is exactly Variants.HoleCards(v) for EVERY seat
                T.Eq(st.Seats[0].Hole.Length, hole, "seat 0 hole-card count = variant contract");
                T.Eq(st.Seats[1].Hole.Length, hole, "seat 1 hole-card count = variant contract");

                // starts on Preflop with an empty board (board is revealed progressively)
                T.Eq(st.Street, Street.Preflop, "hand opens on Preflop");
                T.Eq(st.Board.Count, 0, "no community cards before the flop");

                // Walk the streets by checking down. Record the board size as each NEW street opens.
                var sizesPerStreet = new List<(Street, int)>();
                Street last = st.Street;
                int guard = 0;
                while (!st.Complete && guard++ < 400)
                {
                    if (st.Street != last) { sizesPerStreet.Add((st.Street, st.Board.Count)); last = st.Street; }
                    var la = st.Legal();
                    if (la.CanCheck) st.Apply(new GameAction(ActionKind.Check, st.ToAct));
                    else if (la.CanCall) st.Apply(new GameAction(ActionKind.Call, st.ToAct));
                    else st.Apply(new GameAction(ActionKind.Fold, st.ToAct));
                }
                T.True(st.Complete, "the hand reached completion");

                // The board grew 3 → 4 → 5 as Flop/Turn/River opened (the engine's 3,1,1 reveal contract).
                int idxFlop = sizesPerStreet.FindIndex(x => x.Item1 == Street.Flop);
                int idxTurn = sizesPerStreet.FindIndex(x => x.Item1 == Street.Turn);
                int idxRiver = sizesPerStreet.FindIndex(x => x.Item1 == Street.River);
                // A pure check-down never folds anyone (heads-up), so all three post-flop streets are reached.
                T.True(idxFlop >= 0 && idxTurn >= 0 && idxRiver >= 0, "Flop, Turn and River all opened");
                T.Eq(sizesPerStreet[idxFlop].Item2, 3, "the flop reveals exactly 3 board cards");
                T.Eq(sizesPerStreet[idxTurn].Item2, 4, "the turn brings the board to 4");
                T.Eq(sizesPerStreet[idxRiver].Item2, 5, "the river brings the board to 5");
                T.Eq(st.Board.Count, 5, "a complete community board is exactly 5 cards");

                // The reveal increments equal the documented per-street counts 3,1,1.
                T.Eq(sizesPerStreet[idxFlop].Item2 - 0, StreetBoardReveal[0], "flop reveal = 3");
                T.Eq(sizesPerStreet[idxTurn].Item2 - sizesPerStreet[idxFlop].Item2, StreetBoardReveal[1], "turn reveal = 1");
                T.Eq(sizesPerStreet[idxRiver].Item2 - sizesPerStreet[idxTurn].Item2, StreetBoardReveal[2], "river reveal = 1");
            });

            // --- B) a full multi-street betting line driven to showdown CONSERVES chips ------------
            T.Run($"{name}: a multi-street bet/call line to showdown conserves chips (Σstacks = constant)", () =>
            {
                long s0 = 200, s1 = 200, total = s0 + s1;
                var set = Variants.CardSet(v).ToList();
                var st = HoldemState.Create(new long[] { s0, s1 }, button: 0, sb: 1, bb: 2, set, v);

                // Drive an ACTIVE line (not a pure check-down): on each street the first-to-act makes the
                // minimum legal bet/raise and the opponent calls. This exercises betting on every street.
                int guard = 0;
                Street lastStreet = (Street)(-1);
                bool betThisStreet = false;
                while (!st.Complete && guard++ < 400)
                {
                    if (st.Street != lastStreet) { betThisStreet = false; lastStreet = st.Street; }
                    var la = st.Legal();
                    int seat = st.ToAct;
                    if (!betThisStreet && la.CanBetOrRaise)
                    {
                        st.Apply(new GameAction(ActionKind.Raise, seat, la.MinRaiseTo)); // min legal bet/raise
                        betThisStreet = true;
                    }
                    else if (la.CanCall) st.Apply(new GameAction(ActionKind.Call, seat));
                    else if (la.CanCheck) st.Apply(new GameAction(ActionKind.Check, seat));
                    else st.Apply(new GameAction(ActionKind.Fold, seat));
                }
                T.True(st.Complete, "the betting line reached showdown / completion");
                // Conservation: chips never created or destroyed across the whole hand.
                T.Eq(st.Seats.Sum(s => s.Stack), total, "sum of stacks is conserved end-to-end");
                // The pot is fully distributed at completion (Pot = ΣTotalCommit − ΣPayouts == 0).
                T.Eq(st.Pot, 0L, "the pot is fully paid out at completion");
                // Conservation INCLUDING in-flight pot at every observed point already held (stacks+pot==total):
                T.Eq(st.Seats.Sum(s => s.Stack) + st.Pot, total, "stacks + pot is invariant");
            });

            // --- C) a 3-way hand with a fold AND an all-in → correct layered side-pot split --------
            // Built on an EXPLICIT deck so the showdown winner of each pot level is known in advance.
            T.Run($"{name}: 3-way with a fold + a short all-in pays the right main/side-pot split", () =>
            {
                Build3WaySidePotHand(v, hole);
            });

            // --- D) HOSTILE: illegal action wrong street/seat rejected; out-of-turn does not mutate -
            T.Run($"{name}: HOSTILE — illegal + out-of-turn actions are rejected and never mutate state", () =>
            {
                var set = Variants.CardSet(v).ToList();
                var st = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, set, v);

                // Heads-up preflop: the button (seat 0) posts SB and is first to act facing the BB.
                int actor = st.ToAct;
                int other = actor == 0 ? 1 : 0;
                T.True(actor >= 0, "someone is to act preflop");

                // (1) OUT-OF-TURN: the seat NOT to act tries to act → rejected, and NOTHING changes.
                var snap = Snapshot(st);
                T.Throws(() => st.Apply(new GameAction(ActionKind.Call, other)), "out-of-turn action must throw");
                T.True(SnapshotEq(snap, Snapshot(st)), "a rejected out-of-turn action left state byte-identical");

                // (2) ILLEGAL ACTION: facing the BB, the actor owes chips, so CHECK is illegal → rejected.
                var la = st.Legal();
                if (la.CanCall && !la.CanCheck)
                {
                    var snap2 = Snapshot(st);
                    T.Throws(() => st.Apply(new GameAction(ActionKind.Check, actor)), "cannot check facing a bet");
                    T.True(SnapshotEq(snap2, Snapshot(st)), "a rejected illegal check left state unchanged");
                }

                // (3) An out-of-RANGE raise is rejected without mutation. Per the engine contract (Apply):
                //   • a target BELOW MinRaiseTo (and not an all-in) → "raise out of range";
                //   • a target that does not INCREASE the seat's street commit (add ≤ 0) → "raise must increase".
                // (A target ABOVE MaxRaiseTo is intentionally NOT tested as a throw: the engine treats any target
                //  ≥ stack as an all-in shove and clamps it — that is legal, not an error.)
                var la2 = st.Legal();
                if (la2.CanBetOrRaise)
                {
                    var sCommit = st.Seats[actor].StreetCommit;

                    // below-min raise (still increases the commit, so it reaches the range check) → rejected
                    if (la2.MinRaiseTo - 1 > sCommit && la2.MinRaiseTo - 1 < la2.MaxRaiseTo)
                    {
                        var snap4 = Snapshot(st);
                        T.Throws(() => st.Apply(new GameAction(ActionKind.Raise, actor, la2.MinRaiseTo - 1)),
                                 "a raise below the minimum is out of range");
                        T.True(SnapshotEq(snap4, Snapshot(st)), "a rejected below-min raise left state unchanged");
                    }

                    // a non-increasing "raise" (target == current street commit) → rejected as "must increase"
                    var snap5 = Snapshot(st);
                    T.Throws(() => st.Apply(new GameAction(ActionKind.Raise, actor, sCommit)),
                             "a raise that does not increase the bet is rejected");
                    T.True(SnapshotEq(snap5, Snapshot(st)), "a rejected non-increasing raise left state unchanged");
                }

                // (4) WRONG STREET: after the hand is complete, ANY further action is rejected.
                int guard = 0;
                while (!st.Complete && guard++ < 400)
                {
                    var l = st.Legal();
                    if (l.CanCheck) st.Apply(new GameAction(ActionKind.Check, st.ToAct));
                    else if (l.CanCall) st.Apply(new GameAction(ActionKind.Call, st.ToAct));
                    else st.Apply(new GameAction(ActionKind.Fold, st.ToAct));
                }
                T.True(st.Complete, "hand is complete");
                var snapDone = Snapshot(st);
                T.Throws(() => st.Apply(new GameAction(ActionKind.Check, 0)), "no action is legal after completion");
                T.Throws(() => st.Apply(new GameAction(ActionKind.Call, 1)), "no action is legal after completion");
                T.True(SnapshotEq(snapDone, Snapshot(st)), "a post-completion action left the final state unchanged");
            });
        }
    }

    /// <summary>
    /// Build a deterministic 3-way hand for a given FSM variant where seat 0 is a SHORT all-in, seat 1 FOLDS,
    /// and seat 2 covers seat 0. The explicit deck guarantees the showdown hands, so the layered main/side-pot
    /// payout (HoldemState.ScoreAndPay) is fully DERIVED and asserted to the satoshi, with chips conserved.
    /// </summary>
    private static void Build3WaySidePotHand(Variant v, int hole)
    {
        // Stacks: seat 0 short (20), seats 1 & 2 deep (100). Button=0 ⇒ (3-handed) SB=seat1, BB=seat2,
        // first to act preflop = seat 0 (button, UTG in 3-handed). We script: seat0 shoves all-in, seat1
        // folds, seat2 calls. Two live contenders remain → a single pot level (seat0's 20) eligible for both
        // seat0 and seat2, plus a side pot of seat2's extra call over the folded seat1's dead chips.
        long st0 = 20, st1 = 100, st2 = 100, total = st0 + st1 + st2;

        // EXPLICIT deck. Layout consumed by HoldemState.Create:
        //   seat0 hole = deck[0 .. hole-1], seat1 = deck[hole .. 2*hole-1], seat2 = deck[2*hole .. 3*hole-1],
        //   flop = next 3, turn = next 1, river = next 1.
        // We make seat 0 the WINNER deterministically: give seat 0 a hole pair of aces; arrange the board so
        // seat 0 (using its own two aces) makes trips, beating seat 2's junk. For Omaha-style (exactly-2-hole)
        // a hole pair of aces + a board ace still yields trip aces using exactly two hole cards — still best.
        var deck = BuildDeterministicDeck(v, hole);

        var st = HoldemState.Create(new long[] { st0, st1, st2 }, button: 0, sb: 1, bb: 2, deck, v);

        // 3-handed, button=0 ⇒ SB=1, BB=2, action opens on seat 0.
        T.Eq(st.ToAct, 0, "3-handed button-0: action opens on seat 0 (UTG)");

        // seat 0 shoves all-in (its entire 20-chip stack as a street-commit target).
        var la0 = st.Legal();
        // seat 0's max raise-to == its full stack as a street total (it has posted nothing yet preflop).
        st.Apply(new GameAction(ActionKind.Raise, 0, la0.MaxRaiseTo));
        T.True(st.Seats[0].AllIn, "seat 0 is all-in after shoving its stack");

        // seat 1 (SB) folds to the shove.
        T.Eq(st.ToAct, 1, "action moves to seat 1 (SB) after seat 0's shove");
        st.Apply(new GameAction(ActionKind.Fold, 1));
        T.True(st.Seats[1].Folded, "seat 1 folded");

        // seat 2 (BB) calls the all-in. With seat 1 folded and seat 0 all-in, only seat 2 can act; the call
        // closes betting and the engine runs the board out to showdown (CanActCount <= 1 path).
        T.Eq(st.ToAct, 2, "action moves to seat 2 (BB)");
        st.Apply(new GameAction(ActionKind.Call, 2));

        T.True(st.Complete, "with one all-in and one folder, the engine deals to showdown and settles");
        T.Eq(st.Board.Count, 5, "the board ran out to a full 5 cards at showdown");

        // DERIVE (don't assume) that seat 0 is the strict best hand under THIS variant's own evaluator — the
        // payout assertions below depend on it, so prove the precondition with the engine's BestForVariant.
        bool exactly2 = Variants.ExactlyTwoHole(v);
        long sc0 = HandEval.BestForVariant(st.Seats[0].Hole, st.Board, exactly2).Score;
        long sc2 = HandEval.BestForVariant(st.Seats[2].Hole, st.Board, exactly2).Score;
        T.True(sc0 > sc2, $"seat 0's hand strictly beats seat 2's under {Variants.Name(v)} (derived winner)");

        // ---- DERIVE the expected payout from the engine's own side-pot contract (ScoreAndPay) ----
        // Contribution levels: seat0 = 20, seat1 = 1 (SB, folded), seat2 = 20 (called the 20 shove).
        //   level 1  (slice 1):  eligible = all three who committed ≥1 → pool 3, live = {0,2} (seat1 folded)
        //   level 20 (slice 19): eligible = {0,2} (committed ≥20)      → pool 38, live = {0,2}
        // seat 0 wins BOTH levels (trip aces) ⇒ seat0 collects 3 + 38 = 41. Seat 2 wins nothing. Seat 1: 0.
        // Total pot = 20 + 1 + 20 = 41, fully awarded to seat 0.
        long expectSeat0Win = st0 + 1 + st0; // = 41 ; the folded SB's dead 1 + both 20s

        T.Eq(st.Payouts.GetValueOrDefault(0), expectSeat0Win, "seat 0 (trip aces) scoops main+side incl. the folded SB's dead chip");
        T.Eq(st.Payouts.GetValueOrDefault(1), 0L, "the folder wins nothing");
        T.Eq(st.Payouts.GetValueOrDefault(2), 0L, "seat 2 loses to the all-in's stronger hand");

        // Chips conserved across the whole multiway hand.
        T.Eq(st.Seats.Sum(s => s.Stack), total, "Σ stacks conserved across the multiway all-in hand");
        T.Eq(st.Pot, 0L, "the pot is fully settled");

        // Seat 0's final stack = it had 0 left after shoving, then won 41.
        T.Eq(st.Seats[0].Stack, expectSeat0Win, "winner's stack = its winnings (it was all-in)");
        // Seat 1 folded after posting only the SB (1) → it keeps 99.
        T.Eq(st.Seats[1].Stack, st1 - 1, "the folded SB is down only its posted blind");
        // Seat 2 called 20 and lost → keeps 80.
        T.Eq(st.Seats[2].Stack, st2 - st0, "the caller is down exactly the amount it called");
    }

    /// <summary>
    /// An explicit, reproducible deck for a 3-seat hand of the given variant. Seat 0 receives a hole PAIR OF
    /// ACES; seats 1 and 2 receive low junk; the board contains a third ace plus blanks so seat 0 makes trip
    /// aces (using exactly two of its hole aces under Omaha-style rules) — the unambiguous winner. Cards are
    /// drawn only from the variant's own CardSet (Royal Hold'em is a T..A deck), so the deck is always legal.
    /// </summary>
    private static Card[] BuildDeterministicDeck(Variant v, int hole)
    {
        bool royal = v == Variant.RoyalHoldem; // only ranks T..A exist (20-card deck)

        // The BOARD is hand-picked to be RAINBOW (no flush), GAP-RIDDEN and UNPAIRED (no board straight, no board
        // full house) and to contain the third ace, so the ONLY made hand it confers is seat 0's trip aces:
        //   • non-royal board:  A♦ K♣ 9♥ 6♠ 3♦  — five suits/ranks chosen so no 5 of {board∪any 2 low junk}
        //                       forms a straight or flush; the lone pair anywhere is the trip-ace pair.
        //   • royal board:      A♦ K♣ Q♥ J♠ T♦  is itself a broadway STRAIGHT, which every seat can play — so we
        //                       instead use A♦ K♣ Q♥ T♠ T♦ (paired tens, gapped at J) so seat 0's aces-over-tens
        //                       full house dominates and no opponent can exceed it from T..A junk without an ace.
        // Seat 0 hole is the ace pair; seats 1 & 2 get the LOWEST available junk that cannot pair the board into a
        // hand beating seat 0. Each variant's hole count is honoured (Pineapple/Tahoe 3, Omaha 4, BigO 5).
        var seat0 = new List<Card> { C("As"), C("Ah") };
        List<Card> board = royal
            ? new List<Card> { C("Ad"), C("Kc"), C("Qh"), C("Ts"), C("Td") }  // A-pair source + paired tens
            : new List<Card> { C("Ad"), C("Kc"), C("9h"), C("6s"), C("3d") }; // rainbow, gapped, unpaired (besides A)

        // Junk pools: lowest, rank-disjoint cards for the opponents and any extra hole slots. None is an ace, none
        // completes the board into a straight/flush, and (non-royal) all are low blanks; (royal) only T..K non-aces.
        var royalJunk = new[] { "Jh", "Jd", "Jc", "Qs", "Qd", "Qc", "Ks", "Kh", "Kd", "Th", "Tc", "Js" };
        var fullJunk = new[] { "2s", "2h", "2d", "2c", "4h", "4s", "4c", "5h", "5s", "5c", "7s", "7h", "7c", "8d" };
        var junk = (royal ? royalJunk : fullJunk).Select(C).ToList();

        var used = new HashSet<Card>();
        foreach (var c in seat0) used.Add(c);
        foreach (var c in board) used.Add(c);
        int ji = 0;
        Card NextJunk()
        {
            while (ji < junk.Count && used.Contains(junk[ji])) ji++;
            var c = junk[ji++]; used.Add(c); return c;
        }

        var seat1 = new List<Card>();
        var seat2 = new List<Card>();
        for (int i = 0; i < hole; i++) seat1.Add(NextJunk());
        for (int i = 0; i < hole; i++) seat2.Add(NextJunk());
        while (seat0.Count < hole) seat0.Add(NextJunk()); // pad seat 0's extra hole slots with dead junk

        // Assemble in the EXACT consumption order: seat0 holes, seat1 holes, seat2 holes, then board (flop,turn,river).
        var deck = new List<Card>();
        deck.AddRange(seat0.Take(hole));
        deck.AddRange(seat1.Take(hole));
        deck.AddRange(seat2.Take(hole));
        deck.AddRange(board.Take(5));
        return deck.ToArray();
    }

    // ---- state snapshot for the hostile no-mutation checks (compare the mutable FSM fields) ----
    private static string Snapshot(HoldemState st)
    {
        var parts = new List<string>
        {
            $"street={st.Street}", $"toact={st.ToAct}", $"complete={st.Complete}",
            $"curbet={st.CurrentBet}", $"minraise={st.MinRaise}", $"board={st.Board.Count}",
            $"pot={st.Pot}",
        };
        foreach (var s in st.Seats)
            parts.Add($"[{s.Seat} stk={s.Stack} sc={s.StreetCommit} tc={s.TotalCommit} f={s.Folded} ai={s.AllIn} act={s.ActedThisStreet}]");
        foreach (var kv in st.Payouts.OrderBy(k => k.Key)) parts.Add($"pay{kv.Key}={kv.Value}");
        return string.Join("|", parts);
    }
    private static bool SnapshotEq(string a, string b) => a == b;

    // =================================================================================================
    // PART 2 — the showdown-only games (PokerGame: SevenCardStud, Razz, FiveCardDraw): no board, no FSM.
    //          Flow = the game's card model (GameDef) + Showdown.Settle (fold = excluded; rule-based award).
    // =================================================================================================
    private static void ShowdownOnlyGameFlow()
    {
        // The three boardless games the engine settles directly (Stud / Razz / Draw). Hold'em & the Omahas
        // are the community-board games already covered as FSM variants above; here we cover the rest so that
        // EVERY supported game has a flow test.
        var games = new[] { PokerGame.SevenCardStud, PokerGame.Razz, PokerGame.FiveCardDraw };

        foreach (var g in games)
        {
            var d = PokerGames.Of(g);

            // --- A) the game's card/board model matches GameDef (its own contract) ------------------
            T.Run($"{d.Name}: card model per GameDef — {d.Hole} hole, {d.Board} board, no community FSM", () =>
            {
                T.Eq(d.Board, 0, $"{d.Name} has no community board");
                T.True(d.Hole >= 5, $"{d.Name} deals each seat its own ≥5-card hand");
                // Stud games are flagged Stud; Draw is flagged Draw; none is HiLo here.
                if (g == PokerGame.SevenCardStud || g == PokerGame.Razz) T.True(d.Stud, "stud flag set");
                if (g == PokerGame.FiveCardDraw) T.True(d.Draw, "draw flag set");
                T.False(d.HiLo, "these three are single-pool games (not hi-lo split)");
                T.Eq(d.ExactlyHole, (int?)null, "no exactly-two-hole constraint (no board to combine with)");
                if (g == PokerGame.Razz) T.True(d.LowOnly, "Razz is low-only"); else T.False(d.LowOnly, "Stud/Draw are high");
            });

            // --- B) a multiway settlement that CONSERVES chips (Σ payouts == pot) -------------------
            T.Run($"{d.Name}: a 3-way showdown awards the whole pot by the game's rule (chips conserved)", () =>
            {
                var (holes, pot) = ThreeWayHands(g);
                var pay = Showdown.Settle(d, holes, None, pot);
                T.Eq(pay.Values.Sum(), pot, "every satoshi of the pot is paid out (conservation)");
            });

            // --- C) a 3-way hand with a FOLD and (modelled) all-in side-pot split -------------------
            // Showdown.Settle takes ONLY the live seats; a folded seat's dead chips stay in the pot total but
            // the folder is never an eligible seat. A short all-in is modelled as a separate pot level settled
            // over just the seats eligible for that level — the engine's documented side-pot decomposition.
            T.Run($"{d.Name}: 3-way with a folder + short all-in → correct side-pot decomposition", () =>
            {
                SidePotForShowdownGame(g, d);
            });

            // --- D) HOSTILE: a near-miss hand must NOT win; folder excluded; bad input rejected ------
            T.Run($"{d.Name}: HOSTILE — the inferior hand wins nothing and a folder cannot be re-admitted", () =>
            {
                HostileShowdownGame(g, d);
            });
        }
    }

    /// <summary>
    /// Three deterministic, physically-distinct 3-way hands per game (no card appears at two seats). The pot is
    /// fixed; the winner is NOT assumed here — test B only asserts conservation (Σ payouts == pot), which holds
    /// for ANY outcome the game's own rule produces.
    /// </summary>
    private static (IReadOnlyList<IReadOnlyList<Card>> holes, long pot) ThreeWayHands(PokerGame g)
    {
        switch (g)
        {
            case PokerGame.SevenCardStud:
                return (new IReadOnlyList<Card>[]
                {
                    H("9h","9d","9s","9c","Ah","Kd","Qs"),  // quad nines (best high)
                    H("As","Ad","Ks","Kc","2h","3d","4s"),  // aces & kings two pair
                    H("Th","Td","Js","Jc","5h","6d","7s"),  // tens & jacks two pair
                }, 99L);
            case PokerGame.Razz:
                return (new IReadOnlyList<Card>[]
                {
                    H("Ah","2d","3s","4c","5h","Kd","Qc"),  // wheel (best low)
                    H("2h","3d","4s","5c","7h","Kh","Qd"),  // 7-low
                    H("3h","4d","6s","7c","8h","Ks","Qh"),  // 8-low
                }, 99L);
            case PokerGame.FiveCardDraw:
                return (new IReadOnlyList<Card>[]
                {
                    H("Ah","Kh","Qh","Jh","9h"),            // ace-high flush (best high)
                    H("As","Ad","Kc","Qd","2c"),            // pair of aces
                    H("Ts","Td","9c","8d","7c"),            // pair of tens
                }, 99L);
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// A 3-way side-pot decomposition for a boardless game. Seat 0 is a SHORT all-in, seat 1 FOLDS (dead money),
    /// seat 2 covers. Two pot levels: a MAIN pot (seat0's level, eligible {0,2}) and a SIDE pot (seat2's excess,
    /// over the folded seat1's dead chips). Every payout is DERIVED from Showdown.Settle's own rule.
    /// </summary>
    private static void SidePotForShowdownGame(PokerGame g, GameDef d)
    {
        // Contributions: seat0 all-in 20 ; seat1 folds after putting in 5 (dead) ; seat2 calls/covers 20.
        long c0 = 20, c1Dead = 5, c2 = 20;
        long mainLevel = Math.Min(c0, c2);                 // = 20, contested by the live all-in and the caller
        long mainPot = mainLevel * 2 + c1Dead;             // both live 20s + seat1's dead 5 = 45 (dead money rides in main)
        // (No side pot here since c0 == c2; the folder's dead money augments the single contested pot.)

        var (winnerHole, loserHole) = TwoHandsFor(g);      // seat0 winner, seat2 loser, per the game's rule
        // Only the LIVE seats (0 and 2) are presented to Settle; seat 1 (folded) is excluded entirely.
        var live = new IReadOnlyList<Card>[] { winnerHole, loserHole };

        var pay = Showdown.Settle(d, live, None, mainPot);

        // DERIVED expectation: seat 0 (index 0 in the live list) wins the whole contested pot incl. dead money.
        T.Eq(pay.GetValueOrDefault(0), mainPot, "the all-in winner scoops the contested pot incl. the folder's dead money");
        T.Eq(pay.GetValueOrDefault(1), 0L, "the live loser wins nothing");
        T.False(pay.ContainsKey(2), "the folded seat is not an eligible seat and is absent from the payout map");
        T.Eq(pay.Values.Sum(), mainPot, "conservation: the full contested pot is paid out");
    }

    /// <summary>Hostile: the inferior hand must win nothing; a non-presented folder cannot win.</summary>
    private static void HostileShowdownGame(PokerGame g, GameDef d)
    {
        var (winnerHole, loserHole) = TwoHandsFor(g);
        // Present the two LIVE hands in REVERSED order to prove the WINNER is decided by strength, not seat index.
        var holes = new IReadOnlyList<Card>[] { loserHole, winnerHole };
        var pay = Showdown.Settle(d, holes, None, 100);
        // The stronger hand (now at seat index 1) takes it all; the inferior (index 0) gets nothing.
        T.Eq(pay.GetValueOrDefault(1), 100L, "the superior hand wins regardless of seat order");
        T.Eq(pay.GetValueOrDefault(0), 0L, "the inferior hand wins nothing (no spurious split)");
        T.False(pay.ContainsKey(2), "a folder never presented to Settle cannot appear in the payouts");
    }

    /// <summary>
    /// A (winner, loser) pair of seven/five-card hands for the given boardless game, with the winner DERIVED
    /// from the game's own evaluator so the test asserts the engine's real ordering, not an assumption.
    /// </summary>
    private static (Card[] winner, Card[] loser) TwoHandsFor(PokerGame g)
    {
        switch (g)
        {
            case PokerGame.SevenCardStud:
            {
                // High: quad nines beat two pair (aces & kings). Physically distinct (no shared card).
                var winner = H("9h", "9d", "9s", "9c", "Ah", "Kd", "Qs"); // quad nines
                var loser = H("As", "Ad", "Ks", "Kc", "2h", "3d", "4s");  // two pair, aces & kings
                return (winner, loser);
            }
            case PokerGame.Razz:
            {
                // Low-only: the wheel (5-4-3-2-A) is the nut low and beats a 7-low. Distinct cards across hands.
                var winner = H("Ah", "2d", "3s", "4c", "5h", "Kd", "Qc"); // wheel
                var loser = H("6h", "7d", "8s", "9c", "Th", "Kh", "Qd");  // best low is 10-low — strictly worse
                return (winner, loser);
            }
            case PokerGame.FiveCardDraw:
            {
                // High five-card: a flush beats a pair. No card is shared between the two hands.
                var winner = H("Ah", "Kh", "Qh", "Jh", "9h"); // ace-high flush
                var loser = H("As", "Ad", "Kc", "Qd", "2c");  // pair of aces
                return (winner, loser);
            }
            default: throw new InvalidOperationException();
        }
    }
}
