using BsvPoker.Crypto;

namespace BsvPoker.Core.Games;

/// <summary>
/// SETTLEMENT — the on-chain payout that closes a hand by paying the n-of-n committed pot
/// (<see cref="ZeroConfMoveChain.Committed"/>) out to the final per-seat result, with EVERY player's consent.
///
/// The pot is the last committed coin of the move-chain: a BARE n-of-n output locked to ALL players
/// (<see cref="Chain.MultisigLockNofN"/>). A settlement spends that one output into one P2PKH output per seat
/// that ends the hand with a positive stack (each seat's net result), value-conserving:
///   Σ payouts == pot − fee.
/// Because the coin is n-of-n, the settlement can ONLY move if EVERY player co-signs it
/// (<see cref="Chain.SignMultisigN"/>), and it is checked on the ordinary consensus path
/// (<see cref="Chain.VerifyMultisigNofN"/>). So a sub-coalition of n−1 colluders can never settle the pot to a
/// thief, never inflate a payout, and never strand the honest player's stake — the honest signature is
/// mandatory by construction (no fabricated covenant; proven OP_CHECKMULTISIG + the FORKID sighash only).
///
/// Liveness: if the cooperative settlement stalls (a party griefs by refusing to sign the agreed payout),
/// the pre-agreed n-of-n nLockTime refund (<see cref="Chain.BuildNofNRecovery"/>, surfaced here as
/// <see cref="Refund"/>) returns each contributor's stake after the timeout — so no stake is ever at another
/// player's mercy. Safety (no theft, consensus-enforced) + liveness (pre-signed refund) for the pot payout.
///
/// The live game feeds this the data from a NetGame "settle" MoveRecord: the committed pot plus the final
/// per-seat (pub, stack) result (<c>MoveRecord.Stacks</c>).
/// </summary>
public static class SettlementMove
{
    /// <summary>One seat's settled result: the player's compressed pubkey and the stack it ends the hand with.
    /// (Mirrors a NetGame settle MoveRecord's per-seat <c>(Pub, Stack)</c>, with Pub as raw 33 bytes.)</summary>
    public readonly record struct Payout(byte[] Pub, long Stack);

    /// <summary>
    /// Build the UNSIGNED settlement: spend the committed n-of-n pot into one P2PKH output per seat that ends
    /// the hand with a POSITIVE stack (each seat's net result). The single tiny <paramref name="fee"/> is taken
    /// from the pot (so Σ outputs == pot − fee); the result must therefore conserve value exactly. A seat with a
    /// zero (busted) stack receives no output. Every player then co-signs (<see cref="Sign"/>) and the assembled
    /// tx verifies on the consensus path (<see cref="Verify"/>).
    /// </summary>
    public static Chain.Tx Build(ZeroConfMoveChain.Committed pot, IReadOnlyList<Payout> finalStacks, long fee = 1)
    {
        if (pot is null) throw new ArgumentException("no committed pot");
        if (finalStacks is null || finalStacks.Count == 0) throw new ArgumentException("no final stacks");
        if (fee < 0) throw new ArgumentException("negative fee");
        foreach (var p in finalStacks)
        {
            if (p.Stack < 0) throw new ArgumentException("negative stack");
            if (p.Pub is null || p.Pub.Length != 33 || (p.Pub[0] != 0x02 && p.Pub[0] != 0x03))
                throw new ArgumentException("payout pubkeys must be 33-byte compressed");
        }

        long paid = finalStacks.Sum(p => p.Stack);
        // The settled stacks plus the single fee MUST account for exactly the pot — no money may be created or
        // burned. (Stacks are conserved through the move-chain; this is the closing consistency check.)
        if (paid != pot.Value - fee)
            throw new ArgumentException($"settlement does not conserve value: Σ stacks ({paid}) + fee ({fee}) != pot ({pot.Value})");

        var outs = finalStacks
            .Where(p => p.Stack > 0)
            .Select(p => new Chain.TxOut(p.Stack, Chain.P2pkhLockForPub(p.Pub)))
            .ToList();
        if (outs.Count == 0) throw new ArgumentException("no positive payouts");

        var ins = new List<Chain.TxIn> { new(pot.Txid, pot.Vout, Array.Empty<byte>(), 0xffffffff) };
        return new Chain.Tx(2, ins, outs, 0);
    }

    /// <summary>One player's signature over the settlement's n-of-n pot input (DER ‖ 0x41, low-S). A player
    /// produces this ONLY after checking the payouts match the agreed final stacks — that refusal is the
    /// no-theft guarantee: an unfair settlement never gathers n signatures.</summary>
    public static byte[] Sign(Chain.Tx settlement, ZeroConfMoveChain.Committed pot, byte[] priv)
        => Chain.SignMultisigN(settlement, 0, pot.Pubs, pot.Value, priv);

    /// <summary>Attach all players' signatures (in pubkey order) to the settlement's n-of-n pot input.</summary>
    public static Chain.Tx Apply(Chain.Tx settlement, IReadOnlyList<byte[]> sigs)
        => Chain.ApplyMultisigScriptSigN(settlement, 0, sigs);

    /// <summary>Verify a fully-signed settlement on the consensus path: EVERY player must have signed (n-of-n)
    /// and the sighash binds the payout outputs. Fewer than n valid sigs (an (n-1) coalition) ⇒ false.</summary>
    public static bool Verify(Chain.Tx signed, ZeroConfMoveChain.Committed pot)
        => Chain.VerifyMultisigNofN(signed, 0, pot.Pubs, pot.Value);

    /// <summary>
    /// Value-conservation check independent of the tx builder: the sum of a settlement's outputs equals
    /// pot − fee (no satoshi created or burned). Use it to validate a settlement received over the wire BEFORE
    /// signing it — a payout set that does not conserve value is rejected.
    /// </summary>
    public static bool ConservesValue(Chain.Tx settlement, ZeroConfMoveChain.Committed pot, long fee)
        => fee >= 0 && settlement.Outs.Sum(o => o.Value) == pot.Value - fee;

    /// <summary>
    /// The pre-agreed n-of-n nLockTime REFUND for a stalled settlement: returns each contributor their stake
    /// after <paramref name="lockHeight"/> (non-final sequence so the locktime binds). Co-signed at SETUP via
    /// <see cref="Sign"/>; if the cooperative payout stalls, anyone broadcasts this after the timeout so no
    /// stake is stranded. Reuses the proven <see cref="Chain.BuildNofNRecovery"/>.
    /// </summary>
    public static Chain.Tx Refund(ZeroConfMoveChain.Committed pot, IReadOnlyList<(byte[] Pub, long Stake)> contributors, long fee, uint lockHeight)
        => Chain.BuildNofNRecovery(pot.Txid, pot.Vout, contributors, fee, lockHeight);
}
