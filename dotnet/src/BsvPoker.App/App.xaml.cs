using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // HEADLESS SELF-TEST: poker.exe --selftest runs the REAL wallet open/close lifecycle 100x with NO
        // window — writes the result to %TEMP%/poker_selftest.txt and exits. Verifies the exe without a screen.
        if (e.Args.Any(a => a == "--selftest")) { RunSelfTest(); Shutdown(); return; }
        base.OnStartup(e);   // StartupUri shows MainWindow; the WALLET requires a password before it can be used
        ShutdownMode = ShutdownMode.OnMainWindowClose; // close the window => the whole app exits
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
                ok++;
            }
            catch (Exception ex) { fail++; if (firstErr.Length == 0) firstErr = "iter " + i + ": " + ex.Message; }
        }
        try { File.WriteAllText(outp, $"SELFTEST {ok}/100 fail={fail}" + (firstErr.Length > 0 ? " firstErr=" + firstErr : "")); } catch { }
    }
}
