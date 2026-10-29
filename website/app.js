// Fills the download buttons and release notes from GitHub's API for Mica's release repo.
// The repo name ends in a dot ("mica."), so the API is reached by numeric id like AppUpdater.cs does.
(function () {
  var REPO_URL = "https://github.com/RumiDaNeko/mica.";
  var API = "https://api.github.com/repositories/1432309505/releases";
  var CACHE_KEY = "mica-releases-v3";
  var CACHE_MS = 10 * 60 * 1000; // unauthenticated API allows 60 requests/hour per IP

  function $(sel) { return document.querySelector(sel); }
  function $all(sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); }

  function formatSize(bytes) {
    return bytes >= 1e9 ? (bytes / 1e9).toFixed(1) + " GB" : Math.round(bytes / 1e6) + " MB";
  }
  function formatDate(iso) {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  }
  function versionOf(release) { return release.tag_name.replace(/^v/i, ""); }
  // Release files by platform. Mica-<v>-Setup.exe brings its own engine; Mica-<v>-Setup-Lite.exe uses Windows'
  // WebView2; Mica-<v>-Update.exe is only for the built-in updater, so it is never offered here. A platform
  // without a file in the latest release shows as coming soon.
  var ASSETS = {
    full: /-Setup\.exe$/i,
    lite: /-Setup-Lite\.exe$/i,
    "mac-arm": /-(arm64|aarch64)\.dmg$/i,
    "mac-intel": /-(x64|x86_64|intel)\.dmg$/i,
    linux: /\.AppImage$/i,
    android: /\.apk$/i
  };
  function pick(assets, pattern) {
    var a = (assets || []).filter(function (x) { return pattern.test(x.name); })[0];
    return a ? { name: a.name, size: a.size, url: a.browser_download_url } : null;
  }
  function files(assets) {
    var out = {};
    Object.keys(ASSETS).forEach(function (k) { out[k] = pick(assets, ASSETS[k]); });
    return out;
  }

  function readCache() {
    try {
      var c = JSON.parse(sessionStorage.getItem(CACHE_KEY));
      if (c && Date.now() - c.at < CACHE_MS) return c.data;
    } catch (e) {}
    return null;
  }
  function writeCache(data) {
    try { sessionStorage.setItem(CACHE_KEY, JSON.stringify({ at: Date.now(), data: data })); } catch (e) {}
  }

  function load() {
    var cached = readCache();
    if (cached) return Promise.resolve(cached);
    return fetch(API + "?per_page=6", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) {
        if (!r.ok) throw new Error("GitHub responded " + r.status);
        return r.json();
      })
      .then(function (list) {
        var data = list.filter(function (r) { return !r.draft; }).map(function (r) {
          return {
            tag_name: r.tag_name, name: r.name, html_url: r.html_url, body: r.body || "",
            published_at: r.published_at, prerelease: r.prerelease,
            files: files(r.assets)
          };
        });
        writeCache(data);
        return data;
      });
  }

  // Which device is this? Chromium tells us through User-Agent Client Hints (including a Mac's chip and
  // the Windows version); other browsers only through the user agent. Unsure means no guess.
  function platform() {
    var ua = navigator.userAgent, uad = navigator.userAgentData;
    var touchMac = /Macintosh/.test(ua) && navigator.maxTouchPoints > 1;
    var base = {
      os: /iPhone|iPad|iPod/.test(ua) || touchMac ? "ios" : /Android/.test(ua) ? "android" : /Windows NT/.test(ua) ? "windows" : /Macintosh/.test(ua) ? "mac" : /Linux|X11|CrOS/.test(ua) ? "linux" : "",
      win11: false, arm: null
    };
    if (!uad || !uad.getHighEntropyValues) return Promise.resolve(base);
    return uad.getHighEntropyValues(["platformVersion", "architecture"]).then(function (v) {
      var map = { Windows: "windows", macOS: "mac", Linux: "linux", "Chrome OS": "linux", Android: "android" };
      if (map[uad.platform]) base.os = map[uad.platform];
      base.win11 = base.os === "windows" && parseInt(v.platformVersion, 10) >= 13;
      if (v.architecture) base.arm = v.architecture === "arm";
      return base;
    }).catch(function () { return base; });
  }

  var NAMES = { windows: "Windows", mac: "Mac", linux: "Linux", android: "Android", ios: "iPhone" };
  var CARD = { windows: "windows", mac: "mac", linux: "linux", android: "mobile", ios: "mobile" };

  // The file this device should get: Lite on Windows 11 (it always has WebView2), else Full, which always
  // works; on a Mac, the chip it reports, else Apple silicon (Safari can't tell, and most Macs now are).
  function bestFor(f, where) {
    if (where.os === "windows") return where.win11 && f.lite ? f.lite : f.full || f.lite;
    if (where.os === "mac") return where.arm === false ? f["mac-intel"] || f["mac-arm"] : f["mac-arm"] || f["mac-intel"];
    if (where.os === "linux") return f.linux;
    if (where.os === "android") return f.android;
    return null;
  }

  function renderDownloads(latest, where) {
    var v = versionOf(latest), f = latest.files || {};
    var chosen = bestFor(f, where), name = NAMES[where.os];

    var primary = $("[data-primary]"), label = $("[data-primary-label]"), meta = $("[data-meta]");
    if (chosen) {
      primary.href = chosen.url;
      label.textContent = "Download for " + name;
      var what = chosen === f.lite ? "Lite, for Windows 11" : chosen === f.full ? "Windows 10 and 11" :
        chosen === f["mac-arm"] ? "Apple silicon" : chosen === f["mac-intel"] ? "Intel Mac" : name;
      meta.textContent = what + " · " + formatSize(chosen.size) + " · Version " + v;
    } else {
      primary.href = "#download";
      label.textContent = name ? "Mica for " + name : "Download Mica";
      meta.textContent = name && where.os !== "windows" ? "Mica for " + name + " is coming soon. Version " + v + " is out for other platforms." : "Free · Version " + v;
    }

    var out = [];
    if (f.full || f.lite) out.push("Windows");
    if (f["mac-arm"] || f["mac-intel"]) out.push("Mac");
    if (f.linux) out.push("Linux");
    if (f.android) out.push("Android");
    $("[data-version-line]").textContent = "Version " + v + ", released " + formatDate(latest.published_at) + "." + (out.length ? " Free for " + out.join(", ").replace(/, ([^,]*)$/, " and $1") + "." : "");
    $all("[data-release-link]").forEach(function (a) { a.href = latest.html_url; });

    var ownCard = $('[data-platform="' + CARD[where.os] + '"]');
    $all("[data-asset]").forEach(function (link) {
      var asset = f[link.dataset.asset], size = link.querySelector("[data-size]");
      if (!asset) {
        link.classList.add("soon");
        link.removeAttribute("href");
        link.setAttribute("aria-disabled", "true");
        size.textContent = "Coming soon";
        return;
      }
      link.href = asset.url;
      size.textContent = size.textContent + " · " + formatSize(asset.size);
      link.classList.toggle("quiet", !!chosen && asset !== chosen && link.closest("[data-platform]") === ownCard);
    });
    $all("[data-platform]").forEach(function (card) {
      var mine = card === ownCard;
      card.classList.toggle("recommended", mine);
      card.querySelector(".badge").hidden = !mine;
    });
    // The device's own platform comes first.
    if (ownCard && ownCard.parentNode.firstElementChild !== ownCard) ownCard.parentNode.insertBefore(ownCard, ownCard.parentNode.firstElementChild);
  }

  function renderReleases(list) {
    var ol = $("[data-releases]");
    ol.textContent = "";
    list.slice(0, 5).forEach(function (r, i) {
      var li = document.createElement("li");
      var head = document.createElement("div");
      head.className = "head";
      var h = document.createElement("h3");
      var a = document.createElement("a");
      a.href = r.html_url;
      a.textContent = r.name || ("Mica " + versionOf(r));
      h.appendChild(a);
      head.appendChild(h);
      if (i === 0 && !r.prerelease) head.appendChild(badge("Latest"));
      if (r.prerelease) head.appendChild(badge("Pre-release"));
      var t = document.createElement("time");
      t.dateTime = r.published_at;
      t.textContent = formatDate(r.published_at);
      head.appendChild(t);
      li.appendChild(head);
      var notes = document.createElement("div");
      notes.className = "notes";
      // Plain text on purpose: release bodies are Markdown we don't render, and never HTML we trust.
      var text = r.body.replace(/\r/g, "").replace(/^#+\s*/gm, "").replace(/\*\*/g, "").trim();
      // Release bodies often open with the release name again; the card already shows it.
      var first = text.split("\n")[0].trim();
      if (r.name && first.indexOf(r.name) === 0) text = text.slice(text.indexOf("\n") + 1).trim();
      notes.textContent = text;
      li.appendChild(notes);
      ol.appendChild(li);
      if (notes.textContent.split("\n").length > 6) {
        notes.classList.add("clamped");
        var more = document.createElement("button");
        more.className = "more";
        more.textContent = "Show all notes";
        more.addEventListener("click", function () {
          var closed = notes.classList.toggle("clamped");
          more.textContent = closed ? "Show all notes" : "Show less";
        });
        li.appendChild(more);
      }
    });
  }
  function badge(text) {
    var s = document.createElement("span");
    s.className = "badge-inline";
    s.textContent = text;
    return s;
  }

  Promise.all([load(), platform()]).then(function (results) {
    var list = results[0];
    var latest = list.filter(function (r) { return !r.prerelease; })[0] || list[0];
    if (!latest) throw new Error("No releases yet");
    renderDownloads(latest, results[1]);
    renderReleases(list);
  }).catch(function () {
    var meta = $("[data-meta]");
    meta.textContent = "Couldn't reach GitHub. ";
    var a = document.createElement("a");
    a.className = "link";
    a.href = REPO_URL + "/releases/latest";
    a.textContent = "Get the latest release on GitHub";
    meta.appendChild(a);
    $("[data-releases]").innerHTML = '<li class="muted">Release notes are unavailable right now.</li>';
  });

  // On a Mac, Spotlight is Command+K.
  if (/Macintosh|Mac OS X/.test(navigator.userAgent)) $all("[data-mod]").forEach(function (k) { k.textContent = "⌘"; });

  // The island: rows come and go while it's on screen, like the real one. With reduced motion every row stays.
  (function island() {
    var card = $("[data-island]");
    if (!card) return;
    var rows = {};
    $all("[data-row]").forEach(function (r) { rows[r.dataset.row] = r; });
    function show(name, on) { rows[name].classList.toggle("on", on); }
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) { Object.keys(rows).forEach(function (k) { show(k, true); }); return; }
    var bar = $("[data-dl-bar]"), note = $("[data-dl-note]"), level = $("[data-zoom-level]");
    function setDownload(pct, text) { bar.style.width = pct + "%"; note.textContent = pct < 100 ? pct + "% · " + text : text; }
    var steps = [
      function () { setDownload(12, "1.8 MB/s"); show("download", true); },
      function () { setDownload(45, "2.3 MB/s"); },
      function () { setDownload(78, "3.1 MB/s"); },
      function () { setDownload(100, "Open"); },
      function () { level.textContent = "110%"; show("zoom", true); },
      function () { level.textContent = "125%"; },
      function () { show("download", false); },
      function () { show("zoom", false); show("media", true); },
      function () { card.classList.add("away"); },
      function () { card.classList.remove("away"); },
      function () { show("media", false); },
      function () {}
    ];
    var i = 0, timer = null;
    function tick() { steps[i](); i = (i + 1) % steps.length; }
    function start() { if (!timer) { tick(); timer = setInterval(tick, 1500); } }
    function stop() { if (timer) { clearInterval(timer); timer = null; } }
    if (!("IntersectionObserver" in window)) { start(); return; }
    new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting) start(); else stop(); });
    }, { threshold: 0.4 }).observe(card.parentNode);
  })();

  // Scroll scenes, Apple-style: pinned stages whose pictures move with the scroll position. Each pinned
  // scene gets p = 0..1 for how far its section has scrolled past. With reduced motion, or without JS,
  // every scene is a still picture instead.
  (function scenes() {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    var darkPage = window.matchMedia("(prefers-color-scheme: dark)");
    function clamp(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    function span(p, a, b) { return clamp((p - a) / (b - a)); }      // 0..1 while p runs from a to b
    function ease(t) { return t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }
    function lerp(a, b, t) { return a + (b - a) * t; }
    function mix(a, b, t) { return "rgb(" + [0, 1, 2].map(function (i) { return Math.round(lerp(a[i], b[i], t)); }).join(",") + ")"; }
    function pinned(section) { var r = section.getBoundingClientRect(); return clamp(-r.top / (r.height - window.innerHeight)); }
    function fadeUp(el, t) { el.style.opacity = t; el.style.transform = "translateY(" + lerp(16, 0, t) + "px)"; }
    var list = [];

    // 1. Hero: the laptop starts nearly closed and its lid opens over the first part of the scroll.
    var lid = $(".hero .lid"), heroDisplay = $(".hero .display");
    if (lid) list.push({ render: function () {
      var t = ease(span(window.scrollY, 0, window.innerHeight * .4));
      lid.style.transform = t < 1 ? "rotateX(" + lerp(-72, 0, t) + "deg)" : "none";
      heroDisplay.style.filter = t < 1 ? "brightness(" + lerp(.25, 1, t) + ")" : "none";
    } });

    // 2. Close-up: a beam of light comes down, the new tab fades in zoomed into its clock (blurred, then
    //    pulled into focus), and the camera pulls back to the whole laptop.
    var closeup = $(".closeup");
    if (closeup) (function () {
      closeup.classList.add("animated");
      var device = closeup.querySelector(".closeup-device"), img = device.querySelector("img");
      var head = closeup.querySelector(".closeup-head"), caption = closeup.querySelector(".closeup-caption");
      var line = closeup.querySelector(".beam-line"), cone = closeup.querySelector(".beam-cone"), spot = closeup.querySelector(".beam-spot");
      var fx = parseFloat(device.dataset.focusX), fy = parseFloat(device.dataset.focusY);
      var W = 0, H = 0, focus = { x: 0, y: 0 }, zoom = 3;
      list.push({
        measure: function () {
          var keep = device.style.transform;
          device.style.transform = "none";
          var d = device.getBoundingClientRect(), i = img.getBoundingClientRect();
          W = d.width; H = d.height;
          focus = { x: i.left - d.left + fx * i.width, y: i.top - d.top + fy * i.height };
          // Zoom until the clock (about 16% of the picture's width) fills roughly half the screen.
          zoom = Math.max(2.5, Math.min(5, (0.5 * window.innerWidth) / (0.16 * i.width)));
          device.style.transform = keep;
        },
        render: function () {
          var p = pinned(closeup);
          var beamIn = span(p, 0, .12), beamOut = span(p, .22, .32);
          line.style.transform = "scaleY(" + beamIn + ")";
          line.style.opacity = 1 - beamOut;
          cone.style.opacity = spot.style.opacity = span(p, .05, .14) * (1 - beamOut);
          var t = ease(span(p, .32, .82));
          var s = lerp(zoom, 1, t), cx = lerp(focus.x, W / 2, t), cy = lerp(focus.y, H / 2, t);
          device.style.transform = "translate(" + (-cx * s) + "px," + (-cy * s) + "px) scale(" + s + ")";
          device.style.opacity = span(p, .14, .26);
          var light = span(p, .14, .36);
          device.style.filter = light < 1 ? "brightness(" + lerp(.35, 1, light) + ") blur(" + lerp(8, 0, light) + "px)" : "none";
          var headIn = span(p, .16, .24), headOut = span(p, .40, .48);
          fadeUp(head, headIn); head.style.opacity = headIn * (1 - headOut);
          fadeUp(caption, span(p, .82, .92));
        }
      });
    })();

    // 3. Statement: each word lights up as the paragraph moves up the window.
    var statement = $("[data-words]");
    if (statement) (function () {
      var words = statement.textContent.trim().split(/\s+/);
      statement.textContent = "";
      var spans = words.map(function (w, i) {
        var s = document.createElement("span"); s.className = "w"; s.textContent = w;
        statement.appendChild(s); if (i < words.length - 1) statement.appendChild(document.createTextNode(" "));
        return s;
      });
      list.push({ render: function () {
        var r = statement.getBoundingClientRect(), vh = window.innerHeight;
        var lit = Math.round(span(r.top, vh * .8, vh * .4 - r.height) * spans.length);
        spans.forEach(function (s, i) { s.classList.toggle("lit", i < lit); });
      } });
    })();

    // 4. Spotlight: Ctrl and K rise and press down, Spotlight opens on the monitor, then the camera moves in.
    var keys = $(".keys");
    if (keys) (function () {
      keys.classList.add("animated");
      var caps = keys.querySelector(".keycaps"), kbds = Array.prototype.slice.call(caps.querySelectorAll("kbd"));
      var monitor = keys.querySelector(".keys-monitor"), after = keys.querySelector(".layer-after");
      var head = keys.querySelector(".keys-head"), caption = keys.querySelector(".keys-caption");
      monitor.style.transformOrigin = "50% 36%";
      list.push({ render: function () {
        var p = pinned(keys);
        var rise = ease(span(p, .06, .18)), press = span(p, .24, .30), gone = span(p, .36, .44);
        caps.style.opacity = rise * (1 - gone);
        caps.style.transform = "translate(-50%," + lerp(-10, -50, rise) + "%) scale(" + lerp(1, .9, gone) + ")";
        kbds.forEach(function (k) {
          k.style.transform = "translateY(" + lerp(0, 5, press) + "px)";
          k.style.boxShadow = press > .5 ? "inset 0 1px 0 rgba(255,255,255,.4), 0 1px 0 rgba(0,0,0,.35), 0 4px 10px -4px rgba(0,0,0,.4)" : "";
        });
        after.style.opacity = span(p, .28, .38);
        var settle = ease(span(p, 0, .2)), zoomIn = ease(span(p, .46, .80));
        monitor.style.transform = "translate(-50%," + lerp(-50, -66, zoomIn) + "%) scale(" + (lerp(.9, 1, settle) * lerp(1, 1.18, zoomIn)) + ")";
        monitor.style.filter = after.style.opacity < 1 ? "brightness(" + lerp(.8, 1, +after.style.opacity) + ")" : "none";
        head.style.opacity = 1 - span(p, .46, .56);
        fadeUp(caption, span(p, .80, .90));
      } });
    })();

    // 5. Themes: a bright edge sweeps across, the dark theme follows it, and (on a light page) the page darkens too.
    var themes = $(".themes");
    if (themes) (function () {
      themes.classList.add("animated");
      var laptop = themes.querySelector(".themes-laptop"), after = themes.querySelector(".layer-after"), sweep = themes.querySelector(".sweep");
      var caption = themes.querySelector(".themes-caption"), head = themes.querySelector(".themes-head"), eyebrow = head.querySelector(".eyebrow");
      list.push({ render: function () {
        var p = pinned(themes);
        var wipe = ease(span(p, .22, .70)), edge = lerp(100, 0, wipe);
        after.style.clipPath = "inset(0 0 0 " + edge + "%)";
        sweep.style.left = edge + "%";
        sweep.style.opacity = span(p, .18, .24) * (1 - span(p, .70, .76));
        laptop.style.transform = "translate(-50%,-50%) scale(" + lerp(.92, 1, ease(span(p, 0, .2))) + ")";
        if (!darkPage.matches) {
          themes.style.backgroundColor = mix([251, 251, 253], [0, 0, 0], wipe);
          themes.style.color = mix([29, 29, 31], [245, 245, 247], wipe);
          caption.style.color = mix([110, 110, 115], [161, 161, 166], wipe);
          eyebrow.style.color = mix([107, 79, 143], [201, 179, 234], wipe);
        } else { themes.style.backgroundColor = themes.style.color = caption.style.color = eyebrow.style.color = ""; }
        head.style.opacity = 1;
        fadeUp(caption, span(p, .76, .86));
      } });
    })();

    var queued = false;
    function frame() { queued = false; list.forEach(function (s) { s.render(); }); }
    function queue() { if (!queued) { queued = true; window.requestAnimationFrame(frame); } }
    function measure() { list.forEach(function (s) { if (s.measure) s.measure(); }); frame(); }
    window.addEventListener("scroll", queue, { passive: true });
    window.addEventListener("resize", function () { window.requestAnimationFrame(measure); });
    window.addEventListener("load", measure);
    measure();
  })();

  // Fade sections in as they scroll into view.
  var targets = $all(".reveal, .reveal-scale");
  if (!("IntersectionObserver" in window)) { targets.forEach(function (el) { el.classList.add("in"); }); return; }
  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } });
  }, { rootMargin: "0px 0px -8% 0px", threshold: 0.12 });
  targets.forEach(function (el) { io.observe(el); });
})();
