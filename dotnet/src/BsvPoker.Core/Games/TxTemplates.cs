using System.Text;

namespace BsvPoker.Core;

/// <summary>Every distinct on-chain action type. Each is its own documented transaction template.</summary>
public enum TxKind
{
    Payment, KeepAlive,
    ChatDirect, ChatGroup,
    CardNft, Commitment, Reveal, ShuffleStage, Deal, BoardReveal, Showdown,
    Bet, PotEscrow, Settlement, Recovery,
    Bid, Auction, RoleClaim,
    TableGenesis, GameStart, HandStart
}

/// <summary>
/// The typed-transaction-template registry. A transaction is like a frame in IP: every kind of action has
/// its OWN type — a unique tag ("frame type"), a version, a documented purpose, and its own contract. The
/// type is stamped into the transaction's script as a data push followed by <c>OP_DROP</c> (never
/// <c>OP_RETURN</c>), so any peer can recognize what a transaction is for. Nothing is generic or assumed.
/// </summary>
public static class TxTemplates
{
    public sealed record Template(TxKind Kind, string Tag, int Version, string Purpose);

    private const byte OP_DROP = 0x75, OP_PUSHDATA1 = 0x4c;

    // Each tag is unique and self-describing; bump the version when a template's structure changes.
    private static readonly Dictionary<TxKind, Template> Registry = new[]
    {
        new Template(TxKind.Payment,      "BSVP:PAY:1",   1, "A plain value transfer of satoshis between wallets."),
        new Template(TxKind.KeepAlive,    "BSVP:KA:1",    1, "A liveness heartbeat tx proving a peer/seat is still present."),
        new Template(TxKind.ChatDirect,   "BSVP:DM:1",    1, "A one-to-one encrypted chat message (ECDH+AES) carried on-chain."),
        new Template(TxKind.ChatGroup,    "BSVP:GC:1",    1, "A group encrypted chat message (per-recipient ECDH+AES)."),
        new Template(TxKind.CardNft,      "BSVP:NFT:1",   1, "A card minted/held/transferred as a 1-sat NFT, sealed to its owner."),
        new Template(TxKind.Commitment,   "BSVP:CMT:1",   1, "A hash commitment (e.g. to shuffle entropy) published before reveal."),
        new Template(TxKind.Reveal,       "BSVP:RVL:1",   1, "The reveal of a prior commitment, verifiable against it."),
        new Template(TxKind.ShuffleStage, "BSVP:SHF:1",   1, "One seat's masking+shuffle stage of the commutative-encryption deal."),
        new Template(TxKind.Deal,         "BSVP:DEAL:1",  1, "Dealing a card to a seat (per-position mask reveal to the recipient)."),
        new Template(TxKind.BoardReveal,  "BSVP:BRD:1",   1, "A community-board card revealed by all seats for a street."),
        new Template(TxKind.Showdown,     "BSVP:SHO:1",   1, "Showdown reveal of a seat's hole cards for settlement."),
        new Template(TxKind.Bet,          "BSVP:BET:1",   1, "A betting action (check/call/bet/raise/fold) committed on-chain."),
        new Template(TxKind.PotEscrow,    "BSVP:POT:1",   1, "The pot held in a threshold (n-of-n) escrow contract."),
        new Template(TxKind.Settlement,   "BSVP:STL:1",   1, "Cooperative settlement paying the pot to the winner(s)."),
        new Template(TxKind.Recovery,     "BSVP:REC:1",   1, "A pre-signed nLockTime refund returning funds if a peer stalls."),
        new Template(TxKind.Bid,          "BSVP:BID:1",   1, "A conditional bid in an on-chain auction (a full smart contract)."),
        new Template(TxKind.Auction,      "BSVP:AUC:1",   1, "An auction genesis defining the item/role and rules."),
        new Template(TxKind.RoleClaim,    "BSVP:ROLE:1",  1, "Claiming an auctioned role (banker, dealer, draw rights)."),
        new Template(TxKind.TableGenesis, "BSVP:TBL:1",   1, "Creating a table (its genesis transaction)."),
        new Template(TxKind.GameStart,    "BSVP:GAME:1",  1, "Starting a game at a table."),
        new Template(TxKind.HandStart,    "BSVP:HAND:1",  1, "Starting a hand within a game."),
    }.ToDictionary(t => t.Kind);

    public static Template Of(TxKind kind) => Registry[kind];
    public static IReadOnlyCollection<Template> All => Registry.Values;

    /// <summary>The type marker bytes for a kind (its on-chain "frame type").</summary>
    public static byte[] Marker(TxKind kind) => Encoding.ASCII.GetBytes(Of(kind).Tag);

    /// <summary>A type-prefix script fragment: push the marker, then OP_DROP — prepended to the action's contract.</summary>
    public static byte[] TypePrefix(TxKind kind)
    {
        var m = Marker(kind);
        var b = new List<byte>();
        if (m.Length < OP_PUSHDATA1) b.Add((byte)m.Length); else { b.Add(OP_PUSHDATA1); b.Add((byte)m.Length); }
        b.AddRange(m);
        b.Add(OP_DROP);
        return b.ToArray();
    }

    /// <summary>Recognize a transaction's type from the leading type marker of its script; null if untyped.</summary>
    public static TxKind? Recognize(byte[] script)
    {
        try
        {
            int p = 0; int len;
            if (script[p] == OP_PUSHDATA1) { len = script[p + 1]; p += 2; } else { len = script[p]; p += 1; }
            if (len <= 0 || len > 64 || p + len + 1 > script.Length) return null;
            if (script[p + len] != OP_DROP) return null; // a type marker is always <push> OP_DROP
            var tag = Encoding.ASCII.GetString(script, p, len);
            foreach (var t in Registry.Values) if (t.Tag == tag) return t.Kind;
            return null;
        }
        catch { return null; }
    }
}
