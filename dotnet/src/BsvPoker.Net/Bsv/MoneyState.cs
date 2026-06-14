using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// The three-state money classifier (whitepaper SPV, the principal's corrected model — see transcripts
/// 20260610-103047 spv-three-state-model and 20260610-102618 spv-proof-persistence-design). A coin/tx is
/// exactly one of:
/// <list type="bullet">
/// <item><b>Confirmed</b>: its SAVED merkle proof RE-VERIFIES against a header in our self-validated
///   <see cref="HeadersChain"/> AND the proven output pays us. The ONLY bucket counted as balance.</item>
/// <item><b>Unconfirmed</b>: a well-formed tx that pays us, received/broadcast, but with NO verifying proof
///   yet (not mined). Shown separately. NEVER counted as balance.</item>
/// <item><b>DoubleSpendOrInvalid</b>: an input is in the known-spent/conflict set, the coin's own outpoint
///   is flagged conflicting, the output does not pay us, or a present proof FAILS to verify. Not money.</item>
/// </list>
/// This is a PURE classifier: it reuses the existing <see cref="SpvFunding"/> / <see cref="MerkleProof"/>
/// verification (no crypto is reimplemented), never mutates, and NEVER deletes a record — callers retain
/// every coin and simply do not count the non-Confirmed ones in balance.
/// </summary>
/// <remarks>
/// PLACEMENT NOTE: the build brief named <c>src/BsvPoker.Core/MoneyState.cs</c>, but the SPV verification
/// this MUST reuse (SpvFunding/MerkleProof/HeadersChain/BlockHeader) lives in BsvPoker.Net, and BsvPoker.Net
/// already references BsvPoker.Core — so placing the classifier in Core would create a circular project
/// reference and force reimplementing the crypto. The "reuse existing verification, do not reimplement"
/// rule wins: this file lives beside the SPV code it depends on, in BsvPoker.Net.Bsv.
/// </remarks>
public static class MoneyState
{
    /// <summary>The three money states. Exactly one applies to a coin at any time.</summary>
    public enum State
    {
        /// <summary>Mined and proven: saved proof re-verifies to a validated header, output pays us. Spendable.</summary>
        Confirmed,
        /// <summary>Valid and broadcast but not yet mined/proven. Shown separately, never spendable as settled.</summary>
        Unconfirmed,
        /// <summary>An input is spent/conflicting, the proof fails, or the coin is malformed. Not money.</summary>
        DoubleSpendOrInvalid,
    }

    /// <summary>
    /// A wallet coin together with the SPV proof material persisted with it (per the proof-persistence design:
    /// the proof is SAVED alongside the coin, not discarded after first verification). <paramref name="Proof"/>
    /// is null when no proof has been saved yet (the coin is at most Unconfirmed). The owning pubkey is carried
    /// so the classifier can re-confirm — offline, against our own headers — that the proven output still pays us.
    /// </summary>
    public sealed record Coin(OnChainWallet.Utxo Utxo, byte[] OwnerPub33, SpvFunding.Proof? Proof);

    /// <summary>The classification of one coin, plus its value. The record is RETAINED whatever the state.</summary>
    public sealed record Classification(Coin Coin, State State, long Value)
    {
        public bool IsConfirmed => State == State.Confirmed;
    }

    /// <summary>
    /// An outpoint key "txid:vout" (display txid) — used both for the known-spent/conflict set and to detect
    /// that a coin's own outpoint has been flagged as conflicting (e.g. a competing double-spend was seen).
    /// </summary>
    public static string OutpointKey(string txid, uint vout) => $"{txid}:{vout}";

    /// <summary>
    /// Classify a single coin. <paramref name="spentOrConflicting"/> holds outpoint keys (see
    /// <see cref="OutpointKey"/>) that are known to be already spent or to conflict — if ANY of the coin's
    /// transaction inputs reference such an outpoint, OR the coin's OWN outpoint is flagged, the coin is a
    /// DoubleSpendOrInvalid. Otherwise a present-and-verifying saved proof makes it Confirmed; a well-formed
    /// pays-us tx with no verifying proof is Unconfirmed; anything else (proof fails, malformed, wrong key)
    /// is DoubleSpendOrInvalid. Pure and total: no exception escapes, nothing is mutated or deleted.
    /// </summary>
    public static Classification Classify(Coin coin, HeadersChain chain, IReadOnlySet<string> spentOrConflicting)
    {
        var u = coin.Utxo;

        // (3) double-spend / conflict — checked FIRST so a conflicting input overrides even a good proof.
        if (spentOrConflicting.Contains(OutpointKey(u.Txid, u.Vout)))
            return new Classification(coin, State.DoubleSpendOrInvalid, u.Value);
        if (coin.Proof != null)
        {
            foreach (var i in coin.Proof.Tx.Ins)
                if (spentOrConflicting.Contains(OutpointKey(i.PrevTxid, i.Vout)))
                    return new Classification(coin, State.DoubleSpendOrInvalid, u.Value);
        }

        // (1) CONFIRMED iff a SAVED proof RE-VERIFIES (reusing SpvFunding.Verify): the block is in our own
        // validated header chain, the merkle branch folds to that header's root, and the output pays us. The
        // proof must also describe THIS coin (same txid + vout) so a valid proof for a different coin can't pass.
        if (coin.Proof != null)
        {
            bool describesThisCoin = coin.Proof.Vout == u.Vout && Chain.Txid(coin.Proof.Tx) == u.Txid;
            if (describesThisCoin)
            {
                var verified = SpvFunding.Verify(coin.Proof, chain, coin.OwnerPub33, u.KeyChain, u.KeyIndex);
                if (verified != null) return new Classification(coin, State.Confirmed, verified.Value);
            }
            // A proof is present but does NOT verify (tampered branch, unknown/forged block header, wrong key,
            // or it describes a different coin) — that is not "merely unconfirmed", it is invalid. (3)
            return new Classification(coin, State.DoubleSpendOrInvalid, u.Value);
        }

        // (2) UNCONFIRMED: no saved proof yet. Still require the coin to be well-formed and to pay US, so a
        // garbage record is not optimistically shown as pending money. A well-formed pays-us, no-proof coin is
        // a valid-but-unmined receipt: shown separately, never counted in balance.
        return PaysUs(coin) ? new Classification(coin, State.Unconfirmed, u.Value)
                            : new Classification(coin, State.DoubleSpendOrInvalid, u.Value);
    }

    /// <summary>
    /// Classify a batch of coins. Returns one <see cref="Classification"/> per input coin (none dropped),
    /// preserving order — the caller keeps EVERY record and merely buckets it.
    /// </summary>
    public static IReadOnlyList<Classification> ClassifyAll(IEnumerable<Coin> coins, HeadersChain chain,
        IReadOnlySet<string> spentOrConflicting)
        => coins.Select(c => Classify(c, chain, spentOrConflicting)).ToList();

    /// <summary>
    /// The spendable BALANCE: the sum of Confirmed coins ONLY. Unconfirmed and DoubleSpendOrInvalid coins are
    /// present in <paramref name="classified"/> (never deleted) but contribute ZERO to balance.
    /// </summary>
    public static long Balance(IEnumerable<Classification> classified)
        => classified.Where(c => c.State == State.Confirmed).Sum(c => c.Value);

    /// <summary>Convenience: classify <paramref name="coins"/> then sum the Confirmed bucket.</summary>
    public static long Balance(IEnumerable<Coin> coins, HeadersChain chain, IReadOnlySet<string> spentOrConflicting)
        => Balance(ClassifyAll(coins, chain, spentOrConflicting));

    /// <summary>All coins in a given state (records retained; this is a VIEW, not a deletion).</summary>
    public static IReadOnlyList<Classification> InState(IEnumerable<Classification> classified, State state)
        => classified.Where(c => c.State == state).ToList();

    // The coin's claimed output exists and pays the owner's key (P2PKH). Total — false on any malformed input.
    private static bool PaysUs(Coin coin)
    {
        try
        {
            var u = coin.Utxo;
            // Without a proof we have no tx body to inspect; the receipt asserts (txid,vout,value,ownerPub).
            // "Pays us" here means the owner pubkey is well-formed (33 bytes) and the recorded value is positive,
            // i.e. a structurally sane receipt. (When a proof IS present the pays-us check is the rigorous one
            // inside SpvFunding.Verify above.)
            if (coin.OwnerPub33 == null || coin.OwnerPub33.Length != 33) return false;
            if (u.Value <= 0) return false;
            _ = Chain.P2pkhLockForPub(coin.OwnerPub33); // throws on a bad key
            return true;
        }
        catch { return false; }
    }
}
