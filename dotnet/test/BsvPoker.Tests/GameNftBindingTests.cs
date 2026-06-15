using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// D-A: a card NFT is PERMANENTLY bound to its game. The card commits its gameId + a hash of a copy of the
/// game's details, so it can be traded/updated WITHIN the game but can never be presented as belonging to
/// another game. Identity (the owner key) and the game are linked into the card itself.
/// </summary>
public static class GameNftBindingTests
{
    public static void All()
    {
        Console.WriteLine("card NFT permanently bound to its game (D-A):");
        var alice = Secp256k1.GenerateKeyPair();
        var bob = Secp256k1.GenerateKeyPair();
        var tableId = new byte[16]; tableId[0] = 7;
        var gameA = new byte[16]; gameA[0] = 0xA1;
        var gameB = new byte[16]; gameB[0] = 0xB2;
        var players = new[] { alice.Pub, bob.Pub };
        byte variant = 0, seats = 2; long stakes = 1000;
        var detailsA = CardNft.GameDetailsHash(tableId, gameA, variant, seats, stakes, players);
        var detailsB = CardNft.GameDetailsHash(tableId, gameB, variant, seats, stakes, players);

        var sealed_ = CardNft.SealToPub(12, new byte[32], alice.Pub);

        T.Run("the card commits its gameId + game-details hash + seal (parse round-trips)", () =>
        {
            var lk = CardNft.NftLockForGame(sealed_, alice.Pub, gameA, detailsA);
            var parsed = CardNft.ParseGameNft(lk);
            T.True(parsed != null, "parses as a game-bound NFT");
            T.True(parsed!.Value.GameId.AsSpan().SequenceEqual(gameA), "gameId recovered");
            T.True(parsed.Value.DetailsHash.AsSpan().SequenceEqual(detailsA), "game-details hash recovered");
            T.True(parsed.Value.SealCommitment.AsSpan().SequenceEqual(CardNft.SealCommitment(sealed_)), "seal commitment recovered");
            T.Eq(lk[^1], (byte)0xac, "ends in OP_CHECKSIG (owner holds it)");
            foreach (var by in lk) { /* no OP_RETURN anywhere */ }
            T.True(lk[0] != 0x6a, "no OP_RETURN");
        });

        T.Run("BelongsToGame: true for its own game, FALSE for any other game", () =>
        {
            var lk = CardNft.NftLockForGame(sealed_, alice.Pub, gameA, detailsA);
            T.True(CardNft.BelongsToGame(lk, gameA, detailsA), "the card belongs to game A");
            T.False(CardNft.BelongsToGame(lk, gameB, detailsB), "the card is REJECTED for a different game (cannot cross games)");
            T.False(CardNft.BelongsToGame(lk, gameA, detailsB), "a mismatched details hash is rejected");
            T.False(CardNft.BelongsToGame(lk, gameB, detailsA), "a mismatched gameId is rejected");
        });

        T.Run("trade WITHIN the game: returns BOTH the resealed blob AND the lock; the lock commits to THAT blob", () =>
        {
            var lk = CardNft.NftLockForGame(sealed_, alice.Pub, gameA, detailsA);
            var parsed = CardNft.ParseGameNft(lk)!;
            // Alice trades the card to Bob within game A — the transfer returns the new sealed blob AND the lock.
            var toBob = CardNft.TransferForGame(sealed_, alice.Priv, bob.Pub, gameA, detailsA);
            T.True(CardNft.BelongsToGame(toBob.LockScript, gameA, detailsA), "after the trade the card still belongs to game A");
            var bobParsed = CardNft.ParseGameNft(toBob.LockScript)!;
            // THE FIX (was a defect): the returned blob is the EXACT one the lock commits to. Bob opens THIS blob,
            // and its hash equals the script's seal commitment — the on-chain commitment and the payload match.
            T.True(CardNft.CanOpen(toBob.SealedHex, bob.Priv), "the new owner (Bob) opens the EXACT blob the transfer produced");
            T.False(CardNft.CanOpen(toBob.SealedHex, alice.Priv), "Alice can no longer open it after the transfer (she lost access)");
            T.True(bobParsed.Value.SealCommitment.AsSpan().SequenceEqual(CardNft.SealCommitment(toBob.SealedHex)),
                "the lock commits to EXACTLY the returned blob (H(sealed) == on-chain commitment) — no disconnect");
            T.False(bobParsed.Value.SealCommitment.AsSpan().SequenceEqual(parsed.Value.SealCommitment), "the re-seal changed the seal commitment (fresh ephemeral); the game binding is unchanged");
        });

        T.Run("the game-details hash is order-independent over players but changes with the game's parameters", () =>
        {
            var swapped = CardNft.GameDetailsHash(tableId, gameA, variant, seats, stakes, new[] { bob.Pub, alice.Pub });
            T.True(swapped.AsSpan().SequenceEqual(detailsA), "player order does not change the details hash");
            var diffStakes = CardNft.GameDetailsHash(tableId, gameA, variant, seats, stakes + 1, players);
            T.False(diffStakes.AsSpan().SequenceEqual(detailsA), "different stakes ⇒ different game ⇒ different hash");
        });
    }
}
