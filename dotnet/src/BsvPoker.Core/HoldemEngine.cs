namespace BsvPoker.Core;

public enum Street { Preflop, Flop, Turn, River, Complete }
public enum ActionKind { Fold, Check, Call, Bet, Raise }

public readonly record struct GameAction(ActionKind Kind, int Seat, long Amount = 0);

public sealed class SeatState
{
    public int Seat;
    public long Stack;
    public Card[] Hole = Array.Empty<Card>();
    public bool Folded;
    public bool AllIn;
    public long StreetCommit;   // chips put in on the current street
    public long TotalCommit;    // chips put in this hand (for side-pot layering)
    public bool ActedThisStreet;
}

public sealed class LegalAction
{
    public bool CanFold, CanCheck, CanCall, CanBetOrRaise;
    public long CallAmount;     // chips to call
    public long MinRaiseTo, MaxRaiseTo; // total street commit a bet/raise targets
}

/// <summary>
/// Deterministic multiway Texas Hold'em: blinds, betting with correct min-raise + all-in handling,
/// street progression, and showdown with layered main/side pots (chips are always conserved). Pure —
/// the only randomness is the agreed mental-poker deck passed to <see cref="Create"/>.
/// </summary>
public sealed class HoldemState
{
    public List<SeatState> Seats = new();
    public List<Card> Board = new();         // up to 5, revealed progressively
    private List<Card> _deck = new();
    private int _deckPos;
    public Street Street = Street.Preflop;
    public int Button;
    public long SmallBlind, BigBlind;
    public long CurrentBet;                   // highest street commit to match
    public long MinRaise;                     // minimum raise increment
    public int ToAct = -1;
    public bool Complete;
    public string Message = "";
    public Variant Variant = Variant.TexasHoldem;
    public Dictionary<int, long> Payouts = new(); // seat -> chips won at showdown

    public long Pot => Seats.Sum(s => s.TotalCommit) - Payouts.Values.Sum();

    public static HoldemState Create(IReadOnlyList<long> stacks, int button, long sb, long bb, IReadOnlyList<Card> deck, Variant variant = Variant.TexasHoldem)
    {
        if (stacks.Count < 2) throw new ArgumentException("need >= 2 players");
        var st = new HoldemState { Button = button, SmallBlind = sb, BigBlind = bb, _deck = deck.ToList(), Variant = variant };
        for (int i = 0; i < stacks.Count; i++) st.Seats.Add(new SeatState { Seat = i, Stack = stacks[i] });
        // hole cards (the variant's count)
        int holeCount = Variants.HoleCards(variant);
        foreach (var s in st.Seats) s.Hole = Enumerable.Range(0, holeCount).Select(_ => st._deck[st._deckPos++]).ToArray();
        // blinds (heads-up: button posts SB)
        int n = st.Seats.Count;
        int sbSeat = n == 2 ? button : (button + 1) % n;
        int bbSeat = n == 2 ? (button + 1) % n : (button + 2) % n;
        st.PostBlind(sbSeat, sb);
        st.PostBlind(bbSeat, bb);
        st.CurrentBet = bb;
        st.MinRaise = bb;
        st.ToAct = NextActive(st, bbSeat);
        st.Message = "Preflop";
        return st;
    }

    private void PostBlind(int seat, long amt)
    {
        var s = Seats[seat];
        long pay = Math.Min(amt, s.Stack);
        s.Stack -= pay; s.StreetCommit += pay; s.TotalCommit += pay;
        if (s.Stack == 0) s.AllIn = true;
    }

    private static int NextActive(HoldemState st, int from)
    {
        int n = st.Seats.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = (from + k) % n;
            var s = st.Seats[i];
            if (!s.Folded && !s.AllIn) return i;
        }
        return -1;
    }

    private int ActiveCount() => Seats.Count(s => !s.Folded);
    private int CanActCount() => Seats.Count(s => !s.Folded && !s.AllIn);

    public LegalAction Legal()
    {
        var la = new LegalAction();
        if (Complete || ToAct < 0) return la;
        var s = Seats[ToAct];
        long toCall = CurrentBet - s.StreetCommit;
        la.CanFold = true;
        la.CanCheck = toCall <= 0;
        la.CanCall = toCall > 0 && s.Stack > 0;
        la.CallAmount = Math.Min(toCall, s.Stack);
        // a bet/raise targets a new street-commit total; min is CurrentBet+MinRaise, max is all-in.
        long maxTo = s.StreetCommit + s.Stack;
        long minTo = CurrentBet <= 0 ? BigBlind : CurrentBet + MinRaise;
        if (maxTo > CurrentBet && s.Stack > 0)
        {
            la.CanBetOrRaise = true;
            la.MinRaiseTo = Math.Min(minTo, maxTo);
            la.MaxRaiseTo = maxTo;
        }
        return la;
    }

    /// <summary>Apply a legal action; throws on an illegal one (the engine is the sole adjudicator).</summary>
    public void Apply(GameAction a)
    {
        if (Complete) throw new InvalidOperationException("hand complete");
        if (a.Seat != ToAct) throw new InvalidOperationException($"not seat {a.Seat}'s turn");
        var s = Seats[ToAct];
        long toCall = CurrentBet - s.StreetCommit;
        switch (a.Kind)
        {
            case ActionKind.Fold:
                s.Folded = true;
                break;
            case ActionKind.Check:
                if (toCall > 0) throw new InvalidOperationException("cannot check facing a bet");
                break;
            case ActionKind.Call:
                if (toCall <= 0) throw new InvalidOperationException("nothing to call");
                Commit(s, Math.Min(toCall, s.Stack));
                break;
            case ActionKind.Bet:
            case ActionKind.Raise:
            {
                var la = Legal();
                if (!la.CanBetOrRaise) throw new InvalidOperationException("cannot bet/raise");
                long target = a.Amount; // total street commit to reach
                bool allIn = target >= s.StreetCommit + s.Stack;
                if (!allIn && (target < la.MinRaiseTo || target > la.MaxRaiseTo)) throw new InvalidOperationException("raise out of range");
                long add = Math.Min(target, s.StreetCommit + s.Stack) - s.StreetCommit;
                if (add <= 0) throw new InvalidOperationException("raise must increase the bet");
                long raiseIncrement = (s.StreetCommit + add) - CurrentBet;
                Commit(s, add);
                if (s.StreetCommit > CurrentBet)
                {
                    if (raiseIncrement >= MinRaise) MinRaise = raiseIncrement; // full raise reopens action
                    CurrentBet = s.StreetCommit;
                    foreach (var o in Seats) if (o != s && !o.Folded && !o.AllIn) o.ActedThisStreet = false; // reopen
                }
                break;
            }
        }
        s.ActedThisStreet = true;
        Advance();
    }

    private void Commit(SeatState s, long amt)
    {
        amt = Math.Min(amt, s.Stack);
        s.Stack -= amt; s.StreetCommit += amt; s.TotalCommit += amt;
        if (s.Stack == 0) s.AllIn = true;
    }

    private void Advance()
    {
        if (ActiveCount() == 1) { Settle(); return; } // everyone else folded
        // betting closed when every non-folded, non-all-in seat has acted and matched CurrentBet
        bool closed = Seats.Where(s => !s.Folded && !s.AllIn).All(s => s.ActedThisStreet && s.StreetCommit == CurrentBet);
        if (CanActCount() <= 1 && Seats.Where(s => !s.Folded && !s.AllIn).All(s => s.ActedThisStreet)) closed = true;
        if (!closed)
        {
            int nxt = NextActive(this, ToAct);
            if (nxt < 0) closed = true; else { ToAct = nxt; return; }
        }
        // street complete → next street (or showdown)
        NextStreet();
    }

    private void NextStreet()
    {
        foreach (var s in Seats) { s.StreetCommit = 0; s.ActedThisStreet = false; }
        CurrentBet = 0; MinRaise = BigBlind;
        switch (Street)
        {
            case Street.Preflop: Board.AddRange(new[] { _deck[_deckPos++], _deck[_deckPos++], _deck[_deckPos++] }); Street = Street.Flop; Message = "Flop"; break;
            case Street.Flop: Board.Add(_deck[_deckPos++]); Street = Street.Turn; Message = "Turn"; break;
            case Street.Turn: Board.Add(_deck[_deckPos++]); Street = Street.River; Message = "River"; break;
            case Street.River: Settle(); return;
        }
        // if <=1 can act (everyone else all-in), keep dealing streets to showdown with no betting
        if (CanActCount() <= 1) { if (Street != Street.River) NextStreet(); else Settle(); return; }
        ToAct = NextActive(this, Button);
        if (ToAct < 0) NextStreet();
    }

    private void Settle()
    {
        Complete = true; Street = Street.Complete;
        var contenders = Seats.Where(s => !s.Folded).ToList();
        if (contenders.Count == 1)
        {
            long won = Seats.Sum(s => s.TotalCommit);
            contenders[0].Stack += won;
            Payouts[contenders[0].Seat] = won;
            Message = $"Seat {contenders[0].Seat} wins {won} (all others folded)";
            return;
        }
        // layered side pots by distinct total-commit levels
        var levels = Seats.Select(s => s.TotalCommit).Where(v => v > 0).Distinct().OrderBy(v => v).ToList();
        long prev = 0;
        var won2 = new Dictionary<int, long>();
        foreach (var lvl in levels)
        {
            long slice = lvl - prev;
            var eligible = Seats.Where(s => s.TotalCommit >= lvl).ToList();
            long potAtLevel = slice * eligible.Count;
            var live = eligible.Where(s => !s.Folded).ToList();
            if (live.Count == 0) { prev = lvl; continue; }
            // best hand(s) among live contenders at this level
            bool exactly2 = Variants.ExactlyTwoHole(Variant);
            var scored = live.Select(s => (s.Seat, Score: HandEval.BestForVariant(s.Hole, Board, exactly2).Score)).ToList();
            long best = scored.Max(x => x.Score);
            var winners = scored.Where(x => x.Score == best).Select(x => x.Seat).ToList();
            long each = potAtLevel / winners.Count;
            long rem = potAtLevel - each * winners.Count;
            foreach (var w in winners) won2[w] = won2.GetValueOrDefault(w) + each;
            if (rem > 0) { var oddSeat = winners.OrderBy(x => x).First(); won2[oddSeat] += rem; } // odd chip left-of-button-ish
            prev = lvl;
        }
        foreach (var (seat, amt) in won2) { Seats[seat].Stack += amt; Payouts[seat] = amt; }
        Message = "Showdown: " + string.Join(", ", won2.Select(kv => $"seat {kv.Key} +{kv.Value}"));
    }
}
