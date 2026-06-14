using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Multiway (N-player) dealerless on-chain hand: a real commutative-encryption deal with n-of-n HOLE privacy,
/// each card minted as a game-bound NFT, every deal step a zero-conf n-of-n move. Proves: each seat opens only
/// its own cards, no two seats share a card, cards are bound to the game, the move-chain verifies + conserves
/// value, and a non-recipient cannot read a hole card (mental-poker privacy).
/// </summary>
public static class MultiwayHandTests
{
    public static void All()
    {
        Console.WriteLine("multiway dealerless on-chain hand (n-of-n privacy + zero-conf move-tape + game-bound NFTs):");
        const int deckSize = 52, holePerSeat = 2;
        var players = Enumerable.Range(0, 3).Select(_ => { var k = Secp256k1.GenerateKeyPair(); return new MultiwayHand.Player(k.Priv, k.Pub); }).ToList();
        var tableId = new byte[16]; tableId[0] = 9;
        long pot = 1000, fee = 1;
        var hand = MultiwayHand.Deal(players, deckSize, holePerSeat, pot, "aa".PadRight(64, '0'), tableId, variant: 0, stakes: pot, fee: fee);

        T.Run("every seat is dealt its hole cards and opens ONLY its own (the seal is private to the seat)", () =>
        {
            T.Eq(hand.Cards.Count, players.Count * holePerSeat, "one hole card per (seat × hole) position");
            foreach (var c in hand.Cards)
            {
                var opened = CardNft.Open(c.SealedNftHex, players[c.Seat].Priv);
                T.Eq(opened.CardIndex, c.CardIndex, $"seat {c.Seat} opens its own card at position {c.Position}");
                var otherSeat = (c.Seat + 1) % players.Count;
                T.False(CardNft.CanOpen(c.SealedNftHex, players[otherSeat].Priv), $"seat {otherSeat} CANNOT open seat {c.Seat}'s card");
            }
        });

        T.Run("no two seats receive the same card (mental-poker integrity)", () =>
        {
            var idxs = hand.Cards.Select(c => c.CardIndex).ToList();
            T.Eq(idxs.Distinct().Count(), idxs.Count, "all dealt cards are distinct");
        });

        T.Run("every dealt card is a game-bound NFT (D-A) — bound to THIS game, rejected for any other", () =>
        {
            var otherGame = new byte[16]; otherGame[0] = 0xEE;
            foreach (var c in hand.Cards)
            {
                T.True(CardNft.BelongsToGame(c.NftLock, hand.GameId, hand.GameDetailsHash), "card belongs to this game");
                T.False(CardNft.BelongsToGame(c.NftLock, otherGame, hand.GameDetailsHash), "card is rejected for a different game");
            }
        });

        T.Run("the zero-conf move-tape: every deal move verifies n-of-n and conserves value (− fee/move)", () =>
        {
            var pubs = players.Select(p => p.Pub).ToList();
            long expected = pot;
            foreach (var m in hand.Moves)
            {
                T.True(ZeroConfMoveChain.Verify(m.Tx, pubs, m.CommittedValue), "the deal move verifies on the n-of-n consensus path");
                T.Eq(m.CommittedValue, expected, "each move spends the prior committed value");
                T.Eq(m.Tx.Outs[0].Value, expected - fee, "the committed coin continues at value − fee");
                expected -= fee;
            }
            T.Eq(hand.FinalPot.Value, pot - hand.Moves.Count * fee, "final pot = pot − (moves × fee)");
        });

        T.Run("HOLE PRIVACY: the recipient (all per-card scalars) identifies the card; a non-recipient cannot", () =>
        {
            // a minimal 2-player remask scenario at the MentalPokerEC layer the multiway deal is built on
            var deck = MentalPokerEC.BaseDeck(deckSize);
            var d0 = MentalPokerEC.NewPerCardScalars(deckSize);
            var d1 = MentalPokerEC.NewPerCardScalars(deckSize);
            var g0 = MentalPokerEC.NewScalar(); deck = MentalPokerEC.Remask(MentalPokerEC.ShuffleMask(deck, g0, Ident(deckSize)), g0, d0);
            var g1 = MentalPokerEC.NewScalar(); deck = MentalPokerEC.Remask(MentalPokerEC.ShuffleMask(deck, g1, Ident(deckSize)), g1, d1);
            // position 0 is seat-0's hole card: seat 0 receives BOTH players' per-card scalars
            T.True(MultiwayHand.CanSeatIdentify(deck, 0, new[] { d0[0], d1[0] }, deckSize), "the recipient with ALL per-card scalars identifies the card");
            // seat 1 (non-recipient) holds only its own scalar — it cannot strip seat 0's mask
            T.False(MultiwayHand.CanSeatIdentify(deck, 0, new[] { d1[0] }, deckSize), "a non-recipient (its own scalar only) CANNOT read the hole card");
            T.False(MultiwayHand.CanSeatIdentify(deck, 0, System.Array.Empty<byte[]>(), deckSize), "an outsider (no scalars) cannot read it");
        });
    }

    private static int[] Ident(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = i; return a; }
}
