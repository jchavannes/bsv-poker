# BSV Poker — web version (in‑browser, local P2P)

The web client runs the **same protocol as the desktop app**, compiled to **WebAssembly** (Blazor). The crypto
(`BsvPoker.Crypto`), the mental‑poker / blackjack / pot core (`BsvPoker.Core`), and the game engines
(`BsvPoker.Net` — `NetBlackjack` / `NetGame`) are the *identical* C# code; only the transport differs.

The game engines were decoupled from the TCP `P2PNode` onto the existing `IGameTransport` interface, so the same
engine runs over the desktop mesh **or** a browser transport with no change. In the browser the first transport is
**`BroadcastChannelTransport`** — server‑less local P2P that connects every open tab on the origin (no backend, no
signalling). Cross‑machine play will add a WebRTC transport (same interface).

## Run it locally
```
cd dotnet/src/BsvPoker.Web
dotnet run
```
Then open **http://localhost:5099** in your browser. To play group blackjack locally, **open the page in a second
tab** and click **Join the table** in both — the deal starts the instant two seats are filled (the on‑chain pot is
funded in the background and never blocks the cards, exactly as on desktop). Add more tabs for 3–6 seats.

## Notes
- It's a standalone project (not in `BsvPoker.sln`) so it never disturbs the desktop build/CI; build it with
  `dotnet build dotnet/src/BsvPoker.Web`.
- This first cut runs **bankroll** group blackjack over local cross‑tab P2P to prove the protocol in the browser.
  Wiring a browser BSV wallet (so the real on‑chain n‑of‑n pot funds/settles from the web, miner‑verified) and a
  WebRTC transport for cross‑machine play are the next steps, followed by the paid **dealer node**.
