using System.Collections.Concurrent;
using BsvPoker.Net;

namespace BsvPoker.App;

/// <summary>
/// A <see cref="LiveDeal.IDealChannel"/> that carries the mental-poker deal between two real players where
/// every message is a Bitcoin transaction: <see cref="Send"/> hands the plaintext to the owner's send action
/// (which encrypts it to the peer, funds a ChatDirect transaction, pushes it IP-to-IP to the peer AND
/// broadcasts it to the miners); <see cref="Receive"/> waits for the next message the peer pushed to us.
/// No shared RNG, no single process choosing the deck — a genuine two-party deal.
///
/// SAFETY: <see cref="Receive"/> is BOUNDED by a timeout and is cancellable. A stalled or unreachable peer
/// can therefore never block the deal thread forever (which would otherwise leave a hand stuck "in progress").
/// On timeout/cancel it throws so the hand unwinds cleanly and the host clears its active-deal handle.
/// </summary>
public sealed class TxDealChannel : LiveDeal.IDealChannel
{
    private readonly BlockingCollection<string> _inbox = new();
    private readonly Action<string> _send;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>How long any single <see cref="Receive"/> waits for the peer before giving up. This is an ONLINE
    /// game and a hand may legitimately take a long time, so the window is generous (default 10 MINUTES per
    /// protocol message). The automated deal messages are fast when a peer is actually present; this large window
    /// only ever matters when a peer is genuinely gone. Because EVERY arriving message starts a fresh Receive,
    /// a hand that keeps making progress never times out no matter how long the whole game runs.</summary>
    public int ReceiveTimeoutMs { get; init; } = 600_000;

    public byte[] PeerPub { get; }

    public TxDealChannel(byte[] peerPub, Action<string> send) { PeerPub = peerPub; _send = send; }

    /// <summary>Called by the inbound router when a deal message from THIS peer arrives (already decrypted).</summary>
    public void Deliver(string message) { try { _inbox.Add(message); } catch { } }

    public void Send(string msg) => _send(msg);

    /// <summary>Wait for the next peer message, but never forever: throws on timeout or cancel so the hand aborts
    /// cleanly instead of wedging the table. (BlockingCollection.Take throws OperationCanceled on cancel.)</summary>
    public string Receive()
    {
        if (_inbox.TryTake(out var item, ReceiveTimeoutMs, _cts.Token)) return item;
        throw new TimeoutException("the on-chain opponent is offline (no response for several minutes) — the table hand is unaffected.");
    }

    /// <summary>Abort any in-flight deal: unblocks <see cref="Receive"/> immediately.</summary>
    public void Cancel() { try { _cts.Cancel(); } catch { } }
}
