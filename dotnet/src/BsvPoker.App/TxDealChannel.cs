using System.Collections.Concurrent;
using BsvPoker.Net;

namespace BsvPoker.App;

/// <summary>
/// A <see cref="LiveDeal.IDealChannel"/> that carries the mental-poker deal between two real players where
/// every message is a Bitcoin transaction: <see cref="Send"/> hands the plaintext to the owner's send action
/// (which encrypts it to the peer, funds a ChatDirect transaction, pushes it IP-to-IP to the peer AND
/// broadcasts it to the miners); <see cref="Receive"/> blocks for the next message the peer pushed to us.
/// No shared RNG, no single process choosing the deck — a genuine two-party deal.
/// </summary>
public sealed class TxDealChannel : LiveDeal.IDealChannel
{
    private readonly BlockingCollection<string> _inbox = new();
    private readonly Action<string> _send;

    public byte[] PeerPub { get; }

    public TxDealChannel(byte[] peerPub, Action<string> send) { PeerPub = peerPub; _send = send; }

    /// <summary>Called by the inbound router when a deal message from THIS peer arrives (already decrypted).</summary>
    public void Deliver(string message) => _inbox.Add(message);

    public void Send(string msg) => _send(msg);
    public string Receive() => _inbox.Take();
}
