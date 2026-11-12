# Color Clash (Our Fourth Game)

A territory game for 2 to 4 players, made in Unity and played in the browser (phone or PC).
Everybody starts with a small square of land. Leave your land and you draw a tail; come back home and
everything inside your loop becomes yours, even other players' land. Run into someone's tail to cut it and
send them home; don't cross your own tail and don't hit the wall. When the clock runs out, the most land wins.

- **Online with friends anywhere (2-4 players):** one player creates a room and gets a 4 digit code and an invite
  link; up to three friends join. The host can fill empty seats with bots.
- **Solo against 1-3 bots.**
- **Controls:** swipe on the phone (or drag with the mouse), arrow keys / WASD on a keyboard.
- Installable as an app (PWA, fullscreen, solo works offline).

## How it is built

Everything is made from code, there are no image or sound files:

| Path | What it is |
|---|---|
| `Assets/Scripts/Core/Arena.cs` | the rules: grid, trails, loop capture (flood fill), crashes, respawns, bots, land map encoding. No Unity code, so it can be tested on its own |
| `Assets/Scripts/Game/OurFourthGame.cs` | screens, match flow, swipe/keyboard input, camera, online protocol |
| `Assets/Scripts/Game/ArenaView.cs` | drawing: land, characters with eyes, snake body, glow, particles, floating texts |
| `Assets/Shaders/Arena.shader` | draws the land from a 1-pixel-per-cell texture as rounded blobs with rims, animated tails, capture flash |
| `Assets/Scripts/Game/Sfx.cs` | sound effects and music loop, synthesised at start-up |
| `Assets/Scripts/Game/Net.cs` + `Assets/Plugins/WebGL/NetBridge.jslib` | the WebSocket link to the room relay |
| `Assets/Editor/FourthBuild.cs` | menu **Color Clash**: create scene, build WebGL, render preview pictures |
| `Assets/WebGLTemplates/OurFourthGame/` | the web page (loading screen, install button, service worker, icons) |
| `relay/` | the room relay: a Cloudflare Worker with one Durable Object per room, up to 4 players |

### Online protocol

Each device drives its own player (the host also drives the bots) and sends every cell it enters
(`M|match|player|seq|x|y|dir`), crashes (`D`) and respawns (`R`). Everybody applies the same rules to those
moves. The host sends the land map every 1.5 s (`G`, run-length encoded) so small differences between devices
are corrected, re-sends the start (`S`) until every guest confirmed it (`A`), and sends the final map (`E`).

## Tests

```bash
powershell -File tools/test-core.ps1      # rules: 31 checks incl. 12 full bot matches (no Unity needed)
bash tools/compile-check.sh               # compiles all scripts against Unity's assemblies (no Unity needed)
cd relay && node test.mjs wss://ourgames-rooms.doha-mustafa.workers.dev   # relay behaviour
```

## Build and publish

Requires **Unity 6000.3.10f1** with WebGL Build Support and an activated license (Unity Hub).

```bash
Unity -batchmode -quit -projectPath . -executeMethod FourthBuild.BuildWebGL -logFile build.log
Unity -batchmode -quit -projectPath . -executeMethod FourthBuild.RenderPreview -logFile preview.log
```

The relay is deployed with `cd relay && npx wrangler deploy` (live at `wss://ourgames-rooms.doha-mustafa.workers.dev`,
the address is set in `Assets/WebGLTemplates/OurFourthGame/index.html`). The web build in `WebBuild/` is a
static site (Vercel).
