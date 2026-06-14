using BsvPoker.Crypto;

namespace BsvPoker.Core.Games;

/// <summary>
/// ZERO-CONF RESILIENT MOVE-CHAIN — the security spine that makes every in-game move safe at ZERO
/// confirmations against a state-level adversary, with PROVEN primitives (n-of-n OP_CHECKMULTISIG + the
/// FORKID sighash) and NO fabricated covenant / no OP_CAT introspection.
///
/// The game's committed coin is locked n-of-n to ALL players (<see cref="Chain.MultisigLockNofN"/>) — NOT a
/// standard single-sig spend. A MOVE spends the current committed output into:
///   out[0] = the NEXT committed n-of-n output (value − one tiny fee), continuing the coin, and
///   out[1] = a typed move-data output (<see cref="TxTemplates"/>, owned by the mover, value 0).
/// The FORKID SIGHASH_ALL signature over the n-of-n input binds BOTH outputs, so the move data and the
/// continuation are committed by the same all-player signature. The whole game is one unbroken chain of
/// n-of-n outputs; the coin can only ever advance with EVERY player's consent.
///
/// Resulting guarantees (the zero-conf directive, 20260612-103843):
///   • NO DOUBLE-SPEND — an honest player signs only the ONE agreed successor, so no conflicting tx can ever
///     reach n-of-n, even at 0-conf and even if n−1 collude on a compromised system.
///   • CONSTRAINED ACTION SET — each player validates a move against the rules BEFORE signing; an out-of-rules
///     move never gathers n signatures and is rejected by construction.
///   • LIVENESS — the pre-signed n-of-n nLockTime recovery (<see cref="Chain.BuildNofNRecovery"/>) refunds
///     every stake if the chain stalls, so nothing strands and no player is at another's mercy.
/// Players hold and dual-path broadcast (IP-to-IP + nodes) the co-signed move; the single-successor honest
/// signature removes any equivocation.
/// </summary>
public static class ZeroConfMoveChain
{
    /// <summary>Lock the game's committed coin to ALL players (n-of-n). This coin flows through every move.</summary>
    public static byte[] Commit(IReadOnlyList<byte[]> playerPubs) => Chain.MultisigLockNofN(playerPubs);

    /// <summary>The genesis funding output value and the n-of-n lock the committed coin starts under.</summary>
    public sealed record Committed(string Txid, uint Vout, long Value, IReadOnlyList<byte[]> Pubs);

    /// <summary>
    /// Build the UNSIGNED next move: spend the current committed n-of-n output into the next committed n-of-n
    /// output (value − <paramref name="fee"/>) plus a typed move-data output owned by <paramref name="mover"/>.
    /// The committed output stays a BARE n-of-n so the existing sign/verify path applies; SIGHASH_ALL binds the
    /// move data. Caller signs with every player (<see cref="Sign"/>), applies (<see cref="Apply"/>), broadcasts.
    /// </summary>
    public static Chain.Tx BuildMove(string commitTxid, uint commitVout, long commitValue,
        IReadOnlyList<byte[]> playerPubs, TxKind kind, byte[] mover, IReadOnlyList<byte[]> fields, long fee = 1)
    {
        if (fee < 0) throw new ArgumentException("negative fee");
        long nextValue = commitValue - fee;
        if (nextValue <= 0) throw new ArgumentException("committed value cannot cover the fee");
        var nextCommit = new Chain.TxOut(nextValue, Chain.MultisigLockNofN(playerPubs));     // out[0] — the continuing committed coin
        var moveData   = new Chain.TxOut(0, TxTemplates.BuildOutput(kind, fields, mover));    // out[1] — the move, bound by the sighash
        var ins = new List<Chain.TxIn> { new(commitTxid, commitVout, Array.Empty<byte>(), 0xffffffff) };
        return new Chain.Tx(2, ins, new List<Chain.TxOut> { nextCommit, moveData }, 0);
    }

    /// <summary>One player's signature over the move's n-of-n committed input (DER ‖ 0x41, low-S). A player
    /// produces this ONLY after validating the move against the rules — that refusal is the action constraint.</summary>
    public static byte[] Sign(Chain.Tx move, IReadOnlyList<byte[]> playerPubs, long commitValue, byte[] priv)
        => Chain.SignMultisigN(move, 0, playerPubs, commitValue, priv);

    /// <summary>Attach all players' signatures (in pubkey order) to the move's committed input.</summary>
    public static Chain.Tx Apply(Chain.Tx move, IReadOnlyList<byte[]> sigs) => Chain.ApplyMultisigScriptSigN(move, 0, sigs);

    /// <summary>Verify a fully-signed move on the consensus path: EVERY player must have signed (n-of-n), the
    /// sighash binds the outputs (the next committed coin + the move data). Fewer than n valid sigs ⇒ false.</summary>
    public static bool Verify(Chain.Tx signed, IReadOnlyList<byte[]> playerPubs, long commitValue)
        => Chain.VerifyMultisigNofN(signed, 0, playerPubs, commitValue);

    /// <summary>The next committed coin produced by a move (out[0]) — the input to the following move.</summary>
    public static Committed Next(Chain.Tx signedMove, IReadOnlyList<byte[]> playerPubs)
        => new(Chain.Txid(signedMove), 0, signedMove.Outs[0].Value, playerPubs);

    /// <summary>The pre-signed n-of-n nLockTime recovery that refunds each stake if the chain stalls (liveness).
    /// Co-signed at SETUP; anyone can broadcast it after the timeout so no stake is ever stranded.</summary>
    public static Chain.Tx Recovery(string commitTxid, uint commitVout,
        IReadOnlyList<(byte[] Pub, long Stake)> contributors, long fee, uint lockHeight)
        => Chain.BuildNofNRecovery(commitTxid, commitVout, contributors, fee, lockHeight);
}
