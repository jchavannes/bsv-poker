using System.Security.Cryptography;

namespace BsvPoker.Core;

/// <summary>
/// A simple PRACTICE bot (lobby "Play a bot"). It is NOT used in real money/P2P games — only when a
/// human chooses to practice against it. Heuristic: check when free; call modest bets; fold to big bets;
/// occasionally make a small raise for variety. Deterministic legality (always returns a legal action).
/// </summary>
public static class BotPolicy
{
    public static GameAction Decide(HoldemState st)
    {
        int seat = st.ToAct;
        var la = st.Legal();
        var me = st.Seats[seat];
        if (la.CanBetOrRaise && la.CanCheck && Roll() < 25) // sometimes bet when checked to
            return new GameAction(ActionKind.Raise, seat, la.MinRaiseTo);
        if (la.CanCheck) return new GameAction(ActionKind.Check, seat);
        if (la.CanCall)
        {
            // call modest bets; fold to a bet larger than ~half our stack (most of the time)
            if (la.CallAmount * 2 <= me.Stack || Roll() < 60) return new GameAction(ActionKind.Call, seat);
            return new GameAction(ActionKind.Fold, seat);
        }
        return new GameAction(ActionKind.Fold, seat);
    }

    private static int Roll() => (int)(RandomNumberGenerator.GetInt32(100));
}
