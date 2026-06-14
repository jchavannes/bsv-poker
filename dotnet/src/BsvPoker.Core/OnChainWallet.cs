using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// The real-BSV spending wallet: it tracks UTXOs owned by keys derived from the master seed, selects coins
/// to fund a payment, builds the transaction, signs every input (secp256k1, low-S, FORKID), returns change,
/// and the result is consensus-verifiable. UTXOs arrive over the P2P/SPV layer (a payer hands them over
/// with merkle proofs); broadcasting is done by the client's own <c>BsvNode</c>. Same model on all networks.
/// </summary>
public sealed class OnChainWallet
{
    /// <summary>An unspent output owned by this wallet, with the (chain,index) of the key that locks it. If
    /// <paramref name="CustodyPubA"/>/<paramref name="CustodyPubB"/> are set, the coin is a 1-of-2 RECOVERABLE
    /// CUSTODY output (<c>MultisigLock1of2(CustodyPubA, CustodyPubB)</c>) that EITHER party can spend — this
    /// wallet signs it with its own key (the one of A/B it holds). Both null = an ordinary P2PKH coin.</summary>
    public sealed record Utxo(string Txid, uint Vout, long Value, uint KeyChain, uint KeyIndex, byte[]? CustodyPubA = null, byte[]? CustodyPubB = null);

    /// <summary>Sign input <paramref name="i"/> for coin <paramref name="u"/>: P2PKH normally, or a 1-of-2 custody
    /// signature (with this wallet's key) when the coin is a custody output. One place so every build path agrees.</summary>
    private Chain.Tx SignInput(Chain.Tx tx, int i, Utxo u)
    {
        var k = WalletKeys.Account(_seed, u.KeyChain, u.KeyIndex);
        if (u.CustodyPubA == null || u.CustodyPubB == null) return Chain.SignP2pkhInput(tx, i, k.Priv, k.Pub, u.Value);
        var sig = Chain.SignMultisig1of2(tx, i, u.CustodyPubA, u.CustodyPubB, u.Value, k.Priv);   // either party's key spends
        return Chain.ApplyMultisig1of2ScriptSig(tx, i, sig);
    }

    private readonly byte[] _seed;
    private readonly List<Utxo> _utxos = new();
    private uint _nextChange;

    public OnChainWallet(byte[] seed32) { _seed = seed32; }

    public void Add(Utxo u) => _utxos.Add(u);
    public long Balance => _utxos.Sum(u => u.Value);
    public IReadOnlyList<Utxo> Coins => _utxos;

    public sealed record Spend(Chain.Tx Tx, IReadOnlyList<Utxo> Inputs, long Fee, long Change);

    /// <summary>
    /// Build and sign a payment of <paramref name="amount"/> to <paramref name="recipientPub33"/> with the
    /// given <paramref name="fee"/>. Selects coins largest-first, returns change to a fresh change key.
    /// Throws on insufficient funds. The returned tx is fully signed and verifiable.
    /// </summary>
    public Spend BuildPayment(byte[] recipientPub33, long amount, long fee)
    {
        if (amount <= 0 || fee < 0) throw new ArgumentException("bad amount/fee");
        long need = amount + fee;
        var chosen = new List<Utxo>(); long sum = 0;
        foreach (var u in _utxos.OrderByDescending(u => u.Value))
        {
            chosen.Add(u); sum += u.Value;
            if (sum >= need) break;
        }
        if (sum < need) throw new InvalidOperationException($"insufficient funds: have {sum}, need {need}");

        long change = sum - need;
        var ins = chosen.Select(u => new Chain.TxIn(u.Txid, u.Vout, Array.Empty<byte>(), 0xffffffff)).ToList();
        var outs = new List<Chain.TxOut> { new(amount, Chain.P2pkhLockForPub(recipientPub33)) };
        byte[]? changePub = null;
        if (change > 0)
        {
            var ck = WalletKeys.Account(_seed, 1, _nextChange++);
            changePub = ck.Pub;
            outs.Add(new Chain.TxOut(change, Chain.P2pkhLockForPub(changePub)));
        }
        var tx = new Chain.Tx(2, ins, outs, 0);
        for (int i = 0; i < chosen.Count; i++) tx = SignInput(tx, i, chosen[i]);
        return new Spend(tx, chosen, fee, change);
    }

    /// <summary>
    /// Fund an arbitrary OUTPUT SCRIPT (e.g. a typed transaction template or a Script contract) with the
    /// given output value, paying the fee and returning change. This is how every on-chain game action
    /// (table genesis, deal, bet, card-NFT, escrow, settlement) is funded: a typed/contract output + change,
    /// fully signed. Throws on insufficient funds.
    /// </summary>
    public Spend BuildAction(byte[] outputScript, long outputValue, long fee)
    {
        if (outputValue < 0 || fee < 0) throw new ArgumentException("bad value/fee");
        long need = outputValue + fee;
        var chosen = new List<Utxo>(); long sum = 0;
        foreach (var u in _utxos.OrderByDescending(u => u.Value)) { chosen.Add(u); sum += u.Value; if (sum >= need) break; }
        if (sum < need) throw new InvalidOperationException($"insufficient funds: have {sum}, need {need}");
        long change = sum - need;
        var ins = chosen.Select(u => new Chain.TxIn(u.Txid, u.Vout, Array.Empty<byte>(), 0xffffffff)).ToList();
        var outs = new List<Chain.TxOut> { new(outputValue, outputScript) };
        if (change > 0) outs.Add(new Chain.TxOut(change, Chain.P2pkhLockForPub(WalletKeys.Account(_seed, 1, _nextChange++).Pub)));
        var tx = new Chain.Tx(2, ins, outs, 0);
        for (int i = 0; i < chosen.Count; i++) tx = SignInput(tx, i, chosen[i]);
        return new Spend(tx, chosen, fee, change);
    }

    /// <summary>
    /// Fund MANY outputs at once (e.g. a BIP270 merchant invoice whose payment request lists several outputs),
    /// paying the fee and returning change. Coin selection is largest-first; change pays a fresh change key.
    /// Every input is signed (secp256k1, low-S, FORKID). Throws on insufficient funds.
    /// </summary>
    public Spend BuildActionMany(IReadOnlyList<(byte[] Script, long Value)> outputs, long fee)
    {
        if (fee < 0 || outputs.Any(o => o.Value < 0)) throw new ArgumentException("bad value/fee");
        long outValue = outputs.Sum(o => o.Value);
        long need = outValue + fee;
        var chosen = new List<Utxo>(); long sum = 0;
        foreach (var u in _utxos.OrderByDescending(u => u.Value)) { chosen.Add(u); sum += u.Value; if (sum >= need) break; }
        if (sum < need) throw new InvalidOperationException($"insufficient funds: have {sum}, need {need}");
        long change = sum - need;
        var ins = chosen.Select(u => new Chain.TxIn(u.Txid, u.Vout, Array.Empty<byte>(), 0xffffffff)).ToList();
        var outs = outputs.Select(o => new Chain.TxOut(o.Value, o.Script)).ToList();
        if (change > 0) outs.Add(new Chain.TxOut(change, Chain.P2pkhLockForPub(WalletKeys.Account(_seed, 1, _nextChange++).Pub)));
        var tx = new Chain.Tx(2, ins, outs, 0);
        for (int i = 0; i < chosen.Count; i++) tx = SignInput(tx, i, chosen[i]);
        return new Spend(tx, chosen, fee, change);
    }

    /// <summary>
    /// Build a typed/contract-output spend AND advance the wallet's own state: remove the spent inputs and
    /// re-absorb the change output as a fresh UTXO, so the NEXT call spends the change. This is what lets a
    /// single hand emit MANY on-chain transactions in sequence (every action its own tx) from one starting
    /// coin. Returns the signed spend to broadcast.
    /// </summary>
    public Spend SpendAction(byte[] outputScript, long outputValue, long fee)
    {
        uint changeIndexBefore = _nextChange;
        var s = BuildAction(outputScript, outputValue, fee);
        foreach (var inp in s.Inputs) _utxos.RemoveAll(u => u.Txid == inp.Txid && u.Vout == inp.Vout);
        if (s.Change > 0)
        {
            // BuildAction appends change as the LAST output, paying change key (chain 1, changeIndexBefore)
            int vout = s.Tx.Outs.Count - 1;
            _utxos.Add(new Utxo(Chain.Txid(s.Tx), (uint)vout, s.Change, 1, changeIndexBefore));
        }
        return s;
    }

    /// <summary>Like <see cref="SpendAction"/> but pays a recipient pubkey (a plain payment), advancing wallet state.</summary>
    public Spend SpendPayment(byte[] recipientPub33, long amount, long fee)
        => SpendAction(Chain.P2pkhLockForPub(recipientPub33), amount, fee);

    /// <summary>Verify every input of a spend this wallet built (consensus check); also confirms value conservation.</summary>
    public bool VerifySpend(Spend s)
    {
        long inSum = 0;
        for (int i = 0; i < s.Inputs.Count; i++)
        {
            var u = s.Inputs[i];
            if (u.CustodyPubA != null && u.CustodyPubB != null)
            { if (!Chain.VerifyMultisig1of2(s.Tx, i, u.CustodyPubA, u.CustodyPubB, u.Value)) return false; }
            else
            { var pub = WalletKeys.Account(_seed, u.KeyChain, u.KeyIndex).Pub; if (!Chain.VerifyP2pkhInput(s.Tx, i, pub, u.Value)) return false; }
            inSum += u.Value;
        }
        long outSum = s.Tx.Outs.Sum(o => o.Value);
        return inSum == outSum + s.Fee;   // value conserved (inputs = outputs + fee)
    }
}
