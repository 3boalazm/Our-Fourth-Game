// Browser side of the online link (C# side: Assets/Scripts/Game/Net.cs).
//
// Everybody in a room opens a WebSocket to the room relay (see /relay):
//   <relay>/room/<4 digit code>?role=host|guest&key=<random>
// The relay forwards every text message to all others as "<slot>|<message>" and
// sends its own control messages "W|slot|others", "J|slot", "L|slot".
// C# polls this plugin every frame; no callbacks into WASM are needed.
//
// Status codes (mirrored in Net.cs):
// 0 idle, 1 connecting, 2 in the room, 6 error, 7 code taken, 8 not found / full, 9 reconnecting

var NetBridgePlugin = {

    $nb: {
        ws: null,
        queue: [],
        status: 0,
        error: "",
        role: "",
        code: "",
        key: "",
        slot: -1,
        everOpen: false,
        tries: 0,
        closedByUs: false,
        retryTimer: null,
        pingTimer: null,
        openTimer: null,
        watching: false,
        hiddenAt: 0,

        base: function () {
            var o = "";
            try {
                // local testing only: ?relay=ws://127.0.0.1:8787
                var q = new URLSearchParams(window.location.search).get("relay");
                if (q && /^ws:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(q)) o = q;
            } catch (e) {}
            if (!o) o = window.OG_RELAY || "";
            if (!o) o = (window.location.protocol === "https:" ? "wss://" : "ws://") + window.location.host;
            return String(o).replace(/\/+$/, "");
        },

        str: function (s) {
            var len = lengthBytesUTF8(s) + 1;
            var buf = _malloc(len);
            stringToUTF8(s, buf, len);
            return buf;
        },

        stopTimers: function () {
            if (nb.retryTimer) { clearTimeout(nb.retryTimer); nb.retryTimer = null; }
            if (nb.openTimer) { clearTimeout(nb.openTimer); nb.openTimer = null; }
            if (nb.pingTimer) { clearInterval(nb.pingTimer); nb.pingTimer = null; }
        },

        connect: function () {
            var url = nb.base() + "/room/" + nb.code + "?role=" + nb.role + "&key=" + nb.key;
            var ws;
            try { ws = new WebSocket(url); } catch (e) { nb.error = "socket"; nb.scheduleRetry(); return; }
            nb.ws = ws;

            // give up on a socket that never opens and try again
            nb.openTimer = setTimeout(function () {
                if (nb.ws === ws && ws.readyState !== 1) { try { ws.close(); } catch (e) {} }
            }, 8000);

            ws.onopen = function () {
                if (nb.ws !== ws) return;
                if (nb.openTimer) { clearTimeout(nb.openTimer); nb.openTimer = null; }
                if (nb.pingTimer) clearInterval(nb.pingTimer);
                nb.pingTimer = setInterval(function () {
                    if (nb.ws === ws && ws.readyState === 1) { try { ws.send("p"); } catch (e) {} }
                }, 20000);
            };

            ws.onmessage = function (e) {
                if (nb.ws !== ws) return;
                var m = String(e.data);
                if (m.charAt(0) === "W" && m.charAt(1) === "|") {
                    nb.slot = parseInt(m.split("|")[1], 10);
                    nb.status = 2;
                    nb.everOpen = true;
                    nb.tries = 0;
                    nb.error = "";
                }
                nb.queue.push(m);
                if (nb.queue.length > 2000) nb.queue.splice(0, nb.queue.length - 2000);
            };

            ws.onerror = function () { if (nb.ws === ws) nb.error = "network"; };

            ws.onclose = function (ev) {
                if (nb.ws !== ws) return;
                nb.ws = null;
                if (nb.openTimer) { clearTimeout(nb.openTimer); nb.openTimer = null; }
                if (nb.pingTimer) { clearInterval(nb.pingTimer); nb.pingTimer = null; }
                if (nb.closedByUs) return;
                var c = ev.code;
                if (c === 4001) { nb.error = "taken"; nb.status = 7; return; }
                if (c === 4003) { nb.error = "full"; nb.status = 8; return; }
                if (c === 4004 && !nb.everOpen) { nb.error = "notfound"; nb.status = 8; return; }
                if (c === 4002) return; // replaced by our own newer connection
                // lost the connection (page in the background, bad signal, host away...): keep the seat and retry
                if (nb.everOpen) nb.status = 9;
                nb.scheduleRetry();
            };
        },

        scheduleRetry: function (immediate) {
            if (nb.closedByUs || nb.retryTimer || !nb.role) return;
            if (!nb.everOpen && nb.tries >= 4) { if (!nb.error) nb.error = "network"; nb.status = 6; return; }
            var wait = immediate ? 50 : Math.min(8000, 1000 * Math.pow(2, Math.min(nb.tries, 3)));
            nb.retryTimer = setTimeout(function () {
                nb.retryTimer = null;
                if (nb.closedByUs || !nb.role) return;
                if (nb.ws && nb.ws.readyState <= 1) return; // already (re)connecting or open
                nb.tries++;
                nb.connect();
            }, wait);
        },

        // Throw the socket away and open a fresh one (a socket that sat in the
        // background may look open but be dead). The relay gives us our seat back.
        reconnectNow: function () {
            if (!nb.role || nb.closedByUs) return;
            if (nb.retryTimer) { clearTimeout(nb.retryTimer); nb.retryTimer = null; }
            nb.stopTimers();
            var old = nb.ws;
            nb.ws = null;
            try { if (old) old.close(); } catch (e) {}
            nb.tries = 0;
            nb.connect();
        },

        watchVisibility: function () {
            if (nb.watching) return;
            nb.watching = true;
            document.addEventListener("visibilitychange", function () {
                if (document.hidden) { nb.hiddenAt = Date.now(); return; }
                if (!nb.role || nb.closedByUs || nb.status === 6) return;
                if (Date.now() - nb.hiddenAt > 3000 || !nb.ws || nb.ws.readyState !== 1) nb.reconnectNow();
            });
            window.addEventListener("online", function () { if (nb.role && !nb.closedByUs) nb.reconnectNow(); });
            window.addEventListener("pageshow", function () {
                if (nb.role && !nb.closedByUs && (!nb.ws || nb.ws.readyState !== 1)) nb.reconnectNow();
            });
        },

        shutdown: function () {
            nb.closedByUs = true;
            nb.stopTimers();
            try { if (nb.ws) nb.ws.close(1000); } catch (e) {}
            nb.ws = null;
            nb.role = "";
            nb.code = "";
            nb.queue = [];
            nb.status = 0;
            nb.error = "";
            nb.slot = -1;
            nb.everOpen = false;
            nb.tries = 0;
        }
    },

    NB_Start: function (isHost, codePtr) {
        var code = UTF8ToString(codePtr);
        nb.shutdown();
        nb.closedByUs = false;
        nb.role = isHost ? "host" : "guest";
        nb.code = code;
        nb.key = Math.random().toString(36).slice(2, 12) + Math.random().toString(36).slice(2, 12);
        nb.status = 1;
        nb.watchVisibility();
        nb.connect();
    },

    NB_Status: function () { return nb.status; },

    NB_Error: function () { return nb.str(nb.error || ""); },

    NB_Slot: function () { return nb.slot; },

    NB_Send: function (msgPtr) {
        if (nb.ws && nb.ws.readyState === 1) {
            try { nb.ws.send(UTF8ToString(msgPtr)); } catch (e) {}
        }
    },

    // Next received message, or an empty string when there is none.
    NB_Poll: function () {
        return nb.str(nb.queue.length ? nb.queue.shift() : "");
    },

    NB_Close: function () { nb.shutdown(); },

    NB_UrlParam: function (namePtr) {
        var s = "";
        try { s = new URLSearchParams(window.location.search).get(UTF8ToString(namePtr)) || ""; } catch (e) {}
        return nb.str(s);
    },

    NB_PageUrl: function () {
        return nb.str(window.location.origin + window.location.pathname);
    },

    // The phone's share sheet (WhatsApp...) on top of the game.
    NB_Share: function (textPtr, urlPtr) {
        var text = UTF8ToString(textPtr);
        var url = UTF8ToString(urlPtr);
        try {
            if (navigator.share) {
                navigator.share({ title: "Color Clash", text: text, url: url }).catch(function () {});
                return 1;
            }
        } catch (e) {}
        return 0;
    },

    NB_CopyText: function (textPtr) {
        var text = UTF8ToString(textPtr);
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text);
                return 1;
            }
        } catch (e) {}
        return 0;
    },

    NB_Vibrate: function (ms) {
        try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) {}
    },

    // The name typed last time, kept by the page (localStorage is shared with the web page).
    NB_GetStored: function (keyPtr) {
        var v = "";
        try { v = localStorage.getItem("ocg_" + UTF8ToString(keyPtr)) || ""; } catch (e) {}
        return nb.str(v);
    },

    NB_SetStored: function (keyPtr, valuePtr) {
        try { localStorage.setItem("ocg_" + UTF8ToString(keyPtr), UTF8ToString(valuePtr)); } catch (e) {}
    },

    // A small HTML text box over the game for typing your name (works with every phone keyboard).
    NB_AskName: function (currentPtr) {
        var current = UTF8ToString(currentPtr);
        var answer = null;
        try { answer = window.prompt("Your name (English letters, up to 12)", current); } catch (e) {}
        if (answer === null) answer = current;
        answer = String(answer).replace(/[|@\n\r]/g, "").trim().slice(0, 12);
        nb.queue.push("N|" + answer);
    }
};

autoAddDeps(NetBridgePlugin, "$nb");
mergeInto(LibraryManager.library, NetBridgePlugin);
