using System.Security.Cryptography;
using BsvPoker.Core;
using BsvPoker.Crypto;

// Testnet exercise tx-builder for the agent-controlled wallet (_testnet_wallet.txt). Builds REAL signed
// testnet transactions with the Core libraries and prints raw hex for broadcast (WhatsOnChain
// POST /v1/bsv/test/tx/raw). Every action is its own tx with a tiny ~1-sat fee. No window, no GUI.
//
//   tnet addr <chain> <index>
//   tnet send <txid> <vout> <value> [destAddr]
//   tnet exercise <txid> <vout> <value>   -> the FULL chained on-chain tape from one funding UTXO

const byte TVER = 0x6f;   // testnet P2PKH version
const long FEE = 1;       // tiny per-tx fee (tech demo; economics irrelevant)
var walletFile = @"D:\claude\Mental Poker\bsv-poker\_testnet_wallet.txt";
var backup = File.ReadAllLines(walletFile).First(l => l.StartsWith("SEEDBACKUP=")).Substring("SEEDBACKUP=".Length).Trim();
var seed = WalletKeys.BackupToSeed(backup);

string AddrOf(uint chain, uint index)
{
    var pub = WalletKeys.Account(seed, chain, index).Pub;
    var payload = new byte[21]; payload[0] = TVER; Hashes.Hash160(pub).CopyTo(payload, 1);
    return Base58.CheckEncode(payload);
}
byte[] Hash160OfAddr(string a) { var p = Base58.CheckDecode(a); if (p.Length != 21 || p[0] != TVER) throw new Exception("not a testnet P2PKH address"); return p[1..]; }
void Emit(string label, Chain.Tx tx)
{
    Console.WriteLine($"--- {label} ---");
    Console.WriteLine("TXID=" + Chain.Txid(tx));
    Console.WriteLine("RAW=" + Convert.ToHexString(Chain.Serialize(tx)).ToLowerInvariant());
}

if (args.Length == 0) { Console.WriteLine("usage: tnet addr <c> <i> | tnet send <txid> <vout> <value> [dest] | tnet exercise <txid> <vout> <value>"); return; }

switch (args[0])
{
    case "addr":
        Console.WriteLine("ADDR=" + AddrOf(uint.Parse(args[1]), uint.Parse(args[2])));
        break;

    case "send":
    {
        string fundTxid = args[1]; uint vout = uint.Parse(args[2]); long value = long.Parse(args[3]);
        var key0 = WalletKeys.Account(seed, 0, 0);
        string dest = args.Length > 4 ? args[4] : AddrOf(1, 0);
        var ins = new List<Chain.TxIn> { new(fundTxid, vout, Array.Empty<byte>(), 0xffffffff) };
        var outs = new List<Chain.TxOut> { new(value - FEE, Chain.P2pkhLock(Hash160OfAddr(dest))) };
        var tx = Chain.SignP2pkhInput(new Chain.Tx(2, ins, outs, 0), 0, key0.Priv, key0.Pub, value);
        Console.WriteLine("VERIFY_INPUT=" + Chain.VerifyP2pkhInput(tx, 0, key0.Pub, value));
        Emit("send", tx);
        break;
    }

    case "exercise":
    {
        // The principal's ruling realised on-chain: every move is its own funded transaction, ~1-sat fees,
        // the pot under a DEALERLESS threshold-custody key, chained off one funding UTXO (SPV-trusted while
        // unconfirmed). This builds the whole ordered tape; broadcast the change-chain in order, then settle.
        string fundTxid = args[1]; uint vout = uint.Parse(args[2]); long value = long.Parse(args[3]);
        var w = new OnChainWallet(seed);
        w.Add(new OnChainWallet.Utxo(fundTxid, vout, value, 0, 0));   // funded at our receive key (0,0)
        var key00 = WalletKeys.Account(seed, 0, 0);

        var pot = ThresholdEcdsa.Jvrss(n: 3, degree: 1);             // dealerless (2-of-3) pot key — no one holds it
        var potLock = ThresholdCustody.PotLock(pot);
        var potUtxos = new List<(string Txid, uint Vout, long Value)>();
        var ordered = new List<(string Label, Chain.Tx Tx)>();

        // 1) post blind / fund the pot — a real funded tx into the threshold key
        var f1 = w.SpendAction(potLock, 100, FEE); ordered.Add(("pot-fund(blind)", f1.Tx)); potUtxos.Add((Chain.Txid(f1.Tx), 0, 100));
        // 2) betting tape — EVERY move its own funded tx paying the wager into the pot
        foreach (var wager in new long[] { 20, 30, 20 }) { var b = w.SpendAction(potLock, wager, FEE); ordered.Add(("bet(" + wager + ")", b.Tx)); potUtxos.Add((Chain.Txid(b.Tx), 0, wager)); }
        // 3) take a card — issued as an encrypted 1-sat on-chain NFT (sealed to our key)
        var blind = RandomNumberGenerator.GetBytes(32);
        var sealedHex = CardNft.SealToPub(7, blind, key00.Pub);
        var nft = w.SpendAction(CardNft.NftLock(sealedHex, key00.Pub), 1, FEE); ordered.Add(("card-nft", nft.Tx));
        // 4) a plain send of change (real P2PKH spend)
        var snd = w.SpendAction(Chain.P2pkhLockForPub(WalletKeys.Account(seed, 1, 50).Pub), 50, FEE); ordered.Add(("send", snd.Tx));
        // 5) showdown — threshold-settle the WHOLE pot to the winner (spends every pot UTXO, threshold-signed)
        var winner = WalletKeys.Account(seed, 2, 0).Pub;
        long potTotal = potUtxos.Sum(u => u.Value);
        var settle = ThresholdCustody.SettleManyToWinner(potUtxos, winner, FEE, pot);
        // (alternative path, not broadcast) the always-recoverable nLockTime threshold refund
        var recovery = ThresholdCustody.BuildRecovery(potUtxos[0].Txid, potUtxos[0].Vout, potUtxos[0].Value,
            new[] { (key00.Pub, potUtxos[0].Value - FEE) }, pot, lockHeight: 1_900_000);

        Console.WriteLine("POT_KEY=" + Convert.ToHexString(pot.PublicKey).ToLowerInvariant());
        Console.WriteLine("POT_ADDR=" + Base58.CheckEncode(new byte[] { TVER }.Concat(Hashes.Hash160(pot.PublicKey)).ToArray()));
        Console.WriteLine("# BROADCAST ORDER: the change-chain below in order, then pot-settle.");
        foreach (var (label, tx) in ordered) Emit(label, tx);
        Emit("pot-settle(threshold)", settle);
        Emit("pot-recovery(nLockTime, alt)", recovery);

        // self-verification: every tx is consensus-valid on the ordinary path
        bool ok = Chain.VerifyP2pkhInput(f1.Tx, 0, key00.Pub, value);                 // funding input
        for (int i = 0; i < potUtxos.Count; i++) ok &= Chain.VerifyP2pkhInput(settle, i, pot.PublicKey, potUtxos[i].Value);  // threshold settle inputs
        ok &= Chain.VerifyP2pkhInput(recovery, 0, pot.PublicKey, potUtxos[0].Value);  // threshold recovery input
        ok &= settle.Outs[0].Value == potTotal - FEE;                                 // value conserved
        Console.WriteLine("ALL_VERIFY=" + ok + "  pot_total=" + potTotal + "  winner_gets=" + settle.Outs[0].Value + "  txs=" + (ordered.Count + 2));
        break;
    }

    default: Console.WriteLine("unknown command: " + args[0]); break;
}
