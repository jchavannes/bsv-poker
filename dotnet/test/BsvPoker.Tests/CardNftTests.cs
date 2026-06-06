using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

public static class CardNftTests
{
    public static void All()
    {
        Console.WriteLine("card NFTs (encrypted, held in wallet; transfer = sender loses access):");
        var alice = Secp256k1.GenerateKeyPair();
        var bob = Secp256k1.GenerateKeyPair();
        var blind = new byte[32]; for (int i = 0; i < 32; i++) blind[i] = (byte)(i + 1);

        T.Run("owner can open their sealed card; a different key cannot", () =>
        {
            var s = CardNft.SealToOwner(42, blind, alice.Priv);
            var o = CardNft.OpenAsOwner(s, alice.Priv);
            T.Eq(o.CardIndex, 42); T.Eq(T.Hex(o.Blind), T.Hex(blind));
            T.False(CardNft.CanOpen(s, bob.Priv), "wrong key cannot open");
        });

        T.Run("TRANSFER: after Alice sends to Bob, Bob opens it and Alice CANNOT", () =>
        {
            var toAlice = CardNft.SealToOwner(7, blind, alice.Priv);
            var toBob = CardNft.Transfer(toAlice, alice.Priv, bob.Priv);
            T.Eq(CardNft.OpenAsOwner(toBob, bob.Priv).CardIndex, 7, "Bob opens to the same card");
            T.False(CardNft.CanOpen(toBob, alice.Priv), "sender LOST access on transfer");
        });

        T.Run("a non-owner cannot transfer (cannot open ⇒ cannot re-seal)", () =>
        {
            var toAlice = CardNft.SealToOwner(3, blind, alice.Priv);
            T.Throws(() => CardNft.Transfer(toAlice, bob.Priv, bob.Priv));
        });

        T.Run("tampering the sealed blob is rejected (AES-GCM tag/AAD)", () =>
        {
            var s = CardNft.SealToOwner(10, blind, alice.Priv);
            var b = Convert.FromHexString(s); b[^1] ^= 0x01;
            T.False(CardNft.CanOpen(Convert.ToHexString(b), alice.Priv));
        });

        T.Run("the 1-sat NFT script binds H(sealed) as PUSHDATA (never an OP_RETURN opcode)", () =>
        {
            var s = CardNft.SealToOwner(20, blind, alice.Priv);
            var lockScript = CardNft.NftLock(s, alice.Pub);
            // structure: OP_PUSHDATA1 <len> <state> OP_DROP <33> <pub> OP_CHECKSIG. The commitment is
            // pushed DATA (a coincidental 0x6a inside the hash is fine); we never emit an OP_RETURN opcode.
            T.Eq(lockScript[0], (byte)0x4c, "OP_PUSHDATA1 (state is pushed data)");
            T.Eq(lockScript[^1], (byte)0xac, "ends with OP_CHECKSIG");
            int stateLen = lockScript[1];
            T.Eq(lockScript[2 + stateLen], (byte)0x75, "OP_DROP follows the pushed state");
            T.True(Contains(lockScript, CardNft.SealCommitment(s)), "locking script binds H(sealed)");
        });
    }

    private static bool Contains(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }
}
