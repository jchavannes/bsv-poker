// Local, server-less P2P transport for BSV Poker in the browser: a single BroadcastChannel that connects every
// open tab on this origin. Tabs message each other directly (the browser relays among same-origin contexts) — no
// signalling server, no backend. This is the LOCAL P2P path; cross-machine play uses WebRTC (a later transport).
// Messages are JSON strings {tableId, id, b64}; .NET handles dedup + delivery.
const channels = {};

export function open(name, dotnetRef) {
  if (channels[name]) { try { channels[name].close(); } catch (e) {} }
  const ch = new BroadcastChannel(name);
  ch.onmessage = (e) => { dotnetRef.invokeMethodAsync('OnMeshMessage', e.data); };
  channels[name] = ch;
}

export function post(name, msg) {
  const ch = channels[name];
  if (ch) ch.postMessage(msg);
}

export function close(name) {
  const ch = channels[name];
  if (ch) { try { ch.close(); } catch (e) {} delete channels[name]; }
}
