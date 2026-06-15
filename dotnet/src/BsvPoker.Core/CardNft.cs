using System.Security.Cryptography;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// A poker card as an ENCRYPTED 1-sat NFT held in a player's wallet. The card's reveal secret
/// (cardIndex ‖ blind) is sealed to the RECIPIENT'S PUBLIC KEY using a fresh ephemeral ECDH key agreement
/// (ephemeral key → ECDH with the recipient's pubkey → HKDF → AES-256-GCM). Only the recipient's PRIVATE
/// key can derive the same secret and open it; the sender never needs the recipient's private key. This
/// makes a real Alice→Bob transfer possible: Alice opens her card and re-seals it to BOB'S PUBLIC KEY, after
/// which she can no longer open it. ECDH + AES only (no ECIES). The on-chain 1-sat NFT binds H(sealed)+owner.
/// </summary>
public static class CardNft
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("bsvpoker-card-ecdh-key-v2");

    public readonly record struct Opened(int CardIndex, byte[] Blind);

    /// <summary>
    /// Seal (cardIndex, blind) to <paramref name="recipientPub33"/> so only that recipient's private key can
    /// open it. Blob = ephemeralPub(33) ‖ nonce(16) ‖ aead. The sealer needs only the recipient's PUBLIC key.
    /// </summary>
    public static string SealToPub(int cardIndex, byte[] blind, byte[] recipientPub33)
    {
        if (cardIndex < 0 || cardIndex > 51) throw new ArgumentException("card index 0..51");
        if (recipientPub33.Length != 33) throw new ArgumentException("recipient pubkey must be 33-byte compressed");
        var eph = Secp256k1.GenerateKeyPair();
        var shared = Secp256k1.Ecdh(eph.Priv, recipientPub33);          // ephemeral ECDH to the recipient's PUBLIC key
        var nonce = RandomNumberGenerator.GetBytes(16);
        var key = Aead.Hkdf(Concat(shared, eph.Pub), nonce, Info);
        var plaintext = Concat(new[] { (byte)cardIndex }, blind);
        var aead = Aead.Seal(key, plaintext, recipientPub33);          // recipient pubkey bound as AAD
        return Convert.ToHexString(Concat(Concat(eph.Pub, nonce), aead)).ToLowerInvariant();
    }

    /// <summary>Open a sealed card with the recipient's private key. Throws if not the recipient / tampered.</summary>
    public static Opened Open(string sealedHex, byte[] recipientPriv32)
    {
        var blob = Convert.FromHexString(sealedHex);
        if (blob.Length < 33 + 16 + 12 + 16 + 1) throw new ArgumentException("sealed blob too short");
        var ephPub = blob[..33];
        var nonce = blob[33..49];
        var aead = blob[49..];
        var myPub = Secp256k1.PublicKeyCompressed(recipientPriv32);
        var shared = Secp256k1.Ecdh(recipientPriv32, ephPub);          // same shared secret from the recipient side
        var key = Aead.Hkdf(Concat(shared, ephPub), nonce, Info);
        var pt = Aead.Open(key, aead, myPub);                          // throws on wrong key / tamper
        return new Opened(pt[0], pt[1..]);
    }

    /// <summary>True iff this private key can open the card (is the current recipient and the blob is intact).</summary>
    public static bool CanOpen(string sealedHex, byte[] recipientPriv32)
    {
        try { Open(sealedHex, recipientPriv32); return true; } catch { return false; }
    }

    /// <summary>
    /// Transfer: the current owner opens with their PRIVATE key and re-seals to the recipient's PUBLIC key.
    /// The sender needs only <paramref name="toPub33"/> (never the recipient's private key); afterwards the
    /// sender can no longer open it, so they LOSE ACCESS. This is a real Alice→Bob wallet transfer.
    /// </summary>
    public static string Transfer(string sealedHex, byte[] fromPriv32, byte[] toPub33)
    {
        var opened = Open(sealedHex, fromPriv32);
        return SealToPub(opened.CardIndex, opened.Blind, toPub33);
    }

    /// <summary>Commitment to a sealed blob (H(sealed)) — bound into the on-chain 1-sat NFT output.</summary>
    public static byte[] SealCommitment(string sealedHex) => Hashes.Sha256(Convert.FromHexString(sealedHex));

    /// <summary>
    /// The 1-sat NFT locking script: &lt;state&gt; OP_DROP &lt;ownerPub&gt; OP_CHECKSIG, where state =
    /// TAG ‖ H(sealed). No OP_RETURN. Issued from the player's own sats (no banker).
    /// </summary>
    public static byte[] NftLock(string sealedHex, byte[] ownerPub33)
    {
        var tag = Encoding.ASCII.GetBytes("BSVPOKER-CARD-NFT-V1");
        var state = Concat(tag, SealCommitment(sealedHex));
        var b = new List<byte>();
        b.Add(0x4c); b.Add((byte)state.Length); b.AddRange(state); // OP_PUSHDATA1 <state>
        b.Add(0x75); // OP_DROP
        b.Add((byte)ownerPub33.Length); b.AddRange(ownerPub33);
        b.Add(0xac); // OP_CHECKSIG
        return b.ToArray();
    }

    // ===================== D-A: a card NFT is PERMANENTLY BOUND to its game =====================
    // The card commits its gameId AND a hash of a COPY of the game's details into its own locking script, so it
    // is provably bound to THAT game and can NEVER be presented as belonging to another game. Trading/updating
    // re-seals to the new owner but keeps the binding. No OP_RETURN.

    private static readonly byte[] GameNftTag = Encoding.ASCII.GetBytes("BSVPOKER-CARD-NFT-V2");   // v2 = game-bound

    /// <summary>Deterministic hash committing a COPY of the game's identifying details (the data that makes this
    /// game unique). Bound into every card NFT of the game so a card cannot cross to a different game.</summary>
    public static byte[] GameDetailsHash(byte[] tableId, byte[] gameId, byte variant, byte seats, long stakes, IReadOnlyList<byte[]> playerPubs)
    {
        var b = new List<byte>();
        b.AddRange(Encoding.ASCII.GetBytes("BSVPOKER-GAME-DETAILS-V1|"));
        void Field(byte[] d) { b.AddRange(BitConverter.GetBytes(d.Length)); b.AddRange(d); }
        Field(tableId); Field(gameId);
        b.Add(variant); b.Add(seats);
        b.AddRange(BitConverter.GetBytes(stakes));
        b.AddRange(BitConverter.GetBytes(playerPubs.Count));
        foreach (var p in playerPubs.OrderBy(p => Convert.ToHexString(p), StringComparer.Ordinal)) Field(p);  // order-independent
        return Hashes.Sha256(b.ToArray());
    }

    /// <summary>The 1-sat GAME-BOUND NFT lock: &lt;state&gt; OP_DROP &lt;ownerPub&gt; OP_CHECKSIG, where
    /// state = TAG-V2 ‖ gameId(16) ‖ gameDetailsHash(32) ‖ H(sealed)(32). The gameId and a hash of a COPY of the
    /// game details are committed INTO the card, binding it permanently to that game. No OP_RETURN.</summary>
    public static byte[] NftLockForGame(string sealedHex, byte[] ownerPub33, byte[] gameId16, byte[] gameDetailsHash32)
    {
        if (gameId16.Length != 16) throw new ArgumentException("gameId must be 16 bytes");
        if (gameDetailsHash32.Length != 32) throw new ArgumentException("gameDetailsHash must be 32 bytes");
        if (ownerPub33.Length != 33) throw new ArgumentException("ownerPub must be 33-byte compressed");
        var state = Concat(Concat(Concat(GameNftTag, gameId16), gameDetailsHash32), SealCommitment(sealedHex));
        var b = new List<byte>();
        b.Add(0x4c); b.Add((byte)state.Length); b.AddRange(state);   // OP_PUSHDATA1 <state>   (state = 20+16+32+32 = 100 bytes)
        b.Add(0x75);                                                 // OP_DROP
        b.Add((byte)ownerPub33.Length); b.AddRange(ownerPub33);
        b.Add(0xac);                                                 // OP_CHECKSIG
        return b.ToArray();
    }

    /// <summary>Read (gameId, gameDetailsHash, sealCommitment) bound into a game-bound NFT output; null if the
    /// script is not a v2 game-bound card NFT.</summary>
    public static (byte[] GameId, byte[] DetailsHash, byte[] SealCommitment)? ParseGameNft(byte[] script)
    {
        try
        {
            if (script.Length < 3 || script[0] != 0x4c) return null;          // OP_PUSHDATA1
            int len = script[1];
            int stateEnd = 2 + len;
            if (len != GameNftTag.Length + 16 + 32 + 32 || stateEnd >= script.Length) return null;
            if (script[stateEnd] != 0x75) return null;                        // OP_DROP
            var state = script[2..stateEnd];
            for (int i = 0; i < GameNftTag.Length; i++) if (state[i] != GameNftTag[i]) return null;
            int p = GameNftTag.Length;
            var gameId = state[p..(p + 16)]; p += 16;
            var details = state[p..(p + 32)]; p += 32;
            var seal = state[p..(p + 32)];
            return (gameId, details, seal);
        }
        catch { return null; }
    }

    /// <summary>The cross-game GUARD: true iff this card NFT is bound to the given game (BOTH gameId and the
    /// game-details hash match). A card bound to a different game is rejected — it can never be used elsewhere.</summary>
    public static bool BelongsToGame(byte[] script, byte[] gameId16, byte[] gameDetailsHash32)
    {
        var p = ParseGameNft(script);
        return p is { } v && v.GameId.AsSpan().SequenceEqual(gameId16) && v.DetailsHash.AsSpan().SequenceEqual(gameDetailsHash32);
    }

    /// <summary>The result of a game-bound transfer: BOTH the new owner's sealed blob AND the new locking script.
    /// They MUST travel together — the script commits to <c>H(SealedHex)</c>, so the new owner needs the exact
    /// <see cref="SealedHex"/> to open the card; returning the lock alone would leave the on-chain commitment and
    /// the encrypted payload disconnected (the new owner could never open it).</summary>
    public readonly record struct GameTransfer(string SealedHex, byte[] LockScript);

    /// <summary>Trade/update a game-bound card to a new owner WITHIN the same game: re-seal the secret to the new
    /// owner and re-issue the NFT lock with the SAME gameId/detailsHash (the binding is permanent). Returns BOTH
    /// the resealed blob and the matching lock so the on-chain commitment and the payload can never be separated.</summary>
    public static GameTransfer TransferForGame(string sealedHex, byte[] fromPriv32, byte[] toPub33, byte[] gameId16, byte[] gameDetailsHash32)
    {
        var resealed = Transfer(sealedHex, fromPriv32, toPub33);
        var lockScript = NftLockForGame(resealed, toPub33, gameId16, gameDetailsHash32);
        return new GameTransfer(resealed, lockScript);
    }

    private static byte[] Concat(byte[] a, byte[] b) { var o = new byte[a.Length + b.Length]; a.CopyTo(o, 0); b.CopyTo(o, a.Length); return o; }
}
