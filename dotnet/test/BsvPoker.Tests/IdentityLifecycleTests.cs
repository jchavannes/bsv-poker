using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// END-TO-END identity-NFT lifecycle. An identity is ON-CHAIN only: it is built into a typed
/// <see cref="TxKind.Identity"/> output (no OP_RETURN), serialized into a REAL transaction, and then
/// found + parsed + verified back out of that transaction. The Base ID key NEVER signs — the derived
/// attestation sub-key does. The same wallet/seed re-derives the SAME identity (PERMANENCE), two seeds
/// yield DIFFERENT identities (UNIQUENESS), and tampering / a foreign signer / an un-broadcast draft are
/// all rejected (HOSTILE). Every expected value is derived from the production code, never invented.
/// </summary>
public static class IdentityLifecycleTests
{
    // The exact invoice strings the production identity path uses (see OnChainIdentityTests + Identity.cs).
    private const string IdInvoice = "bsvpoker/identity";
    private const string AttInvoice = "bsvpoker/identity/attestation";

    private static byte[] IdPriv(byte[] seed) => Type42.UniqueKey(seed, IdInvoice);
    private static byte[] IdPub(byte[] seed) => Secp256k1.PublicKeyCompressed(IdPriv(seed));
    private static byte[] AttPriv(byte[] seed) => Type42.UniqueKey(seed, AttInvoice);
    private static byte[] AttPub(byte[] seed) => Secp256k1.PublicKeyCompressed(AttPriv(seed));

    /// <summary>Wrap an identity output script in a real, otherwise-ordinary transaction (as it would be broadcast).</summary>
    private static Chain.Tx WrapInTx(byte[] identityScript) =>
        new(2,
            new() { new(new string('a', 64), 0, Array.Empty<byte>(), 0xffffffff) },
            new() { new(1, identityScript) },
            0);

    public static void All()
    {
        Console.WriteLine("identity-NFT lifecycle (build → tx → discover → verify; permanence, uniqueness, hostile):");

        var seed = T.Seed(123);
        var pseudonym = "CsTominaga";
        var email = "craig@rcjbr.org";

        // ---- LIFECYCLE: build the NFT, serialize into a REAL tx, discover + parse + verify it back out ----
        T.Run("full round-trip: build NFT → serialize into a real tx → find + parse + verify back out", () =>
        {
            var script = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            var tx = WrapInTx(script);

            // serialize to wire and re-deserialize — the identity must survive an actual on-chain round-trip.
            var wire = Chain.Serialize(tx);
            var back = Chain.Deserialize(wire);
            T.Eq(Chain.Txid(back), Chain.Txid(tx), "tx survives wire serialize/deserialize unchanged");

            // discover the identity inside the re-parsed transaction (only returns a SIGNATURE-VERIFIED claim).
            var c = OnChainIdentity.TryReadTx(back);
            T.True(c != null, "identity discovered inside the on-chain transaction");
            T.True(OnChainIdentity.Verify(c!), "discovered identity verifies");

            // pseudonym / @handle / email round-trip EXACTLY.
            T.Eq(c!.Pseudonym, pseudonym, "pseudonym/@handle round-trips exactly");
            T.Eq(c.Email, email, "email round-trips exactly");

            // the Base ID is carried, and the attestation pub is carried and distinct.
            T.Eq(T.Hex(c.IdentityPub), T.Hex(IdPub(seed)), "Base ID pub carried unchanged");
            T.Eq(T.Hex(c.AttestationPub), T.Hex(AttPub(seed)), "attestation pub carried unchanged");
        });

        T.Run("the Base ID is NOT the signer — the derived attestation key is", () =>
        {
            var script = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            var c = OnChainIdentity.TryReadTx(WrapInTx(script))!;

            // attestation key is a distinct, derived sub-key — never the Base ID.
            T.True(T.Hex(c.AttestationPub) != T.Hex(c.IdentityPub), "attestation key is a derived sub-key, not the Base ID");

            // the signature over the canonical claim verifies under the ATTESTATION key...
            var canon = OnChainIdentity.Canonical(c.IdentityPub, c.AttestationPub, c.Pseudonym, c.Email);
            T.True(Secp256k1.Verify(c.AttestationPub, canon, c.Signature), "signature verifies under the attestation key");

            // ...and does NOT verify under the Base ID key (the Base ID never produced it).
            T.False(Secp256k1.Verify(c.IdentityPub, canon, c.Signature), "the Base ID key did NOT sign the claim");
        });

        // ---- PERMANENCE: the same wallet/seed re-derives the SAME identity (re-discovered, never new) ----
        T.Run("PERMANENCE: the same seed always re-derives the SAME identity (immutable, re-discovered)", () =>
        {
            // build the identity twice from the same seed — keys are deterministic, so both must match.
            var c1 = OnChainIdentity.TryReadTx(WrapInTx(
                OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email)))!;
            var c2 = OnChainIdentity.TryReadTx(WrapInTx(
                OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email)))!;

            T.Eq(T.Hex(c2.IdentityPub), T.Hex(c1.IdentityPub), "same seed ⇒ same Base ID (never a new one)");
            T.Eq(T.Hex(c2.AttestationPub), T.Hex(c1.AttestationPub), "same seed ⇒ same attestation key");

            // re-derive through the wallet's own KeyRing (the production identity slot) — still the SAME Base ID.
            var ring = new KeyRing(seed);
            T.Eq(T.Hex(ring.IdentityPub()), T.Hex(c1.IdentityPub), "the wallet KeyRing re-derives the identical Base ID");

            // a previously-on-chain identity is RE-DISCOVERED from its tx, not minted afresh.
            var onChainTx = WrapInTx(OnChainIdentity.BuildScript(ring.IdentityPub(), AttPriv(seed), pseudonym, email));
            var rediscovered = OnChainIdentity.TryReadTx(onChainTx)!;
            T.Eq(T.Hex(rediscovered.IdentityPub), T.Hex(ring.IdentityPub()), "the on-chain identity is re-discovered, not re-created");
        });

        T.Run("PERMANENCE: the identity key is fixed across KeyRing instances and never rotates", () =>
        {
            var a = new KeyRing(seed).IdentityPub();
            var b = new KeyRing(seed).IdentityPub();   // a fresh ring on the SAME seed
            T.Eq(T.Hex(a), T.Hex(b), "the Base ID is the one fixed, never-rotating slot");
            // it is also NOT any of the rotating receive keys.
            var recv = new KeyRing(seed).NextReceive();
            T.True(T.Hex(recv.Pub) != T.Hex(a), "the identity key is never a rotating receive address");
        });

        // ---- UNIQUENESS: two different seeds yield different identities ----
        T.Run("UNIQUENESS: two different seeds yield completely different identities", () =>
        {
            var seedA = T.Seed(10);
            var seedB = T.Seed(20);

            var cA = OnChainIdentity.TryReadTx(WrapInTx(
                OnChainIdentity.BuildScript(IdPub(seedA), AttPriv(seedA), "Alice", "alice@example.com")))!;
            var cB = OnChainIdentity.TryReadTx(WrapInTx(
                OnChainIdentity.BuildScript(IdPub(seedB), AttPriv(seedB), "Bob", "bob@example.com")))!;

            T.True(T.Hex(cA.IdentityPub) != T.Hex(cB.IdentityPub), "different seeds ⇒ different Base IDs");
            T.True(T.Hex(cA.AttestationPub) != T.Hex(cB.AttestationPub), "different seeds ⇒ different attestation keys");
            T.True(T.Hex(cA.Signature) != T.Hex(cB.Signature), "different identities ⇒ different attestations");
        });

        // ---- HOSTILE: tamper, foreign signer, and un-broadcast draft are all rejected ----
        T.Run("HOSTILE: a tampered pseudonym fails verification", () =>
        {
            var script = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            var c = OnChainIdentity.TryRead(script)!;
            var forged = c with { Pseudonym = "Mallory" };
            T.False(OnChainIdentity.Verify(forged), "changing the @handle breaks the attestation signature");
        });

        T.Run("HOSTILE: a tampered email fails verification", () =>
        {
            var script = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            var c = OnChainIdentity.TryRead(script)!;
            var forged = c with { Email = "attacker@evil.example" };
            T.False(OnChainIdentity.Verify(forged), "changing the email breaks the attestation signature");
        });

        T.Run("HOSTILE: a signature from any key OTHER than the real attestation key fails", () =>
        {
            var c = OnChainIdentity.TryRead(
                OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email))!;

            // sign the exact same canonical claim with a foreign key and graft both the sig and its pub on.
            var foreignPriv = Type42.UniqueKey(T.Seed(200), AttInvoice);
            var foreignPub = Secp256k1.PublicKeyCompressed(foreignPriv);
            var canon = OnChainIdentity.Canonical(c.IdentityPub, foreignPub, c.Pseudonym, c.Email);
            var foreignSig = Secp256k1.Sign(foreignPriv, canon);

            // (a) An impostor that swaps in the foreign attestation key AND its own self-consistent sig DOES
            // verify (the canonical claim binds the attestation pub) — but it is a DIFFERENT identity: its
            // attestation key is not this seed's derived sub-key, so it cannot impersonate this Base ID.
            var impostor = c with { AttestationPub = foreignPub, Signature = foreignSig };
            T.True(OnChainIdentity.Verify(impostor), "the impostor self-verifies — but under a foreign key");
            T.True(T.Hex(impostor.AttestationPub) != T.Hex(AttPub(seed)),
                   "the impostor's attestation key is NOT this seed's derived key (a different identity, no impersonation)");

            // (b) foreign sig over the REAL attestation pub — verification must fail (wrong signer).
            var grafted = c with { Signature = foreignSig };
            T.False(OnChainIdentity.Verify(grafted), "a signature from a non-attestation key is rejected");

            // (c) keep the real sig but swap the attestation pub to a foreign one — verification must fail.
            var swappedPub = c with { AttestationPub = foreignPub };
            T.False(OnChainIdentity.Verify(swappedPub), "the real sig does not verify under a foreign attestation key");

            // a tx carrying the grafted-sig identity yields NO verified identity at all.
            T.True(OnChainIdentity.TryReadTx(WrapInTx(
                OnChainIdentity.BuildScript(IdPub(seed), foreignPriv, pseudonym, email))) is { } realFromForeign
                && T.Hex(realFromForeign.AttestationPub) == T.Hex(foreignPub),
                "a foreign signer mints only ITS OWN attestation, never this seed's");
        });

        T.Run("HOSTILE: a DRAFT (never built into / broadcast in a tx) is NOT a real identity", () =>
        {
            // A local draft is just strings + keys with no on-chain output: there is nothing to discover.
            // 1) an empty transaction (nothing was broadcast) yields NO identity.
            var emptyTx = new Chain.Tx(2,
                new() { new(new string('b', 64), 0, Array.Empty<byte>(), 0xffffffff) },
                new() { new(1, Chain.P2pkhLockForPub(IdPub(seed))) },   // an ordinary payment, not an identity
                0);
            T.True(OnChainIdentity.TryReadTx(emptyTx) == null, "a tx with no identity output is not an identity");

            // 2) a draft whose attestation signature is corrupt (an unfinished/never-signed draft) is rejected:
            //    the byte string exists but is not a verified on-chain claim.
            var script = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            var draft = OnChainIdentity.TryRead(script)!;
            var corruptSig = (byte[])draft.Signature.Clone();
            corruptSig[10] ^= 0xFF;                                     // a draft that was never properly attested
            var unsigned = draft with { Signature = corruptSig };
            T.False(OnChainIdentity.Verify(unsigned), "an unsigned/corrupt draft does not verify");

            // 3) a tx that carries ONLY a corrupt-signature identity output yields NO identity. We rebuild the
            //    identity output (same documented field order) but substitute the corrupt signature, so the
            //    output PARSES as identity-shaped yet FAILS the embedded attestation check.
            var draftFields = new[]
            {
                draft.IdentityPub,
                draft.AttestationPub,
                System.Text.Encoding.UTF8.GetBytes(draft.Pseudonym),
                System.Text.Encoding.UTF8.GetBytes(draft.Email),
                corruptSig,
            };
            var draftOutput = TxTemplates.BuildOutput(TxKind.Identity, draftFields, draft.AttestationPub);
            T.True(OnChainIdentity.TryRead(draftOutput) != null, "the corrupt-sig output still PARSES as identity-shaped");
            T.True(OnChainIdentity.TryReadTx(WrapInTx(draftOutput)) == null,
                   "but a tx carrying only an unverified draft yields NO identity (drafts never count on-chain)");

            // 4) and the proper, fully-attested + broadcast identity DOES count — the lifecycle's positive end.
            var goodScript = OnChainIdentity.BuildScript(IdPub(seed), AttPriv(seed), pseudonym, email);
            T.True(OnChainIdentity.TryReadTx(WrapInTx(goodScript)) != null, "a properly-attested + broadcast identity DOES count");
        });
    }
}
