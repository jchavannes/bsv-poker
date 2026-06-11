using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net;
using BsvPoker.Net.Bsv;

namespace BsvPoker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // HEADLESS SELF-TEST: poker.exe --selftest runs the REAL wallet open/close lifecycle 100x with NO
        // window — writes the result to %TEMP%/poker_selftest.txt and exits. Verifies the exe without a screen.
        if (e.Args.Any(a => a == "--selftest")) { RunSelfTest(); Shutdown(); return; }
        // HEADLESS FIND-COINS: poker.exe --findcoins <address> runs the EXACT automatic discovery (connect to
        // public BSV nodes, sync headers, scan blocks) and writes what it finds for that address to
        // %TEMP%/poker_findcoins.txt, then exits. Lets the coin-discovery be verified with NO window.
        int fcIdx = Array.IndexOf(e.Args, "--findcoins");
        if (fcIdx >= 0 && fcIdx + 1 < e.Args.Length) { RunFindCoins(e.Args[fcIdx + 1]).GetAwaiter().GetResult(); Shutdown(); return; }
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose; // close the window => the whole app exits
        // SEQUENTIAL STARTUP (the principal's rule): the wallet SELECTOR is the first thing on screen, ALONE; then
        // the PASSWORD, ALONE; only AFTER a wallet is open and unlocked is the main window shown. The selector and
        // password are modal dialogs run here, BEFORE the main window exists, so there is NEVER a window behind
        // them and they appear one after the other — never together, never with the game/wallet behind.
        var w = new MainWindow();
        if (w.RunStartupLogin())   // selector → password (each alone); false => the user cancelled => exit
        {
            MainWindow = w;        // ShutdownMode.OnMainWindowClose now binds to the real window
            w.Show();
            // EVERY instance is its own player — bring this one to the front so a 2nd/3rd copy is never lost behind
            // another, and let each window be moved/maximised independently on whichever screen you want.
            try { w.Activate(); w.Topmost = true; w.Topmost = false; } catch { }
        }
        else Shutdown();
    }

    /// <summary>Headless verification of the SAME discovery the wallet runs: connect to public BSV nodes (with the
    /// known-good seed peers), sync headers from genesis, scan recent blocks, and report every output paying the
    /// given address. Result → %TEMP%/poker_findcoins.txt.</summary>
    private static async System.Threading.Tasks.Task RunFindCoins(string address)
    {
        string outp = Path.Combine(Path.GetTempPath(), "poker_findcoins.txt");
        var log = new System.Text.StringBuilder();
        void L(string s) { log.AppendLine(s); try { File.WriteAllText(outp, log.ToString()); } catch { } }
        try
        {
            var h160 = Base58.CheckDecode(address)[1..];
            L($"findcoins {address} (hash160 {Convert.ToHexString(h160).ToLowerInvariant()})");
            var node = new BsvNode(NetworkParams.For(BsvNetwork.Mainnet));
            await node.StartAsync(16);
            foreach (var ip in new[] { "135.125.170.182", "198.154.93.204", "198.154.93.210", "198.154.93.212", "135.181.137.155", "141.95.126.79", "57.128.233.172", "57.128.216.248", "162.19.222.167" })
                node.AddManualPeer(ip, 8333);
            for (int i = 0; i < 40 && node.PeerCount < 1; i++) await System.Threading.Tasks.Task.Delay(1000);
            L($"peers={node.PeerCount}");
            if (node.PeerCount < 1) { L("NO PEERS — cannot scan"); return; }
            var store = new HeaderStore(Path.Combine(Path.GetTempPath(), "poker_fc_hdrs.dat"));
            for (int r = 0; r < 600; r++) { var (app, _) = await node.SyncHeadersToStoreAsync(store, maxBatches: 40); if (app == 0) break; }
            var (chain, _) = store.BuildChain(); int tip = chain.Height; var hdrs = store.Load();
            L($"headers synced, tip={tip}");
            if (tip < 1) { L("no headers"); return; }
            long found = 0; int hits = 0; int from = Math.Max(1, tip - 1500);
            var want = Chain.P2pkhLock(h160);
            for (int hgt = from; hgt <= tip; hgt++)
            {
                var bd = (byte[])hdrs[hgt].Hash().Clone(); Array.Reverse(bd); var bh = Convert.ToHexString(bd).ToLowerInvariant();
                var raw = await node.GetBlockAsync(bh, 30000); if (raw == null) continue;
                BsvBlock.Parsed blk; try { blk = BsvBlock.Parse(raw); } catch { continue; }
                foreach (var tx in blk.Txs)
                    for (int v = 0; v < tx.Outs.Count; v++)
                        if (tx.Outs[v].Script.AsSpan().SequenceEqual(want))
                        { found += tx.Outs[v].Value; hits++; L($"FOUND {tx.Outs[v].Value} sat in block {hgt}, txid {Chain.Txid(tx)}:{v}"); }
            }
            L($"RESULT: {hits} output(s), TOTAL {found} sat for {address} (scanned blocks {from}–{tip}).");
            node.Dispose();
        }
        catch (Exception ex) { L("ERROR: " + ex.Message); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode); // guaranteed termination: no lingering threads/orphans
    }

    /// <summary>Run the REAL wallet open/close lifecycle 100x, headless, using the SAME encryption the wallet
    /// uses at rest (<see cref="WalletExtras"/>): create + open with the RIGHT password, REJECT the WRONG
    /// password, confirm the seed round-trips, identity sign+verify, and a real FORKID spend sign+verify — 100
    /// different keys/passwords. Result → %TEMP%/poker_selftest.txt. Touches NOTHING the real wallet uses.</summary>
    private static void RunSelfTest()
    {
        string outp = Path.Combine(Path.GetTempPath(), "poker_selftest.txt");
        int ok = 0, fail = 0; string firstErr = "";
        for (int i = 1; i <= 100; i++)
        {
            try
            {
                byte[] seed = WalletKeys.NewSeed();
                string backup = WalletKeys.SeedToBackup(seed);
                string pw = "pw" + i + "Aa!";

                // (1) encrypt at rest with the password; the WRONG password must be rejected; the RIGHT one
                //     round-trips back to the exact same seed (open / close / open / close).
                string enc = WalletExtras.EncryptSeed(backup, pw);
                if (!WalletExtras.IsEncryptedSeed(enc)) throw new Exception("not recognised as encrypted");
                if (WalletKeys.BackupToSeed(WalletExtras.DecryptSeed(enc, pw)) is not { Length: 32 } s1 || !s1.AsSpan().SequenceEqual(seed)) throw new Exception("open round-trip mismatch");
                bool wrongRejected = false;
                try { WalletExtras.DecryptSeed(enc, pw + "x"); } catch { wrongRejected = true; }
                if (!wrongRejected) throw new Exception("WRONG PASSWORD ACCEPTED");
                if (!WalletKeys.BackupToSeed(WalletExtras.DecryptSeed(enc, pw)).AsSpan().SequenceEqual(seed)) throw new Exception("second open mismatch");

                // (2) identity: a derived attestation sub-key signs the profile; the Base ID never signs
                byte[] attPriv = Type42.UniqueKey(seed, "bsvpoker/identity/attestation");
                byte[] attPub = Secp256k1.PublicKeyCompressed(attPriv);
                byte[] profile = System.Text.Encoding.UTF8.GetBytes("{\"pseudonym\":\"p" + i + "\"}");
                byte[] sig = Secp256k1.Sign(attPriv, profile);
                if (!Secp256k1.Verify(attPub, profile, sig)) throw new Exception("identity signature verify failed");

                // (3) a REAL P2PKH spend, signed and verified against the FORKID sighash
                var k = WalletKeys.Account(seed, 0, 0);
                var tx = new Chain.Tx(2, new() { new(new string('b', 64), 0, Array.Empty<byte>(), 0xffffffff) },
                                          new() { new(50_000, Chain.P2pkhLockForPub(k.Pub)) }, 0);
                var signed = Chain.SignP2pkhInput(tx, 0, k.Priv, k.Pub, 100_000);
                if (!Chain.VerifyP2pkhInput(signed, 0, k.Pub, 100_000)) throw new Exception("spend signature verify failed");

                // (4) STARTUP LOGIN ENFORCEMENT — NO wallet ever opens without a login. The startup decision
                //     (the SAME one Load() uses) must require: encrypted seed → Unlock (password); existing
                //     plaintext seed → must SET a password; absent/garbage seed → the new-wallet wizard.
                if (Views.WalletView.DecideLogin(enc) != Views.WalletView.StartupLogin.Unlock) throw new Exception("encrypted seed did not require Unlock");
                if (Views.WalletView.DecideLogin(backup) != Views.WalletView.StartupLogin.SetPassword) throw new Exception("plaintext seed did not require a password");
                if (Views.WalletView.DecideLogin("not-a-seed-" + i) != Views.WalletView.StartupLogin.NewWizard) throw new Exception("invalid seed did not route to the new-wallet wizard");
                if (Views.WalletView.DecideLogin("") != Views.WalletView.StartupLogin.NewWizard) throw new Exception("empty seed did not route to the new-wallet wizard");
                ok++;
            }
            catch (Exception ex) { fail++; if (firstErr.Length == 0) firstErr = "iter " + i + ": " + ex.Message; }
        }
        try { File.WriteAllText(outp, $"SELFTEST {ok}/100 fail={fail}" + (firstErr.Length > 0 ? " firstErr=" + firstErr : "")); } catch { }
    }
}
