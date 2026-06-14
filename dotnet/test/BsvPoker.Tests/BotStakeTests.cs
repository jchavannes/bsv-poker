using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// D-C: a 1-of-2 bot stake. The OWNER can ALWAYS reclaim her value; the BOT can only move it into the game's
/// n-of-n pot to play; a stranger can do neither. Her money stays hers, the bot plays with it.
/// </summary>
public static class BotStakeTests
{
    public static void All()
    {
        Console.WriteLine("bot stake — 1-of-2 (owner always reclaims; bot plays with it) (D-C):");
        var owner = Secp256k1.GenerateKeyPair();
        var bot = Secp256k1.GenerateKeyPair();
        var other = Secp256k1.GenerateKeyPair();           // a second player at the table
        var stranger = Secp256k1.GenerateKeyPair();
        string stakeTxid = "fe".PadRight(64, '0');
        const long value = 1000, fee = 1;
        var potPlayers = new[] { owner.Pub, other.Pub };   // the game pot is n-of-n over the table

        T.Run("the stake lock is 1-of-2 (EITHER owner or bot can spend it)", () =>
        {
            var lk = BotStake.FundLock(owner.Pub, bot.Pub);
            T.Eq(lk[0], (byte)0x51, "OP_1: only one signature required");
            T.Eq(lk[^1], (byte)0xae, "ends in OP_CHECKMULTISIG");
        });

        T.Run("the OWNER can ALWAYS reclaim her value (signs alone; pays herself)", () =>
        {
            var rec = BotStake.Reclaim(stakeTxid, 0, value, owner.Pub, bot.Pub, owner.Priv, fee);
            T.True(BotStake.Verify(rec, value, owner.Pub, bot.Pub), "the owner's unilateral reclaim verifies");
            T.True(rec.Outs[0].Script.AsSpan().SequenceEqual(Chain.P2pkhLockForPub(owner.Pub)), "the reclaim pays the OWNER");
            T.Eq(rec.Outs[0].Value, value - fee, "value conserved (− fee)");
        });

        T.Run("the BOT can play: move the stake into the game's n-of-n pot (signs alone, owner not needed)", () =>
        {
            var into = BotStake.StakeIntoPot(stakeTxid, 0, value, owner.Pub, bot.Pub, bot.Priv, potPlayers, fee);
            T.True(BotStake.Verify(into, value, owner.Pub, bot.Pub), "the bot's stake-into-pot verifies");
            T.True(into.Outs[0].Script.AsSpan().SequenceEqual(Chain.MultisigLockNofN(potPlayers)), "the stake now sits in the n-of-n game pot");
            T.Eq(into.Outs[0].Value, value - fee, "value conserved (− fee)");
        });

        T.Run("HOSTILE: a stranger can neither reclaim nor stake (only the owner or the bot can spend)", () =>
        {
            var tx = new Chain.Tx(2, new() { new(stakeTxid, 0, System.Array.Empty<byte>(), 0xffffffff) },
                                  new() { new Chain.TxOut(value - fee, Chain.P2pkhLockForPub(stranger.Pub)) }, 0);
            var sig = Chain.SignMultisig1of2(tx, 0, owner.Pub, bot.Pub, value, stranger.Priv);
            var signed = Chain.ApplyMultisig1of2ScriptSig(tx, 0, sig);
            T.False(BotStake.Verify(signed, value, owner.Pub, bot.Pub), "a stranger's signature does not satisfy the 1-of-2");
        });
    }
}
