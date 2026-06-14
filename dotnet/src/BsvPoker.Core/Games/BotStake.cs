using BsvPoker.Crypto;

namespace BsvPoker.Core.Games;

/// <summary>
/// D-C: a player funds her BOT with a 1-of-2 (owner OR bot) stake. The OWNER can ALWAYS reclaim her value
/// unilaterally — her money stays hers — while the BOT can use it to PLAY: move it into the game's n-of-n
/// committed pot (<see cref="ZeroConfMoveChain"/>). Either party can spend the 1-of-2, so the owner is never
/// at the bot's mercy and the bot can stake on her behalf. Proven primitives only (Chain 1-of-2 multisig).
/// </summary>
public static class BotStake
{
    /// <summary>The 1-of-2 lock holding the bot's stake: EITHER the owner or the bot can spend it.</summary>
    public static byte[] FundLock(byte[] ownerPub33, byte[] botPub33) => Chain.MultisigLock1of2(ownerPub33, botPub33);

    /// <summary>The OWNER's ALWAYS-available reclaim: take the full stake back to the owner. Owner signs alone,
    /// needs no one's permission — "Alice can always receive her transaction value back".</summary>
    public static Chain.Tx Reclaim(string stakeTxid, uint vout, long value, byte[] ownerPub33, byte[] botPub33, byte[] ownerPriv32, long fee = 1)
    {
        if (fee < 0 || fee >= value) throw new ArgumentException("fee out of range");
        var tx = new Chain.Tx(2, new() { new(stakeTxid, vout, Array.Empty<byte>(), 0xffffffff) },
                              new() { new Chain.TxOut(value - fee, Chain.P2pkhLockForPub(ownerPub33)) }, 0);
        var sig = Chain.SignMultisig1of2(tx, 0, ownerPub33, botPub33, value, ownerPriv32);
        return Chain.ApplyMultisig1of2ScriptSig(tx, 0, sig);
    }

    /// <summary>The BOT plays: move the stake into the game's n-of-n committed pot (the bot signs alone; the
    /// owner's signature is not needed). From here the coin advances only under the zero-conf move-chain
    /// (every move needs all players), so the bot can play WITH the stake but cannot divert it.</summary>
    public static Chain.Tx StakeIntoPot(string stakeTxid, uint vout, long value, byte[] ownerPub33, byte[] botPub33,
        byte[] botPriv32, IReadOnlyList<byte[]> potPlayerPubs, long fee = 1)
    {
        if (fee < 0 || fee >= value) throw new ArgumentException("fee out of range");
        var tx = new Chain.Tx(2, new() { new(stakeTxid, vout, Array.Empty<byte>(), 0xffffffff) },
                              new() { new Chain.TxOut(value - fee, Chain.MultisigLockNofN(potPlayerPubs)) }, 0);
        var sig = Chain.SignMultisig1of2(tx, 0, ownerPub33, botPub33, value, botPriv32);
        return Chain.ApplyMultisig1of2ScriptSig(tx, 0, sig);
    }

    /// <summary>Verify a 1-of-2 bot-stake spend (either the owner's reclaim or the bot's stake-into-pot).</summary>
    public static bool Verify(Chain.Tx signed, long value, byte[] ownerPub33, byte[] botPub33)
        => Chain.VerifyMultisig1of2(signed, 0, ownerPub33, botPub33, value);
}
