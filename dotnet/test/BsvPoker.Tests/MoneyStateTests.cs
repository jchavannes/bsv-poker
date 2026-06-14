using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// The three-state money classifier (MoneyState). A coin with a saved proof that re-verifies to a validated
/// header is CONFIRMED (and the ONLY thing counted in balance); a valid-but-unmined coin (no proof) is
/// UNCONFIRMED (never balance); a coin whose input is in the spent/conflict set, whose own outpoint is
/// flagged, whose proof is tampered, or whose proof points at a non-validated/forged header is
/// DOUBLE-SPEND-OR-INVALID. Records are RETAINED in every case — nothing is ever deleted. Positive AND
/// hostile tests.
/// </summary>
public static class MoneyStateTests
{
    private const uint EasyBits = 0x207fffff;

    private static BlockHeader MineHeader(byte[] prev, byte[] merkleRoot)
    {
        for (uint nonce = 1; ; nonce++)
        {
            var h = new BlockHeader(1, prev, merkleRoot, 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
    }

    // A funding tx paying `me` `value` at vout 1 (vout 0 pays `other`), placed in a block among filler leaves
    // whose header is validated into a fresh HeadersChain. Returns the pieces a Coin needs.
    private static (Chain.Tx Tx, BlockHeader Header, byte[][] Branch, int Idx, HeadersChain Chain)
        Mined(byte[] mePub, byte[] otherPub, long value)
    {
        // UNIQUE input per funding tx (derived from value) so each coin spends a DISTINCT outpoint — otherwise a
        // conflict marked on one coin's input would wrongly flag every other coin that shared the same hardcoded input.
        var fundTx = new Chain.Tx(2,
            new() { new(value.ToString("x").PadLeft(64, '0'), 0, Array.Empty<byte>(), 0xffffffff) },
            new() { new(10000, Chain.P2pkhLockForPub(otherPub)), new(value, Chain.P2pkhLockForPub(mePub)) }, 0);
        var leaf = Hashes.Sha256d(Chain.Serialize(fundTx));
        var leaves = new List<byte[]>();
        for (int i = 0; i < 4; i++) { var b = new byte[32]; b[0] = (byte)(i + 1); leaves.Add(b); }
        int idx = 2; leaves.Insert(idx, leaf);
        var root = MerkleProof.Root(leaves);
        var branch = MerkleProof.Branch(leaves, idx);
        var header = MineHeader(new byte[32], root);
        var chain = new HeadersChain();
        chain.AddGenesis(header);
        return (fundTx, header, branch, idx, chain);
    }

    private static MoneyState.Coin ConfirmedCoin(byte[] mePub, Chain.Tx tx, BlockHeader header, byte[][] branch, int idx)
    {
        var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 1, tx.Outs[1].Value, 0, 0);
        var proof = new SpvFunding.Proof(tx, 1, header.HashHex(), branch, idx);
        return new MoneyState.Coin(utxo, mePub, proof);
    }

    private static readonly IReadOnlySet<string> NoConflicts = new HashSet<string>();

    public static void All()
    {
        Console.WriteLine("three-state money classifier (balance = Confirmed only; nothing deleted):");
        // DETERMINISTIC keys (fixed seeds, not random) so this suite is fully reproducible and can never flake on a
        // chance keypair/merkle interaction — the classifier's behaviour does not depend on which keys are used.
        byte[] Seed(byte v) { var s = new byte[32]; s[31] = v; return s; }
        var me = (Priv: Seed(0x11), Pub: Secp256k1.PublicKeyCompressed(Seed(0x11)));
        var other = (Priv: Seed(0x22), Pub: Secp256k1.PublicKeyCompressed(Seed(0x22)));

        T.Run("a coin with a proof that re-verifies to a validated header is CONFIRMED and counts as balance", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            var coin = ConfirmedCoin(me.Pub, tx, header, branch, idx);
            var c = MoneyState.Classify(coin, chain, NoConflicts);
            T.Eq(c.State, MoneyState.State.Confirmed, "confirmed");
            T.Eq(c.Value, 70000L, "value picked up from the proven output");
            T.Eq(MoneyState.Balance(new[] { c }), 70000L, "confirmed coin is the balance");
        });

        T.Run("a valid coin with NO saved proof is UNCONFIRMED and is NEVER counted in balance", () =>
        {
            var (tx, _, _, _, chain) = Mined(me.Pub, other.Pub, 50000);
            // received/broadcast but no proof yet (not mined): proof = null
            var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 1, 50000, 0, 0);
            var coin = new MoneyState.Coin(utxo, me.Pub, Proof: null);
            var c = MoneyState.Classify(coin, chain, NoConflicts);
            T.Eq(c.State, MoneyState.State.Unconfirmed, "unconfirmed (valid but unmined)");
            T.Eq(MoneyState.Balance(new[] { c }), 0L, "unconfirmed contributes ZERO to balance");
        });

        T.Run("lifecycle: the SAME coin, once its proof arrives, moves Unconfirmed -> Confirmed (no delete)", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 33000);
            var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 1, 33000, 0, 0);
            var pending = new MoneyState.Coin(utxo, me.Pub, Proof: null);
            T.Eq(MoneyState.Classify(pending, chain, NoConflicts).State, MoneyState.State.Unconfirmed, "starts pending");
            var proven = pending with { Proof = new SpvFunding.Proof(tx, 1, header.HashHex(), branch, idx) };
            T.Eq(MoneyState.Classify(proven, chain, NoConflicts).State, MoneyState.State.Confirmed, "settles once proven");
        });

        T.Run("a coin whose tx input is in the spent/conflict set is DOUBLE-SPEND/INVALID (overrides a good proof)", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            var coin = ConfirmedCoin(me.Pub, tx, header, branch, idx); // proof is otherwise perfect
            // the funding tx's only input references ee..:0 — mark that prevout as already spent/conflicting
            var conflict = new HashSet<string> { MoneyState.OutpointKey(tx.Ins[0].PrevTxid, tx.Ins[0].Vout) };
            var c = MoneyState.Classify(coin, chain, conflict);
            T.Eq(c.State, MoneyState.State.DoubleSpendOrInvalid, "conflicting input => not money even with a valid proof");
            T.Eq(MoneyState.Balance(new[] { c }), 0L, "double-spend never counts");
        });

        T.Run("a coin whose OWN outpoint is flagged conflicting is DOUBLE-SPEND/INVALID", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            var coin = ConfirmedCoin(me.Pub, tx, header, branch, idx);
            var conflict = new HashSet<string> { MoneyState.OutpointKey(Chain.Txid(tx), 1) };
            T.Eq(MoneyState.Classify(coin, chain, conflict).State, MoneyState.State.DoubleSpendOrInvalid, "own outpoint flagged");
        });

        T.Run("a tampered merkle branch makes the coin DOUBLE-SPEND/INVALID (not merely unconfirmed)", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            var bad = branch.Select(b => (byte[])b.Clone()).ToArray(); bad[0][0] ^= 0xFF;
            var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 1, 70000, 0, 0);
            var coin = new MoneyState.Coin(utxo, me.Pub, new SpvFunding.Proof(tx, 1, header.HashHex(), bad, idx));
            var c = MoneyState.Classify(coin, chain, NoConflicts);
            T.Eq(c.State, MoneyState.State.DoubleSpendOrInvalid, "a present-but-failing proof => invalid");
            T.Eq(MoneyState.Balance(new[] { c }), 0L, "never counted");
        });

        T.Run("a proof pointing at a header NOT in our validated chain (forged block) is DOUBLE-SPEND/INVALID", () =>
        {
            var (tx, header, branch, idx, _) = Mined(me.Pub, other.Pub, 70000);
            var coin = ConfirmedCoin(me.Pub, tx, header, branch, idx);
            var emptyChain = new HeadersChain(); // we validated NOTHING — the claimed block is unknown to us
            T.Eq(MoneyState.Classify(coin, emptyChain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid,
                "unknown/forged block header is rejected, not trusted");
        });

        T.Run("a tampered tx (block real, but tx body altered after proving) is DOUBLE-SPEND/INVALID", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            // alter the proven amount: the leaf no longer matches the branch, and txid changes too
            var alteredTx = tx with { Outs = new() { tx.Outs[0], new Chain.TxOut(999999, tx.Outs[1].Script) } };
            var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 1, 999999, 0, 0); // claims the bumped value
            var coin = new MoneyState.Coin(utxo, me.Pub, new SpvFunding.Proof(alteredTx, 1, header.HashHex(), branch, idx));
            T.Eq(MoneyState.Classify(coin, chain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid,
                "an altered tx no longer folds to the proven root");
        });

        T.Run("a proven coin that pays SOMEONE ELSE'S key is DOUBLE-SPEND/INVALID for us", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            // claim vout 0, which pays `other`, but record OUR pubkey as owner — the output does not pay us
            var utxo = new OnChainWallet.Utxo(Chain.Txid(tx), 0, tx.Outs[0].Value, 0, 0);
            var coin = new MoneyState.Coin(utxo, me.Pub, new SpvFunding.Proof(tx, 0, header.HashHex(), branch, idx));
            T.Eq(MoneyState.Classify(coin, chain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid,
                "output not paying our key is not our money");
        });

        T.Run("a valid proof that describes a DIFFERENT coin than the UTXO claims is rejected", () =>
        {
            var (tx, header, branch, idx, chain) = Mined(me.Pub, other.Pub, 70000);
            // proof is genuine for (txid,vout=1), but the UTXO record claims vout=0 / a different value
            var proof = new SpvFunding.Proof(tx, 1, header.HashHex(), branch, idx);
            var mismatchedUtxo = new OnChainWallet.Utxo(Chain.Txid(tx), 0, 70000, 0, 0);
            var coin = new MoneyState.Coin(mismatchedUtxo, me.Pub, proof);
            T.Eq(MoneyState.Classify(coin, chain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid,
                "proof must describe the very coin (txid+vout) it is attached to");
        });

        T.Run("a no-proof receipt that is malformed (bad pubkey / non-positive value) is INVALID, not pending", () =>
        {
            var (tx, _, _, _, chain) = Mined(me.Pub, other.Pub, 50000);
            var badKey = new MoneyState.Coin(new OnChainWallet.Utxo(Chain.Txid(tx), 1, 50000, 0, 0), new byte[10], Proof: null);
            T.Eq(MoneyState.Classify(badKey, chain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid, "bad owner key => invalid");
            var zeroVal = new MoneyState.Coin(new OnChainWallet.Utxo(Chain.Txid(tx), 1, 0, 0, 0), me.Pub, Proof: null);
            T.Eq(MoneyState.Classify(zeroVal, chain, NoConflicts).State, MoneyState.State.DoubleSpendOrInvalid, "non-positive value => invalid");
        });

        T.Run("a mixed wallet: balance = sum of CONFIRMED only; every record is retained and bucketable", () =>
        {
            var (tx1, h1, br1, i1, chain) = Mined(me.Pub, other.Pub, 70000); // confirmed
            var confirmed = ConfirmedCoin(me.Pub, tx1, h1, br1, i1);

            var (tx2, _, _, _, _) = Mined(me.Pub, other.Pub, 20000);          // unconfirmed (no proof)
            var unconfirmed = new MoneyState.Coin(new OnChainWallet.Utxo(Chain.Txid(tx2), 1, 20000, 0, 0), me.Pub, null);

            var (tx3, h3, br3, i3, _) = Mined(me.Pub, other.Pub, 90000);       // invalid (forged: proven on a block
            // not in `chain`, plus we mark its input conflicting)
            var dodgy = ConfirmedCoin(me.Pub, tx3, h3, br3, i3);

            var conflict = new HashSet<string> { MoneyState.OutpointKey(tx3.Ins[0].PrevTxid, tx3.Ins[0].Vout) };
            var all = MoneyState.ClassifyAll(new[] { confirmed, unconfirmed, dodgy }, chain, conflict);

            T.Eq(all.Count, 3, "NOTHING dropped — every record retained");
            T.Eq(MoneyState.Balance(all), 70000L, "balance counts only the one Confirmed coin");
            T.Eq(MoneyState.InState(all, MoneyState.State.Confirmed).Count, 1, "1 confirmed bucketed");
            T.Eq(MoneyState.InState(all, MoneyState.State.Unconfirmed).Count, 1, "1 unconfirmed bucketed (visible, not balance)");
            T.Eq(MoneyState.InState(all, MoneyState.State.DoubleSpendOrInvalid).Count, 1, "1 invalid bucketed (visible, not deleted)");
        });
    }
}
