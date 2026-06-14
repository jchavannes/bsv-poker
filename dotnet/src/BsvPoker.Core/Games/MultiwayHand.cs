using System.Security.Cryptography;
using BsvPoker.Crypto;

namespace BsvPoker.Core.Games;

/// <summary>
/// MULTIWAY (N-player) dealerless on-chain hand — the capstone that composes the proven organs:
///   • the real commutative-encryption shuffle+deal (<see cref="MentalPokerEC"/>) for N players, with HOLE
///     privacy kept n-of-n (every OTHER player hands the recipient their per-card scalar; the recipient's own
///     scalar is mandatory, so a sub-coalition can NEVER read a hole card),
///   • the ZERO-CONF resilient move-chain (<see cref="ZeroConfMoveChain"/>) — the n-of-n committed pot; every
///     deal/board/bet step is its own n-of-n move tx (no double-spend at 0-conf, no out-of-rules move),
///   • game-bound NFTs (<see cref="CardNft.NftLockForGame"/>) — each dealt card is minted permanently bound to
///     THIS game (D-A).
/// Pure composition of tested primitives; the live engine drives this over the mesh with every player funding,
/// co-signing and dual-path broadcasting their own moves.
/// </summary>
public static class MultiwayHand
{
    public sealed record Player(byte[] Priv, byte[] Pub);
    public sealed record DealtCard(int Seat, int Position, int CardIndex, string SealedNftHex, byte[] NftLock);
    public sealed record MoveStep(TxKind Kind, Chain.Tx Tx, long CommittedValue);
    public sealed record Hand(
        IReadOnlyList<DealtCard> Cards,
        IReadOnlyList<MoveStep> Moves,
        ZeroConfMoveChain.Committed FinalPot,
        byte[] GameId,
        byte[] GameDetailsHash);

    /// <summary>
    /// Run a full multiway deal + zero-conf move-tape for <paramref name="players"/>, dealing
    /// <paramref name="holePerSeat"/> private cards to each seat. Returns each seat's cards (game-bound NFTs),
    /// the n-of-n move-chain (one move per dealt card), and the continuing committed pot.
    /// </summary>
    public static Hand Deal(IReadOnlyList<Player> players, int deckSize, int holePerSeat,
        long pot, string potTxid, byte[] tableId, byte variant, long stakes, long fee = 1)
    {
        int P = players.Count;
        if (P < 2) throw new ArgumentException("need >= 2 players");
        int positions = P * holePerSeat;
        if (positions > deckSize) throw new ArgumentException("not enough cards for the seats");
        var pubs = players.Select(p => p.Pub).ToList();
        var gameId = RandomNumberGenerator.GetBytes(16);
        var handId = RandomNumberGenerator.GetBytes(16);
        var detailsHash = CardNft.GameDetailsHash(tableId, gameId, variant, (byte)P, stakes, pubs);

        // --- the real multiway commutative shuffle: each player masks (its OWN global) + permutes in turn ---
        var deck = MentalPokerEC.BaseDeck(deckSize);
        var globals = new byte[P][];
        for (int p = 0; p < P; p++)
        {
            globals[p] = MentalPokerEC.NewScalar();
            deck = MentalPokerEC.ShuffleMask(deck, globals[p], RandPerm(deckSize));
        }
        // now deck[k] = (∏_p globals_p) · M_{σ(k)}

        // --- remask: each player STRIPS its OWN shuffle global and applies independent per-card scalars ---
        var perCardByPlayer = new byte[P][][];
        for (int p = 0; p < P; p++)
        {
            var d = MentalPokerEC.NewPerCardScalars(deckSize);
            deck = MentalPokerEC.Remask(deck, globals[p], d);               // strip g_p, apply this player's per-card d's
            perCardByPlayer[p] = d;
        }
        // now deck[k] = (∏_p d_{p,k}) · M_{σ(k)} — every shuffle global stripped, only per-card scalars remain

        // --- private deal: seat s gets positions [s*hole .. ), unmasked with EVERY player's per-card scalar ---
        var cards = new List<DealtCard>(positions);
        for (int s = 0; s < P; s++)
            for (int h = 0; h < holePerSeat; h++)
            {
                int k = s * holePerSeat + h;
                var scalars = Enumerable.Range(0, P).Select(p => perCardByPlayer[p][k]).ToList();   // all players' d for k
                var basePoint = MentalPokerEC.Unmask(deck[k], scalars);
                int idx = MentalPokerEC.Identify(basePoint, deckSize);
                if (idx < 0) throw new InvalidOperationException("deal failed to recover a card (mask mismatch)");
                var sealedHex = CardNft.SealToPub(idx, RandomNumberGenerator.GetBytes(32), players[s].Pub);
                var nft = CardNft.NftLockForGame(sealedHex, players[s].Pub, gameId, detailsHash);
                cards.Add(new DealtCard(s, k, idx, sealedHex, nft));
            }

        // --- the move-tape: the n-of-n committed pot, one zero-conf Deal move per dealt position ---
        var moves = new List<MoveStep>();
        var cur = new ZeroConfMoveChain.Committed(potTxid, 0, pot, pubs);
        foreach (var c in cards)
        {
            var fields = new[] { handId, new[] { (byte)c.Position }, CardNft.SealCommitment(c.SealedNftHex) };
            var move = ZeroConfMoveChain.BuildMove(cur.Txid, cur.Vout, cur.Value, pubs, TxKind.Deal, players[c.Seat].Pub, fields, fee);
            var sigs = players.Select(pl => ZeroConfMoveChain.Sign(move, pubs, cur.Value, pl.Priv)).ToList();
            var signed = ZeroConfMoveChain.Apply(move, sigs);
            moves.Add(new MoveStep(TxKind.Deal, signed, cur.Value));
            cur = ZeroConfMoveChain.Next(signed, pubs);
        }

        return new Hand(cards, moves, cur, gameId, detailsHash);
    }

    /// <summary>Can seat <paramref name="seat"/> identify the card at <paramref name="position"/> given ONLY the
    /// per-card scalars it legitimately holds (its own)? Used to prove HOLE privacy: a non-recipient cannot.</summary>
    public static bool CanSeatIdentify(byte[][] remaskedDeck, int position, IReadOnlyList<byte[]> scalarsHeld, int deckSize)
    {
        try { return MentalPokerEC.Identify(MentalPokerEC.Unmask(remaskedDeck[position], scalarsHeld), deckSize) >= 0; }
        catch { return false; }
    }

    private static int[] RandPerm(int n)
    {
        var a = Enumerable.Range(0, n).ToArray();
        for (int i = n - 1; i > 0; i--) { int j = RandomNumberGenerator.GetInt32(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }
}
