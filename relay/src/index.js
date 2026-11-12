// "Our Fourth Game" room relay: one Cloudflare Worker + one Durable Object per room.
//
// Up to 4 players per 4 digit room code:
//   slot 0      the host (role=host)
//   slots 1..3  guests  (role=guest), first free slot
//
//   wss://<worker>/room/<code>?role=host|guest&key=<random>
//
// Every text message a player sends is forwarded to everybody else in the room,
// prefixed with the sender's slot:  "<slot>|<message>".
// A message starting with "@<slot>|" is delivered to that one slot only.
//
// Control messages sent BY THE RELAY (start with a letter, never with a digit):
//   "W|<yourSlot>|<other slots, comma separated>"   welcome, you are in the room
//   "J|<slot>"                                     somebody joined (or came back)
//   "L|<slot>"                                     somebody left / lost connection
// Clients may send "p" as a keep-alive; it is swallowed.
//
// Close codes: 4001 room code already used by another host, 4003 room is full,
//              4004 room not found (no host), 4002 replaced by a newer connection of yours.
//
// Reconnecting: a client keeps its random `key`; a new connection with the same key gets
// the same slot back and replaces the old socket (e.g. a phone that was in the background),
// without the others being told that anybody left.

const MaxSlots = 4;
const MaxMessage = 16000;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/health" || url.pathname === "/") {
      return new Response("Our Fourth Game relay is running.\n", {
        headers: { "content-type": "text/plain", "access-control-allow-origin": "*" },
      });
    }
    const m = url.pathname.match(/^\/room\/(\d{4})$/);
    if (!m) {
      return new Response("not found", { status: 404 });
    }
    if (request.headers.get("Upgrade") !== "websocket") {
      return new Response("Expected a WebSocket connection", { status: 426 });
    }
    const stub = env.ROOMS.get(env.ROOMS.idFromName("room-" + m[1]));
    return stub.fetch(request);
  },
};

export class Room {
  constructor(state) {
    this.state = state;
  }

  live(slot) {
    return this.state.getWebSockets("s" + slot);
  }

  slotOf(ws) {
    const tag = this.state.getTags(ws).find((t) => t[0] === "s");
    return tag ? Number(tag.slice(1)) : -1;
  }

  occupied() {
    const slots = [];
    for (let s = 0; s < MaxSlots; s += 1) {
      if (this.live(s).length > 0) slots.push(s);
    }
    return slots;
  }

  reject(server, client, code, reason) {
    server.accept();
    try {
      server.close(code, reason);
    } catch (e) {
      /* ignore */
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async fetch(request) {
    const url = new URL(request.url);
    const role = url.searchParams.get("role");
    const key = (url.searchParams.get("key") || "").slice(0, 64);
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    const storage = this.state.storage;

    let slot = -1;
    if (role === "host") {
      const hostKey = await storage.get("k0");
      if (this.live(0).length > 0 && hostKey !== key) {
        return this.reject(server, client, 4001, "taken");
      }
      slot = 0;
    } else if (role === "guest") {
      if (this.live(0).length === 0) {
        return this.reject(server, client, 4004, "notfound");
      }
      // Same key as before: take the same seat back (unless somebody else sits there now).
      const keys = (await storage.get("keys")) || {};
      const known = keys[key];
      if (known >= 1 && known < MaxSlots) {
        const owner = await storage.get("k" + known);
        if (this.live(known).length === 0 || owner === key) slot = known;
      }
      if (slot < 0) {
        for (let s = 1; s < MaxSlots; s += 1) {
          if (this.live(s).length === 0) {
            slot = s;
            break;
          }
        }
      }
      if (slot < 0) {
        return this.reject(server, client, 4003, "full");
      }
      keys[key] = slot;
      await storage.put("keys", keys);
    } else {
      return new Response("role must be host or guest", { status: 400 });
    }
    await storage.put("k" + slot, key);

    // A newer connection of the same player replaces the old one quietly.
    for (const old of this.live(slot)) {
      old.serializeAttachment({ replaced: true });
      try {
        old.close(4002, "replaced");
      } catch (e) {
        /* ignore */
      }
    }
    this.state.acceptWebSocket(server, ["s" + slot]);
    server.serializeAttachment({ replaced: false });

    const others = this.occupied().filter((s) => s !== slot);
    server.send("W|" + slot + "|" + others.join(","));
    for (const s of others) {
      for (const ws of this.live(s)) {
        try {
          ws.send("J|" + slot);
        } catch (e) {
          /* ignore */
        }
      }
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string") return; // text only
    if (message === "p") return; // keep-alive
    if (message.length > MaxMessage) return;
    const from = this.slotOf(ws);
    if (from < 0) return;

    if (message[0] === "@") {
      // direct message to one slot: "@<slot>|payload"
      const bar = message.indexOf("|");
      const to = Number(message.slice(1, bar));
      if (bar < 0 || !(to >= 0 && to < MaxSlots) || to === from) return;
      const out = from + "|" + message.slice(bar + 1);
      for (const o of this.live(to)) {
        try {
          o.send(out);
        } catch (e) {
          /* ignore */
        }
      }
      return;
    }

    const out = from + "|" + message;
    for (let s = 0; s < MaxSlots; s += 1) {
      if (s === from) continue;
      for (const o of this.live(s)) {
        try {
          o.send(out);
        } catch (e) {
          /* ignore */
        }
      }
    }
  }

  async peerGone(ws) {
    let att = null;
    try {
      att = ws.deserializeAttachment();
    } catch (e) {
      /* ignore */
    }
    if (att && att.replaced) return; // we closed it ourselves, nobody left
    const slot = this.slotOf(ws);
    if (slot < 0) return;
    if (this.live(slot).some((o) => o !== ws)) return; // already back on a new socket
    let anyoneLeft = false;
    for (let s = 0; s < MaxSlots; s += 1) {
      if (s === slot) continue;
      for (const o of this.live(s)) {
        anyoneLeft = true;
        try {
          o.send("L|" + slot);
        } catch (e) {
          /* ignore */
        }
      }
    }
    // Everybody is gone: forget the seats so the code is fresh next time.
    if (!anyoneLeft) {
      await this.state.storage.deleteAll();
    }
  }

  async webSocketClose(ws, code, reason) {
    await this.peerGone(ws);
    try {
      ws.close(code, reason);
    } catch (e) {
      /* ignore */
    }
  }

  async webSocketError(ws) {
    await this.peerGone(ws);
  }
}
