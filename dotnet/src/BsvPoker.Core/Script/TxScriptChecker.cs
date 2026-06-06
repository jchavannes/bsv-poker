using BsvPoker.Crypto;

namespace BsvPoker.Core.Script;

/// <summary>
/// Binds the Script engine's CHECKSIG / CLTV / CSV checks to a real spending transaction, so on-chain
/// smart contracts are actually spendable: CHECKSIG verifies a (canonical-DER ‖ 0x41) signature against
/// the BSV FORKID sighash of the spend, and CHECKLOCKTIMEVERIFY / CHECKSEQUENCEVERIFY gate on the tx's
/// locktime / input sequence. This is what makes the contracts in <see cref="Contracts"/> real money.
/// </summary>
public sealed class TxScriptChecker : ScriptEngine.IChecker
{
    private readonly Chain.Tx _tx;
    private readonly int _index;
    private readonly byte[] _scriptCode;
    private readonly long _amount;

    public TxScriptChecker(Chain.Tx tx, int index, byte[] scriptCode, long amount)
    {
        _tx = tx; _index = index; _scriptCode = scriptCode; _amount = amount;
    }

    public bool CheckSig(byte[] sig, byte[] pubKey)
    {
        try
        {
            if (sig.Length < 1 || sig[^1] != Chain.ContractHashType) return false; // require SIGHASH_ALL|FORKID
            var compact = Chain.ParseStrictDer(sig[..^1]);
            if (compact == null) return false;
            var digest = Chain.ContractSighash(_tx, _index, _scriptCode, _amount);
            return Secp256k1.VerifyDigest(pubKey, digest, compact);
        }
        catch { return false; }
    }

    // CLTV: valid when the tx's locktime has reached the contract's value and the input is non-final.
    public bool CheckLockTime(long lockTime)
        => lockTime >= 0 && _tx.LockTime >= (uint)lockTime && _tx.Ins[_index].Sequence != 0xffffffff;

    // CSV: valid when the input's relative sequence is at least the contract's value (simplified BIP-style check).
    public bool CheckSequence(long sequence) => sequence >= 0 && _tx.Ins[_index].Sequence >= (uint)sequence;

    /// <summary>Sign for a contract input: canonical DER over the FORKID sighash, plus the 0x41 hashtype byte.</summary>
    public static byte[] Sign(Chain.Tx tx, int index, byte[] scriptCode, long amount, byte[] privSeed)
    {
        var digest = Chain.ContractSighash(tx, index, scriptCode, amount);
        var der = Secp256k1.ToDer(Secp256k1.SignDigest(privSeed, digest));
        return der.Concat(new[] { Chain.ContractHashType }).ToArray();
    }
}
