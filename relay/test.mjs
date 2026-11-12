// Relay behaviour tests.
//   npx wrangler dev --port 8787      (in another terminal)
//   node test.mjs ws://127.0.0.1:8787
// or against the live relay:  node test.mjs wss://ourgames-rooms.<account>.workers.dev
const base = process.argv[2] || "ws://127.0.0.1:8787";
const room = String(1000 + Math.floor(Math.random() * 9000));
let failed = 0;
// the live relay is further away than a local one: give messages more time
const slow = base.startsWith("wss") ? 5 : 1;
const wait = (ms) => new Promise((r) => setTimeout(r, ms * slow));
const check = (ok, what) => {
  console.log((ok ? "PASS " : "FAIL ") + what);
  if (!ok) failed += 1;
};

function connect(role, key) {
  const ws = new WebSocket(`${base}/room/${room}?role=${role}&key=${key}`);
  ws.got = [];
  ws.closeCode = null;
  ws.onmessage = (e) => ws.got.push(String(e.data));
  ws.onclose = (e) => (ws.closeCode = e.code);
  return new Promise((resolve) => {
    ws.onopen = () => resolve(ws);
    ws.onerror = () => resolve(ws);
  });
}
const has = (ws, text) => ws.got.includes(text);

const lonely = await connect("guest", "nobody");
await wait(400);
check(lonely.closeCode === 4004, "guest without a host gets 4004 (not found)");

const host = await connect("host", "hostkey");
await wait(300);
check(has(host, "W|0|"), "host gets slot 0");

const g1 = await connect("guest", "k1");
await wait(300);
check(has(g1, "W|1|0"), "first guest gets slot 1 and sees the host");
check(has(host, "J|1"), "host is told slot 1 joined");

const g2 = await connect("guest", "k2");
const g3 = await connect("guest", "k3");
await wait(400);
check(has(g2, "W|2|0,1"), "second guest gets slot 2");
check(g3.got.some((m) => m.startsWith("W|3|")), "third guest gets slot 3");

const g4 = await connect("guest", "k4");
await wait(400);
check(g4.closeCode === 4003, "fifth player is refused (room full)");

const intruder = await connect("host", "otherhost");
await wait(400);
check(intruder.closeCode === 4001, "a second host is refused (code taken)");

host.send("hello");
await wait(300);
check(has(g1, "0|hello") && has(g2, "0|hello") && has(g3, "0|hello"), "host message reaches all guests with its slot");
check(!has(host, "0|hello"), "sender does not get its own message");

g2.send("@0|secret");
await wait(300);
check(has(host, "2|secret") && !has(g1, "2|secret"), "direct message reaches only its target");

host.send("p");
await wait(200);
check(!g1.got.includes("0|p"), "keep-alive is swallowed");

// reconnect with the same key: same slot, nobody is told about a leave
const g1b = await connect("guest", "k1");
await wait(400);
check(has(g1b, "W|1|0,2,3"), "reconnecting guest gets its old slot back");
check(!has(host, "L|1"), "a reconnect is not announced as leaving");

g3.close();
await wait(500);
check(has(host, "L|3") && has(g2, "L|3"), "others are told when somebody leaves");

const g5 = await connect("guest", "k5");
await wait(400);
check(g5.got.some((m) => m.startsWith("W|3|")), "a new player takes the free slot");

for (const ws of [host, g1b, g2, g5]) ws.close();
await wait(300);
console.log(failed ? `${failed} FAILED` : "all relay tests passed");
process.exit(failed ? 1 : 0);
