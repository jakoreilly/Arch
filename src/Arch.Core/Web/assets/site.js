/* ArchDiagram viewer — dependency-free except vendored mermaid.min.js.
   Pan/zoom + PNG/SVG export, lazy per-card rendering, hover tooltips, selector
   groups, theme-aware diagrams with live re-render, Ctrl+K search palette,
   and client-side filters for the structure tree and type listings. */
(function () {
  "use strict";

  function currentTheme() {
    return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
  }

  // Mermaid's own classDef/linkStyle grammar does not accept a CSS var() call as a colour
  // value (it parses the diagram source itself, before anything reaches the DOM) — so a
  // classDef built with var(--accent) fails to parse rather than rendering a wrong colour.
  // Diagram source text (both server-baked and client-built) is written with var(--token)
  // anyway, for the same "one token, both themes" reason every other colour in this sheet
  // is; this resolves each one to the currently active theme's literal value right before
  // mermaid ever sees the text. Called on every render, including theme-toggle re-renders,
  // so it always reflects the theme active at that moment.
  function resolveThemeVars(text) {
    var style = getComputedStyle(document.documentElement);
    return text.replace(/var\(--([a-z0-9-]+)\)/gi, function (m, name) {
      var val = style.getPropertyValue("--" + name).trim();
      return val || m;
    });
  }

  // Pages with no diagram at all don't load mermaid.min.js (a 3.3 MB bundle otherwise parsed
  // on every navigation for nothing) — see PageTemplate.Render's needsMermaid sniff. Every
  // mermaid touchpoint below must tolerate that absence instead of throwing, since a thrown
  // error here would abort this whole IIFE partway through and take the search palette, theme
  // toggle and every other unrelated feature down with it.
  var hasMermaid = typeof mermaid !== "undefined";

  function initMermaid() {
    if (!hasMermaid) { return; }
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "loose",
      theme: currentTheme() === "dark" ? "dark" : "neutral",
      maxTextSize: 200000,
      maxEdges: 100000,
      flowchart: { htmlLabels: false }
    });
  }
  initMermaid();

  var seq = 0;
  var tipEl = document.getElementById("hover-tip");

  // Rich hover card shared by diagram nodes and the metrics scatter. Pointer + keyboard.
  function bindTip(node, text) {
    if (!text || !tipEl) { return; }
    function show(e) {
      tipEl.textContent = text; tipEl.hidden = false;
      var px = (e && e.clientX), py = (e && e.clientY);
      if (px == null) { var r = node.getBoundingClientRect(); px = r.left + r.width / 2; py = r.top; }
      tipEl.style.left = Math.min(px + 14, window.innerWidth - tipEl.offsetWidth - 8) + "px";
      tipEl.style.top = Math.min(py + 14, window.innerHeight - tipEl.offsetHeight - 8) + "px";
    }
    // aria-describedby is set only while the tip is up. The tip is a single shared element
    // reused by every node, so a permanent association would describe every node with
    // whatever text was shown last; pointing at it on show and releasing on hide keeps the
    // description truthful. Keyboard users already got the tip visually via the focus
    // handler below — this is what makes it reach a screen reader too.
    function hide() { tipEl.hidden = true; node.removeAttribute("aria-describedby"); }
    function showAndDescribe(e) { show(e); node.setAttribute("aria-describedby", "hover-tip"); }
    node.addEventListener("mousemove", show);
    node.addEventListener("focus", showAndDescribe);
    node.addEventListener("mouseleave", hide);
    node.addEventListener("blur", hide);
  }

  function renderCard(card) {
    if (card.dataset.rendered) { return; }
    card.dataset.rendered = "1";
    var src = card.querySelector(".mermaid-src");
    var target = card.querySelector(".render-target");
    if (!src || !target) { return; }

    mermaid.render("mmd-" + (++seq), resolveThemeVars(src.textContent)).then(function (out) {
      target.innerHTML = out.svg;
      growViewBoxToContent(target.querySelector("svg"));
      setupCard(card);
    }).catch(function (err) {
      target.innerHTML = "<div class='diagram-error'>Diagram failed to render: " +
        String(err && err.message || err).replace(/</g, "&lt;") + "</div>";
    });
  }

  // Mermaid sometimes sizes a flowchart's viewBox from node/edge positions alone, and an edge
  // label or a wide shape (the database cylinder, a long site name) that overhangs the
  // rightmost/bottommost rank ends up outside it — invisible, since SVG hard-crops to the
  // viewBox. getBBox() measures what was actually drawn, so grow (never shrink, in case a
  // rendering quirk makes it return something smaller) the viewBox to contain it.
  function growViewBoxToContent(svg) {
    if (!svg) { return; }
    var vb = (svg.getAttribute("viewBox") || "").split(/[\s,]+/).map(Number);
    if (vb.length !== 4 || !isFinite(vb[0]) || !isFinite(vb[1]) || vb[2] <= 0 || vb[3] <= 0) { return; }
    var box;
    try { box = svg.getBBox(); } catch (e) { return; }
    if (!box || !isFinite(box.width) || !isFinite(box.height) || box.width <= 0 || box.height <= 0) { return; }
    var pad = 4;
    var minX = Math.min(vb[0], box.x - pad), minY = Math.min(vb[1], box.y - pad);
    var maxX = Math.max(vb[0] + vb[2], box.x + box.width + pad);
    var maxY = Math.max(vb[1] + vb[3], box.y + box.height + pad);
    if (maxX - minX !== vb[2] || maxY - minY !== vb[3] || minX !== vb[0] || minY !== vb[1]) {
      svg.setAttribute("viewBox", minX + " " + minY + " " + (maxX - minX) + " " + (maxY - minY));
    }
  }

  function hydrateToolbar(card) {
    var slot = card.querySelector(".toolbar[data-toolbar-lazy]");
    if (!slot) { return; }
    var tpl = document.getElementById("diagram-toolbar-tpl");
    if (!tpl) { return; }
    slot.removeAttribute("data-toolbar-lazy");
    slot.appendChild(tpl.content.cloneNode(true));
  }

  function setupCard(card) {
    hydrateToolbar(card);
    var stage = card.querySelector(".stage");
    var svg = stage.querySelector("svg");
    if (!svg) { return; }

    // Re-renders (theme toggle) must not stack stage/window listeners.
    if (card._ac) { card._ac.abort(); }
    var ac = new AbortController();
    card._ac = ac;
    var on = function (el, ev, fn, opts) {
      var o = opts || {};
      o.signal = ac.signal;
      el.addEventListener(ev, fn, o);
    };

    svg.removeAttribute("width");
    svg.removeAttribute("height");
    svg.style.width = "auto";
    svg.style.height = "auto";

    var scale = 1, tx = 0, ty = 0;
    function apply() { svg.style.transform = "translate(" + tx + "px," + ty + "px) scale(" + scale + ")"; }
    function zoomAt(cx, cy, factor) {
      var next = Math.min(8, Math.max(0.1, scale * factor));
      tx = cx - (cx - tx) * (next / scale);
      ty = cy - (cy - ty) * (next / scale);
      scale = next;
      apply();
    }
    function fit() {
      var stageRect = stage.getBoundingClientRect();
      var size = svgSize();
      if (!size.w || !size.h) { return; }
      var pad = 24;
      var svgRect = svg.getBoundingClientRect();
      var natW = svgRect.width / scale, natH = svgRect.height / scale;
      if (!natW || !natH) { natW = size.w; natH = size.h; }
      // Floored at 0.3: with no lower bound, a very dense diagram (a star-shaped call graph, a
      // huge dependency fan) auto-fit itself down to an illegible smudge just to make every node
      // visible at once. Below this floor the labels stop being readable regardless, so it's
      // better to show a legible slice and let the reader pan/zoom the rest — which they can,
      // since the stage still supports drag-to-pan past this point.
      scale = Math.max(0.3, Math.min((stageRect.width - pad) / natW, (stageRect.height - pad) / natH, 4));
      tx = (stageRect.width - natW * scale) / 2;
      ty = (stageRect.height - natH * scale) / 2;
      apply();
    }

    // "Find node…" jump target: reuses the existing anchor-preserving zoom, then a pure
    // screen-space pan (via getBoundingClientRect, so it's correct regardless of the
    // SVG's internal coordinate system) to bring the node to the stage's centre.
    function centerOn(node) {
      var stageRect = stage.getBoundingClientRect();
      var r = node.getBoundingClientRect();
      if (scale < 1.5) {
        zoomAt(r.left + r.width / 2 - stageRect.left, r.top + r.height / 2 - stageRect.top, 1.5 / scale);
        r = node.getBoundingClientRect();
      }
      tx += (stageRect.left + stageRect.width / 2) - (r.left + r.width / 2);
      ty += (stageRect.top + stageRect.height / 2) - (r.top + r.height / 2);
      apply();
    }

    card.querySelector("[data-act='zoom-in']").onclick = function () {
      var r = stage.getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 1.2);
    };
    card.querySelector("[data-act='zoom-out']").onclick = function () {
      var r = stage.getBoundingClientRect(); zoomAt(r.width / 2, r.height / 2, 1 / 1.2);
    };
    card.querySelector("[data-act='zoom-reset']").onclick = function () { scale = 1; tx = 0; ty = 0; apply(); };
    var fitBtn = card.querySelector("[data-act='fit']");
    if (fitBtn) { fitBtn.onclick = fit; }
    var pngBtn = card.querySelector("[data-act='png']");
    if (pngBtn) { pngBtn.onclick = function () { guard(pngBtn, downloadPng); }; }
    var svgBtn = card.querySelector("[data-act='svg']");
    if (svgBtn) { svgBtn.onclick = function () { guard(svgBtn, function (release) { downloadSvg(); release(); }); }; }
    var copyBtn = card.querySelector("[data-act='copy']");
    if (copyBtn) {
      copyBtn.onclick = function () {
        var src = card.querySelector(".mermaid-src");
        if (!src) { return; }
        var done = function () {
          var old = copyBtn.textContent;
          copyBtn.textContent = "✓ Copied";
          setTimeout(function () { copyBtn.textContent = old; }, 1500);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(src.textContent).then(done).catch(function () { fallbackCopy(src.textContent); done(); });
        } else { fallbackCopy(src.textContent); done(); }
      };
    }

    on(stage, "wheel", function (e) {
      e.preventDefault();
      var r = stage.getBoundingClientRect();
      zoomAt(e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.1 : 1 / 1.1);
    }, { passive: false });

    var dragging = false, lastX = 0, lastY = 0;
    on(stage, "mousedown", function (e) {
      dragging = true; lastX = e.clientX; lastY = e.clientY; stage.classList.add("panning");
    });
    on(stage, "dblclick", function () { scale = 1; tx = 0; ty = 0; apply(); });
    on(window, "mousemove", function (e) {
      if (!dragging) { return; }
      tx += e.clientX - lastX; ty += e.clientY - lastY;
      lastX = e.clientX; lastY = e.clientY;
      apply();
    });
    on(window, "mouseup", function () { dragging = false; stage.classList.remove("panning"); });

    attachTooltips(card, svg, centerOn);

    // Auto-fit once the SVG has laid out so the whole diagram is visible on load
    // without clicking Fit. Double rAF lets mermaid's SVG size settle before measuring.
    // Only ever runs for a rendered (visible) card, so the stage has real dimensions.
    requestAnimationFrame(function () { requestAnimationFrame(fit); });

    function fallbackCopy(text) {
      var ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); } catch (e) { }
      document.body.removeChild(ta);
    }

    function serializeSvg() {
      var clone = svg.cloneNode(true);
      clone.style.transform = "";
      clone.removeAttribute("style");
      if (!clone.getAttribute("xmlns")) { clone.setAttribute("xmlns", "http://www.w3.org/2000/svg"); }
      return new XMLSerializer().serializeToString(clone);
    }

    function svgSize() {
      var vb = (svg.getAttribute("viewBox") || "").split(/[\s,]+/).map(Number);
      if (vb.length === 4 && vb[2] > 0 && vb[3] > 0) { return { w: vb[2], h: vb[3] }; }
      var box = svg.getBBox();
      return { w: box.width, h: box.height };
    }

    function downloadSvg() {
      var blob = new Blob([serializeSvg()], { type: "image/svg+xml;charset=utf-8" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = (card.dataset.pngName || "archdiagram") + ".svg";
      a.click();
      URL.revokeObjectURL(a.href);
    }

    // Export is asynchronous (image decode, then canvas.toBlob), and nothing stopped a second
    // click landing mid-flight and starting a duplicate download. Disable for the round trip
    // and always re-enable, including on the error path.
    function guard(btn, work) {
      if (!btn || btn.disabled) { return; }
      btn.disabled = true;
      var done = function () { btn.disabled = false; };
      try { work(done); } catch (e) { done(); }
    }

    function downloadPng(release) {
      var size = svgSize();
      // 2x for crisp raster, clamped so huge diagrams stay under canvas limits.
      var s = Math.min(2, 8192 / Math.max(size.w, size.h));
      var canvas = document.createElement("canvas");
      canvas.width = Math.ceil(size.w * s);
      canvas.height = Math.ceil(size.h * s);
      var ctx = canvas.getContext("2d");
      ctx.fillStyle = getComputedStyle(stage).backgroundColor || "#ffffff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      var url = URL.createObjectURL(new Blob([serializeSvg()], { type: "image/svg+xml;charset=utf-8" }));
      var img = new Image();
      img.onload = function () {
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        URL.revokeObjectURL(url);
        canvas.toBlob(function (blob) {
          var a = document.createElement("a");
          a.href = URL.createObjectURL(blob);
          a.download = (card.dataset.pngName || "archdiagram") + ".png";
          a.click();
          URL.revokeObjectURL(a.href);
          release();
        }, "image/png");
      };
      img.onerror = function () { URL.revokeObjectURL(url); release(); };
      img.src = url;
    }
  }

  function attachTooltips(card, svg, centerOn) {
    var mapEl = card.querySelector("script.tooltips");
    var map = {};
    if (mapEl) { try { map = JSON.parse(mapEl.textContent); } catch (e) { map = {}; } }
    var hrefEl = card.querySelector("script.hrefs");
    var hrefs = {};
    if (hrefEl) { try { hrefs = JSON.parse(hrefEl.textContent); } catch (e) { hrefs = {}; } }
    var adjEl = card.querySelector("script.adjacency");
    var adjacency = {};
    if (adjEl) { try { adjacency = JSON.parse(adjEl.textContent); } catch (e) { adjacency = {}; } }

    var nodeByAlias = {};
    svg.querySelectorAll("g.node").forEach(function (node) {
      // Mermaid node DOM ids embed our alias, e.g. "flowchart-n001-12".
      // Aliases are zero-padded to >=3 digits but grow past n999 on large diagrams,
      // so match 3-or-more digits (n\d{3,}) — not exactly 3 — or links break at scale.
      var m = /(?:^|-)(n\d{3,})(?:-|$)/.exec(node.id || "");
      var alias = m && m[1];
      if (!alias) { return; }
      nodeByAlias[alias] = node;
      var text = map[alias];
      var url = hrefs[alias];

      bindTip(node, text);

      if (url) {
        node.classList.add("clickable-node");
        // Mermaid draws a plain <g>, not an <a> — with no href, a keyboard user could not
        // reach it at all (the focus/blur handlers bindTip wires up could never fire either),
        // and a click always navigated in-place even with Ctrl/Cmd or the middle button held.
        node.setAttribute("tabindex", "0");
        node.setAttribute("role", "link");
        var openNode = function (newTab) {
          if (newTab) { window.open(url, "_blank", "noopener"); } else { window.location.href = url; }
        };
        node.addEventListener("click", function (e) { openNode(e.ctrlKey || e.metaKey); });
        // Middle-click fires "auxclick", not "click", for a non-anchor element.
        node.addEventListener("auxclick", function (e) { if (e.button === 1) { e.preventDefault(); openNode(true); } });
        node.addEventListener("keydown", function (e) {
          if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openNode(e.ctrlKey || e.metaKey); }
        });
      } else if (text) {
        node.style.cursor = "pointer";
      }
    });

    setupFlowHighlight(card, svg, nodeByAlias, adjacency);
    setupNodeFind(card, nodeByAlias, centerOn);
  }

  /* ---- Hover flow-path highlight: light a node's neighbours, dim the rest ----
     Edge dimming is best-effort: mermaid's rendered edge element ids are version-
     specific ("L_n001_n002_0" in the vendored build here — verified against the
     source below). If that pattern ever matches nothing (a mermaid upgrade changed
     it), highlighting silently degrades to nodes-only, which still delivers the
     core value and never breaks node click-to-open. */
  function setupFlowHighlight(card, svg, nodeByAlias, adjacency) {
    var aliases = Object.keys(nodeByAlias);
    if (aliases.length === 0) { return; }
    var edgeEls = svg.querySelectorAll(".edgePaths path, .edgePath path, path.flowchart-link");

    function edgeTouches(el, alias) {
      var id = el.id || el.getAttribute("class") || "";
      return id.indexOf("_" + alias + "_") >= 0 || id.indexOf("-" + alias + "-") >= 0
          || id.indexOf("_" + alias) === id.length - alias.length - 1
          || id.indexOf(alias + "_") === 0 || id.indexOf(alias + "-") === 0;
    }

    function highlight(alias) {
      var keep = {}; keep[alias] = true;
      (adjacency[alias] || []).forEach(function (n) { keep[n] = true; });
      svg.classList.add("path-focus");
      aliases.forEach(function (a) {
        nodeByAlias[a].classList.toggle("path-dim", !keep[a]);
      });
      edgeEls.forEach(function (el) { el.classList.toggle("path-dim", !edgeTouches(el, alias)); });
    }
    function clear() {
      svg.classList.remove("path-focus");
      svg.querySelectorAll(".path-dim").forEach(function (el) { el.classList.remove("path-dim"); });
    }

    aliases.forEach(function (a) {
      var node = nodeByAlias[a];
      node.addEventListener("mouseenter", function () { highlight(a); });
      node.addEventListener("mouseleave", clear);
      node.addEventListener("focus", function () { highlight(a); });
      node.addEventListener("blur", clear);
    });
  }

  /* ---- "Find node…" toolbar box: centers + pulses the matching node ---- */
  function setupNodeFind(card, nodeByAlias, centerOn) {
    var input = card.querySelector("[data-act='find']");
    if (!input) { return; }

    function labelOf(node) {
      var t = node.querySelector("text, .nodeLabel, span");
      return (t ? t.textContent : node.textContent || "").trim();
    }

    function findAndGo() {
      var q = input.value.trim().toLowerCase();
      if (!q) { return; }
      var aliases = Object.keys(nodeByAlias);
      for (var i = 0; i < aliases.length; i++) {
        var node = nodeByAlias[aliases[i]];
        if (labelOf(node).toLowerCase().indexOf(q) >= 0) {
          centerOn(node);
          node.classList.add("path-pulse");
          setTimeout(function () { node.classList.remove("path-pulse"); }, 1200);
          return;
        }
      }
    }
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); findAndGo(); } });
  }

  // Selector groups: <select data-diagram-select="group"> shows one card per group. The
  // selection is reflected into the URL hash (one "group=value" pair per group, so a page with
  // more than one selector still round-trips correctly) — without this, "look at the call graph
  // for OrderService" could not be sent to a colleague or bookmarked. replaceState rather than
  // pushState, matching the recenter pattern object.html/impact.html already use: one shareable
  // URL per view, not a new history entry per selection.
  function hashParams() {
    var params = {};
    (location.hash || "").replace(/^#/, "").split("&").forEach(function (pair) {
      var eq = pair.indexOf("=");
      if (eq < 0) { return; }
      var k = decodeURIComponent(pair.slice(0, eq));
      if (k) { params[k] = decodeURIComponent(pair.slice(eq + 1)); }
    });
    return params;
  }
  function setHashParam(key, value) {
    var params = hashParams();
    params[key] = value;
    var next = Object.keys(params).map(function (k) {
      return encodeURIComponent(k) + "=" + encodeURIComponent(params[k]);
    }).join("&");
    history.replaceState(null, "", (next ? "#" + next : location.pathname + location.search));
  }

  document.querySelectorAll("select[data-diagram-select]").forEach(function (sel) {
    var group = sel.getAttribute("data-diagram-select");

    // Restore a selection carried in the URL (a shared or bookmarked link), if it names a real
    // option — a stale/foreign hash value just leaves the select at its normal default.
    var fromHash = hashParams()[group];
    if (fromHash && Array.prototype.some.call(sel.options, function (o) { return o.value === fromHash; })) {
      sel.value = fromHash;
    }

    function update() {
      document.querySelectorAll(".diagram-card[data-group='" + group + "']").forEach(function (card) {
        var show = card.id === sel.value;
        card.hidden = !show;
        if (show) { renderCard(card); }
      });
    }
    sel.addEventListener("change", function () { setHashParam(group, sel.value); update(); });
    update();
  });

  // Metrics scatter: give every dot the same rich hover card as diagram nodes,
  // plus keyboard focus (the dots carry tabindex + data-tip from the server).
  document.querySelectorAll(".metrics-scatter [data-tip]").forEach(function (el) {
    bindTip(el, el.getAttribute("data-tip"));
  });

  // Metrics: offline zone calculator (no network). Mirrors ArchitectureMetrics.Classify.
  (function () {
    var box = document.getElementById("zone-calc");
    if (!box) { return; }
    var ca = box.querySelector("#calc-ca"), ce = box.querySelector("#calc-ce");
    var abs = box.querySelector("#calc-abs"), total = box.querySelector("#calc-total");
    var outI = box.querySelector("#calc-i"), outA = box.querySelector("#calc-a"), outD = box.querySelector("#calc-d");
    var verdict = box.querySelector("#calc-verdict");
    function num(el) { var v = parseInt(el.value, 10); return isNaN(v) || v < 0 ? 0 : v; }
    function cls(el, k) { el.className = "badge" + (k ? " " + k : ""); }
    function zoneVerdict(I, A, D, Ca, Ce, Ab, Tot) {
      if ((Ca + Ce) === 0) {
        return "Isolated module — no dependencies in or out. Instability is undefined; treated as 0.";
      }
      if (I <= 0.3 && A <= 0.3 && Ca > 0) {
        return "<strong>Zone of pain</strong> — rigid and heavily depended-on. Add abstractions "
             + "(interfaces/abstract base types) so dependents rely on contracts, or raise Ce toward "
             + Ca + " to increase instability.";
      }
      if (I >= 0.7 && A >= 0.7) {
        return "<strong>Zone of uselessness</strong> — abstract but barely used. Delete unused "
             + "abstractions or give this module concrete work.";
      }
      if (D <= 0.3) { return "<strong>Healthy</strong> — close to the main sequence."; }
      if (A <= 0.3 && I <= 0.3 && Ca === 0) {
        return "Concrete leaf with no dependents — the high distance is a formula artifact, not a real problem.";
      }
      return "<strong>Watch</strong> — D=" + D.toFixed(2) + " is off the main sequence; "
           + "nudge abstractness or coupling toward A + I = 1.";
    }
    function recompute() {
      var Ca = num(ca), Ce = num(ce), Ab = num(abs), Tot = num(total);
      var I = (Ca + Ce) === 0 ? 0 : Ce / (Ca + Ce);
      var A = Tot === 0 ? 0 : Math.min(1, Ab / Tot);
      var D = Math.abs(A + I - 1);
      outI.textContent = "I " + I.toFixed(2);
      outA.textContent = "A " + A.toFixed(2);
      outD.textContent = "D " + D.toFixed(2);
      cls(outD, D <= 0.3 ? "ok" : D <= 0.6 ? "" : "warn");
      verdict.innerHTML = zoneVerdict(I, A, D, Ca, Ce, Ab, Tot);
    }
    ["input", "change"].forEach(function (ev) {
      [ca, ce, abs, total].forEach(function (el) { el.addEventListener(ev, recompute); });
    });
    recompute();
  })();

  /* ---- Live regions for the counts and summaries pages update in place ----
     These elements are emitted by a dozen different page generators; marking them here
     keeps the rule in one place and means a new page gets it for free by reusing the
     class/id. "polite" so a running filter never interrupts what the user is typing. */
  [".filter-count", "#dep-summary", "#lf-summary", "#query-count", "#obj-hops-val"].forEach(function (sel) {
    document.querySelectorAll(sel).forEach(function (el) {
      if (!el.hasAttribute("role")) { el.setAttribute("role", "status"); }
    });
  });

  // Render all initially-visible cards. Cards marked data-deferred are rendered
  // by a page-specific controller (e.g. the landscape layer filters) instead.
  document.querySelectorAll(".diagram-card:not([hidden]):not([data-deferred])").forEach(renderCard);

  // Public hook so page-specific controllers can swap a card's Mermaid source and
  // force a re-render through the same path (used by the landscape layer filters).
  window.ArchViewer = {
    rerenderCard: function (card) {
      if (!card) { return; }
      if (card._ac) { card._ac.abort(); }
      delete card.dataset.rendered;
      var target = card.querySelector(".render-target");
      if (target) { target.innerHTML = ""; }
      renderCard(card);
    }
  };

  // Theme toggle: swap theme, re-init mermaid, and re-render every already-rendered card.
  var toggle = document.getElementById("theme-toggle");
  if (toggle) {
    // The label used to read "◐ Theme" forever, so neither a sighted user nor a screen
    // reader could tell which theme was active — unlike the tests toggle right beside it,
    // which has always named its state. #theme-status is the polite live region that
    // announces the change (a label rewrite on its own is not announced).
    var themeStatus = document.getElementById("theme-status");
    function syncTheme(announceIt) {
      var now = currentTheme();
      toggle.textContent = "◐ Theme: " + now;
      toggle.title = "Switch to the " + (now === "dark" ? "light" : "dark") + " theme";
      if (themeStatus && announceIt) { themeStatus.textContent = now === "dark" ? "Dark theme" : "Light theme"; }
    }
    syncTheme(false);
    toggle.onclick = function () {
      var cur = currentTheme() === "dark" ? "light" : "dark";
      document.documentElement.setAttribute("data-theme", cur);
      try { localStorage.setItem("archdiagram-theme", cur); } catch (e) { }
      syncTheme(true);
      initMermaid();
      document.querySelectorAll(".diagram-card[data-rendered]").forEach(function (card) {
        delete card.dataset.rendered;
        if (!card.hidden) { renderCard(card); }
      });
    };
  }

  /* ---- Test-file visibility toggle ----
     Tests are hidden by default (root class .hide-tests applied pre-paint in the template).
     This button flips it, persists the choice, prunes now-empty structure-tree folders, and
     keeps its own label in sync. Presentation only — nothing is removed from the model/search. */
  (function () {
    var btn = document.getElementById("tests-toggle");
    if (!btn) { return; }
    var root = document.documentElement;
    var tree = document.getElementById("structure-tree");

    function pruneTests() {
      if (!tree) { return; }
      var hiding = root.classList.contains("hide-tests");
      // Deepest-first so a parent sees its children's already-computed hidden state.
      var all = Array.prototype.slice.call(tree.querySelectorAll("details")).reverse();
      all.forEach(function (d) {
        if (!hiding) { d.hidden = false; return; }
        var visibleFile = d.querySelector(":scope > ul > li[data-path]:not([data-test])");
        var visibleChild = d.querySelector(":scope > details:not([hidden])");
        d.hidden = !visibleFile && !visibleChild;
      });
    }

    function pruneSections() {
      // Hide a Types-page namespace section when all its type cards are test files.
      var hiding = root.classList.contains("hide-tests");
      document.querySelectorAll("section.ns-group").forEach(function (sec) {
        if (!hiding) { sec.hidden = false; return; }
        sec.hidden = !sec.querySelector(".type-card:not([data-test])");
      });
    }

    function sync() {
      var hidden = root.classList.contains("hide-tests");
      btn.textContent = "🧪 Tests: " + (hidden ? "hidden" : "shown");
      pruneTests();
      pruneSections();
    }

    // Keep the 3D graph's own "Hide test files" checkbox in step with the global toggle
    // (the graph is WebGL, not CSS, so the .hide-tests class can't reach it).
    function syncGraph(hidden) {
      var g3d = document.getElementById("g3d-hide-tests");
      if (g3d && g3d.checked !== hidden) {
        g3d.checked = hidden;
        g3d.dispatchEvent(new Event("change"));
      }
    }

    btn.onclick = function () {
      var hidden = root.classList.toggle("hide-tests");
      try { localStorage.setItem("archdiagram-show-tests", hidden ? "0" : "1"); } catch (e) { }
      sync();
      syncGraph(hidden);
    };
    sync();
  })();

  /* ---- Ctrl+K search palette ---- */
  (function () {
    var overlay = document.getElementById("palette");
    var input = document.getElementById("palette-input");
    var list = document.getElementById("palette-results");
    var openBtn = document.getElementById("search-open");
    if (!overlay || !input || !list) { return; }
    var index = window.ARCH_SEARCH_INDEX || [];
    var relRoot = overlay.getAttribute("data-rel-root") || "";
    var selected = 0, hits = [];
    // Whatever had focus when the palette opened, so Esc can hand it back. Without this,
    // closing left focus on a now-hidden input and the browser dropped it to <body> —
    // a keyboard user lost their place in the page every time they dismissed a search.
    var lastFocus = null;

    function open() {
      lastFocus = document.activeElement;
      overlay.hidden = false;
      input.value = "";
      search("");
      input.focus();
    }
    function close() {
      overlay.hidden = true;
      if (lastFocus && lastFocus.focus) { lastFocus.focus(); }
      lastFocus = null;
    }

    function score(name, detail, q) {
      var n = name.toLowerCase(), d = (detail || "").toLowerCase();
      var i = n.indexOf(q);
      if (i === 0) { return 100; }
      if (i > 0) { return n.length - i > 0 ? 60 - Math.min(40, i) : 0; }
      if (d.indexOf(q) >= 0) { return 10; }
      // All query chars in order (subsequence match).
      var pos = -1;
      for (var c = 0; c < q.length; c++) {
        pos = n.indexOf(q[c], pos + 1);
        if (pos < 0) { return 0; }
      }
      return 5;
    }

    function search(q) {
      q = q.trim().toLowerCase();
      hits = [];
      if (q.length === 0) {
        for (var i = 0; i < index.length && hits.length < 12; i++) {
          if (index[i][0] === "file") { hits.push(index[i]); }
        }
      } else {
        var scored = [];
        for (var j = 0; j < index.length; j++) {
          var s = score(index[j][1], index[j][2], q);
          if (s > 0) { scored.push([s, index[j]]); }
        }
        scored.sort(function (a, b) { return b[0] - a[0]; });
        hits = scored.slice(0, 20).map(function (x) { return x[1]; });
      }
      selected = 0;
      renderList();
    }

    function renderList() {
      list.innerHTML = "";
      if (hits.length === 0) {
        var li = document.createElement("li");
        li.className = "palette-empty";
        li.textContent = "No matches";
        list.appendChild(li);
        input.removeAttribute("aria-activedescendant");
        announce("No matches");
        return;
      }
      hits.forEach(function (h, i) {
        var li = document.createElement("li");
        li.id = "palette-opt-" + i;
        li.setAttribute("role", "option");
        li.setAttribute("aria-selected", i === selected ? "true" : "false");
        if (i === selected) { li.className = "selected"; }
        var kind = document.createElement("span");
        kind.className = "palette-kind";
        kind.textContent = h[0];
        var name = document.createElement("span");
        name.className = "palette-name";
        name.textContent = h[1];
        var detail = document.createElement("span");
        detail.className = "palette-detail";
        detail.textContent = h[2] || "";
        li.appendChild(kind); li.appendChild(name); li.appendChild(detail);
        li.addEventListener("click", function () { go(h); });
        li.addEventListener("mousemove", function () {
          if (selected !== i) { selected = i; renderList(); }
        });
        list.appendChild(li);
      });
      var sel = list.querySelector(".selected");
      if (sel) { sel.scrollIntoView({ block: "nearest" }); }
      // Points the combobox at the highlighted option so a screen reader reads the row the
      // arrow keys landed on. Focus itself never leaves the input — that is the pattern.
      input.setAttribute("aria-activedescendant", "palette-opt-" + selected);
      announce(hits.length + " result" + (hits.length === 1 ? "" : "s"));
    }

    // Result counts are obvious on screen and invisible to a screen reader. Debounced via the
    // natural throttle of typing; the region is polite so it never interrupts the keystroke.
    var liveEl = null;
    function announce(text) {
      if (!liveEl) {
        liveEl = document.createElement("div");
        liveEl.className = "sr-only";
        liveEl.setAttribute("role", "status");
        overlay.appendChild(liveEl);
      }
      liveEl.textContent = text;
    }

    function go(h) { window.location.href = relRoot + h[3]; }

    input.addEventListener("input", function () { search(input.value); });
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); selected = Math.min(hits.length - 1, selected + 1); renderList(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); selected = Math.max(0, selected - 1); renderList(); }
      else if (e.key === "Enter" && hits[selected]) { go(hits[selected]); }
      else if (e.key === "Escape") { close(); }
      // Focus trap. The palette holds exactly one focusable element, so "trapping" is just
      // refusing to let Tab leave it — otherwise Tab walked into the page behind an open
      // modal, which is the classic aria-modal violation.
      else if (e.key === "Tab") { e.preventDefault(); }
    });
    overlay.addEventListener("mousedown", function (e) { if (e.target === overlay) { close(); } });
    if (openBtn) { openBtn.onclick = open; }
    window.addEventListener("keydown", function (e) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); overlay.hidden ? open() : close(); }
      else if (e.key === "/" && overlay.hidden && !/^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName)) { e.preventDefault(); open(); }
      else if (e.key === "Escape" && !overlay.hidden) { close(); }
    });
  })();

  /* ---- Generic card filter: <input class="filter-input" data-filter-target="sel"> ---- */
  document.querySelectorAll(".filter-input[data-filter-target]").forEach(function (input) {
    var groupSel = input.getAttribute("data-filter-target");
    var countEl = input.parentElement.querySelector(".filter-count");
    input.addEventListener("input", function () {
      var q = input.value.trim().toLowerCase();
      var visible = 0, total = 0;
      document.querySelectorAll(groupSel).forEach(function (group) {
        var any = false;
        group.querySelectorAll(".filterable").forEach(function (card) {
          total++;
          var show = q.length === 0 || (card.dataset.search || "").indexOf(q) >= 0;
          card.hidden = !show;
          if (show) { any = true; visible++; }
        });
        group.hidden = !any;
      });
      if (countEl) { countEl.textContent = q.length === 0 ? "" : visible + " of " + total + " shown"; }
    });
  });

  /* ---- Structure tree: filter + expand/collapse ---- */
  (function () {
    var tree = document.getElementById("structure-tree");
    if (!tree) { return; }
    var filter = document.getElementById("tree-filter");
    var expand = document.getElementById("tree-expand");
    var collapse = document.getElementById("tree-collapse");
    var countEl = document.querySelector(".select-row .filter-count");

    if (expand) { expand.onclick = function () { tree.querySelectorAll("details").forEach(function (d) { d.open = true; }); }; }
    if (collapse) { collapse.onclick = function () { tree.querySelectorAll("details").forEach(function (d) { d.open = false; }); }; }

    if (filter) {
      filter.addEventListener("input", function () {
        var q = filter.value.trim().toLowerCase();
        var visible = 0, total = 0;
        tree.querySelectorAll("li[data-path]").forEach(function (li) {
          total++;
          var show = q.length === 0 || li.dataset.path.indexOf(q) >= 0;
          li.hidden = !show;
          if (show) { visible++; }
        });
        // Hide folders with no visible files; open matches while filtering.
        function prune(details) {
          var any = false;
          details.querySelectorAll(":scope > details").forEach(function (child) {
            if (prune(child)) { any = true; }
          });
          details.querySelectorAll(":scope > ul > li[data-path]").forEach(function (li) {
            if (!li.hidden) { any = true; }
          });
          details.hidden = q.length > 0 && !any;
          if (q.length > 0 && any) { details.open = true; }
          return any;
        }
        tree.querySelectorAll(":scope > details").forEach(prune);
        if (countEl) { countEl.textContent = q.length === 0 ? "" : visible + " of " + total + " files"; }
      });
    }
  })();

  /* ---- Landscape layer filters ----
     Rebuilds the Mermaid source from the original embedded diagram so the
     service-call and shared-package layers toggle independently, low-volume call
     edges threshold out, and call edges weight by volume. Self-guards: on any page
     without #landscape-filters it returns immediately. */
  (function () {
    var card = document.getElementById("landscape");
    var bar = document.getElementById("landscape-filters");
    var src = card && card.querySelector(".mermaid-src");
    if (!card || !bar || !src || !window.ArchViewer) { return; }

    var header = [], siteNodes = [], pkgNodes = [], calls = [], pkgLinks = [], pkgEdges = [];
    src.textContent.split(/\r?\n/).forEach(function (raw) {
      var line = raw.trim();
      if (!line) { return; }
      if (/^flowchart/.test(line) || /^classDef/.test(line)) { header.push(line); return; }
      var call = /^(\S+)\s*-\.->\|"(\d+)\s*calls?"\|\s*(\S+)/.exec(line);
      if (call) { calls.push({ from: call[1], to: call[3], count: +call[2] }); return; }
      if (/^\S+\s*-->\|/.test(line)) { pkgLinks.push(line); return; }
      if (/^\S+\s*-\.->\s*\S+\s*$/.test(line)) { pkgEdges.push(line); return; }
      var node = /^(n\d{3,})\s*[\[{]/.exec(line);
      if (node) { (/:::external/.test(line) ? pkgNodes : siteNodes).push(line); }
    });

    var maxCount = calls.reduce(function (m, c) { return Math.max(m, c.count); }, 1);
    var thresholdEl = document.getElementById("lf-threshold");
    thresholdEl.max = Math.ceil(maxCount / 25) * 25;

    var cbCalls = document.getElementById("lf-calls");
    var cbPkgs = document.getElementById("lf-packages");
    var cbLinks = document.getElementById("lf-pkglinks");
    var valEl = document.getElementById("lf-threshold-val");
    var summaryEl = document.getElementById("lf-summary");

    function rebuild() {
      var thr = +thresholdEl.value;
      valEl.textContent = thr;
      var lines = header.slice().concat(siteNodes);
      if (cbPkgs.checked) { lines = lines.concat(pkgNodes); }
      var edgeIdx = -1, styles = [];
      if (cbLinks.checked) { pkgLinks.forEach(function (l) { lines.push(l); edgeIdx++; }); }
      if (cbPkgs.checked) { pkgEdges.forEach(function (l) { lines.push(l); edgeIdx++; }); }
      var shownCalls = 0;
      if (cbCalls.checked) {
        calls.forEach(function (c) {
          if (c.count < thr) { return; }
          lines.push(c.from + ' -.->|"' + c.count + ' calls"| ' + c.to);
          edgeIdx++; shownCalls++;
          var w = (1.5 + 4.5 * (c.count / maxCount)).toFixed(1);
          styles.push("linkStyle " + edgeIdx + " stroke-width:" + w + "px;");
        });
      }
      lines = lines.concat(styles);
      src.textContent = lines.join("\n");
      window.ArchViewer.rerenderCard(card);
      summaryEl.textContent =
        (cbCalls.checked ? shownCalls + " calls (≥" + thr + ")" : "calls hidden") +
        " · " + (cbLinks.checked ? pkgLinks.length + " package links" : "links hidden") +
        " · " + (cbPkgs.checked ? pkgNodes.length + " shared packages" : "packages hidden");
    }

    [cbCalls, cbPkgs, cbLinks].forEach(function (el) { el.addEventListener("change", rebuild); });
    thresholdEl.addEventListener("input", rebuild);
    bar.hidden = false;
    rebuild();
  })();

  /* ---- Modules page: min-import-weight filter ----
     Same rebuild-the-Mermaid-source-then-rerenderCard technique as the Landscape
     layer filters above, adapted to the Modules diagram's edge shape: a solid arrow
     ("-->", not Landscape's dashed "-.->"), and a label of "N imports" that is OMITTED
     entirely (not "1 imports") when N is exactly 1 — an unlabeled edge must count as
     weight 1, not be skipped. Self-guards: on any page without #mod-filters it
     returns immediately. */
  (function () {
    var card = document.getElementById("modules");
    var bar = document.getElementById("mod-filters");
    var src = card && card.querySelector(".mermaid-src");
    if (!card || !bar || !src || !window.ArchViewer) { return; }

    var header = [], nodes = [], edges = [];
    src.textContent.split(/\r?\n/).forEach(function (raw) {
      var line = raw.trim();
      if (!line) { return; }
      if (/^flowchart/.test(line) || /^classDef/.test(line)) { header.push(line); return; }
      var labeled = /^(\S+)\s*-->\|"(\d+)\s*imports?"\|\s*(\S+)/.exec(line);
      if (labeled) { edges.push({ from: labeled[1], to: labeled[3], count: +labeled[2], raw: line }); return; }
      var bare = /^(\S+)\s*-->\s*(\S+)\s*$/.exec(line);
      if (bare) { edges.push({ from: bare[1], to: bare[2], count: 1, raw: line }); return; }
      var node = /^(n\d{3,})\s*[\[({]/.exec(line);
      if (node) { nodes.push(line); }
    });

    var maxCount = edges.reduce(function (m, e) { return Math.max(m, e.count); }, 1);
    var thresholdEl = document.getElementById("mod-threshold");
    thresholdEl.max = Math.max(1, maxCount);

    var valEl = document.getElementById("mod-threshold-val");
    var summaryEl = document.getElementById("mod-summary");

    function rebuild() {
      var thr = +thresholdEl.value;
      valEl.textContent = thr;
      var lines = header.slice().concat(nodes);
      var shown = 0;
      edges.forEach(function (e) {
        if (e.count < thr) { return; }
        lines.push(e.raw);
        shown++;
      });
      src.textContent = lines.join("\n");
      window.ArchViewer.rerenderCard(card);
      summaryEl.textContent = shown + " of " + edges.length + " links (≥" + thr + " import" + (thr === 1 ? "" : "s") + ")";
    }

    thresholdEl.addEventListener("input", rebuild);
    bar.hidden = false;
    rebuild();
  })();

  /* ---- Dependencies page: internal/external layer toggle + highlight filter ----
     Parses the visible dep card's embedded Mermaid source and rebuilds it from the
     control state, then re-renders. External nodes carry ":::external"; edges to them
     are dashed ("-.->"). Highlight dims (opacity) non-matching nodes/edges via appended
     style/linkStyle statements. State is persisted (E1); quick-filter chips are built
     from the visible card's external packages (E2). Self-guards on #dep-filters. */
  (function () {
    var bar = document.getElementById("dep-filters");
    if (!bar || !window.ArchViewer) { return; }
    var cbInternal = document.getElementById("dep-internal");
    var cbExternal = document.getElementById("dep-external");
    var filterEl = document.getElementById("dep-filter");
    var chipsEl = document.getElementById("dep-chips");
    var summaryEl = document.getElementById("dep-summary");
    var sel = document.querySelector("select[data-diagram-select='deps']");

    // E1: persist toggle state (filter text stays transient). Guarded for file://.
    var STORE_KEY = "archdiagram-deps-filter";
    function loadPrefs() { try { return JSON.parse(localStorage.getItem(STORE_KEY) || "{}") || {}; } catch (e) { return {}; } }
    function savePrefs() {
      try { localStorage.setItem(STORE_KEY, JSON.stringify({ internal: cbInternal.checked, external: cbExternal.checked })); } catch (e) { }
    }
    var p0 = loadPrefs();
    if (typeof p0.internal === "boolean") { cbInternal.checked = p0.internal; }
    if (typeof p0.external === "boolean") { cbExternal.checked = p0.external; }

    function visibleCard() {
      var cards = document.querySelectorAll(".diagram-card[data-group='deps']");
      for (var i = 0; i < cards.length; i++) { if (!cards[i].hidden) { return cards[i]; } }
      return null;
    }

    // Classify one Mermaid line. Alias regex is n\d{3,} (grows past n999 on big diagrams).
    function parse(text) {
      var header = [], intNodes = [], extNodes = [], edges = [];
      text.split(/\r?\n/).forEach(function (raw) {
        var line = raw.trim();
        if (!line) { return; }
        if (/^flowchart/.test(line) || /^classDef/.test(line)) { header.push(line); return; }
        var edge = /^(n\d{3,})\s*-(\.?)->(?:\|"[^"]*"\|)?\s*(n\d{3,})/.exec(line);
        if (edge) { edges.push({ line: line, from: edge[1], to: edge[3] }); return; }
        var node = /^(n\d{3,})\s*[\[{(]/.exec(line);
        if (node) {
          var isExt = /:::external/.test(line);
          (isExt ? extNodes : intNodes).push({ alias: node[1], line: line });
        }
      });
      return { header: header, intNodes: intNodes, extNodes: extNodes, edges: edges };
    }

    function labelOf(nodeLine) {
      var m = /["']([^"']*)["']/.exec(nodeLine); // first quoted label
      return (m ? m[1] : "");
    }

    // E2: chips for the visible card's external packages (already count-desc ordered).
    function renderChips(extNodes) {
      if (!chipsEl) { return; }
      chipsEl.innerHTML = "";
      extNodes.slice(0, 8).forEach(function (n) {
        var name = labelOf(n.line);
        if (!name) { return; }
        var b = document.createElement("button");
        b.type = "button";
        b.className = "btn";
        b.style.padding = ".15rem .5rem";
        b.style.fontSize = ".75rem";
        b.textContent = name;
        b.addEventListener("click", function () { filterEl.value = name; apply(); });
        chipsEl.appendChild(b);
      });
    }

    function rebuild(card) {
      if (!card) { return; }
      if (card.dataset.depOriginal == null) {
        var src0 = card.querySelector(".mermaid-src");
        if (!src0) { return; }
        card.dataset.depOriginal = src0.textContent;
      }
      var p = parse(card.dataset.depOriginal);
      renderChips(p.extNodes);
      var showInt = cbInternal.checked, showExt = cbExternal.checked;
      var q = (filterEl.value || "").trim().toLowerCase();

      var live = {};
      p.intNodes.forEach(function (n) { if (showInt) { live[n.alias] = n; } });
      p.extNodes.forEach(function (n) { if (showExt) { live[n.alias] = n; } });

      function matches(alias) {
        if (!q) { return true; }
        var n = live[alias];
        return !!n && labelOf(n.line).toLowerCase().indexOf(q) >= 0;
      }

      var lines = p.header.slice();
      Object.keys(live).forEach(function (a) { lines.push(live[a].line); });
      var kept = [];
      p.edges.forEach(function (e) {
        if (!live[e.from] || !live[e.to]) { return; }
        kept.push(e);
        lines.push(e.line);
      });

      var shown = 0;
      if (q) {
        Object.keys(live).forEach(function (a) {
          if (matches(a)) { shown++; } else { lines.push("style " + a + " opacity:0.15"); }
        });
        kept.forEach(function (e, i) {
          if (!(matches(e.from) || matches(e.to))) { lines.push("linkStyle " + i + " opacity:0.12"); }
        });
      } else {
        shown = Object.keys(live).length;
      }

      card.querySelector(".mermaid-src").textContent = lines.join("\n");
      window.ArchViewer.rerenderCard(card);
      summaryEl.textContent =
        (showInt ? "internal on" : "internal off") + " · " +
        (showExt ? "external on" : "external off") +
        (q ? " · " + shown + " match “" + q + "”" : "");
    }

    function active() { return !cbInternal.checked || !cbExternal.checked || filterEl.value.trim().length > 0; }
    function apply() { rebuild(visibleCard()); }
    [cbInternal, cbExternal].forEach(function (el) {
      el.addEventListener("change", function () { savePrefs(); apply(); });
    });
    filterEl.addEventListener("input", apply);

    // Rebuild chips (and re-apply if filters are active) for whatever card is now shown.
    function refresh() {
      var card = visibleCard();
      if (!card) { return; }
      if (active()) { rebuild(card); return; }
      var src = card.querySelector(".mermaid-src");
      renderChips(src ? parse(card.dataset.depOriginal != null ? card.dataset.depOriginal : src.textContent).extNodes : []);
    }
    // site.js's own change handler renders the pristine new card first; refresh after it.
    if (sel) { sel.addEventListener("change", function () { setTimeout(refresh, 0); }); }
    bar.hidden = false;
    refresh();
  })();

  /* ---- Explore: client-side query console over the embedded dependency model ----
     Fixed, discoverable predicate vocabulary run entirely in-browser against window.ARCH_QUERY
     (the same node/edge payload the 3D graph uses). Self-guards: returns immediately on any page
     without #query-console. No network, no server — works from file://. */
  (function () {
    var box = document.getElementById("query-console");
    var data = window.ARCH_QUERY;
    if (!box || !data || !data.nodes) { return; }
    var input = document.getElementById("query-input");
    var resultsEl = document.getElementById("query-results");
    var countEl = document.getElementById("query-count");

    // Indexes over the payload, built once.
    var byId = {};
    data.nodes.forEach(function (n) { byId[n.id] = n; });
    var out = {}, inc = {};                       // adjacency: id -> [ids]
    (data.edges || []).forEach(function (e) {
      (out[e.source] = out[e.source] || []).push(e.target);
      (inc[e.target] = inc[e.target] || []).push(e.source);
    });
    var hasEdge = {};
    (data.edges || []).forEach(function (e) { hasEdge[e.source] = 1; hasEdge[e.target] = 1; });

    function matchNodes(term) {
      var t = (term || "").trim().toLowerCase();
      if (!t) { return []; }
      return data.nodes.filter(function (n) { return (n.path || "").toLowerCase().indexOf(t) >= 0; });
    }
    // BFS transitive closure over an adjacency map from a set of seed ids (excludes the seeds).
    function closure(seedIds, adj) {
      var seen = {}, queue = seedIds.slice(), result = {};
      seedIds.forEach(function (id) { seen[id] = 1; });
      while (queue.length) {
        var cur = queue.shift();
        (adj[cur] || []).forEach(function (nx) {
          if (!seen[nx]) { seen[nx] = 1; result[nx] = 1; queue.push(nx); }
        });
      }
      return Object.keys(result).map(function (id) { return byId[id]; }).filter(Boolean);
    }
    function shortestPath(fromId, toId) {
      if (fromId === toId) { return [byId[fromId]]; }
      var prev = {}, seen = {}, queue = [fromId]; seen[fromId] = 1;
      while (queue.length) {
        var cur = queue.shift();
        var nexts = out[cur] || [];
        for (var i = 0; i < nexts.length; i++) {
          var nx = nexts[i];
          if (seen[nx]) { continue; }
          seen[nx] = 1; prev[nx] = cur;
          if (nx === toId) {
            var chain = [toId]; for (var a = cur; a != null; a = prev[a]) { chain.push(a); }
            return chain.reverse().map(function (id) { return byId[id]; });
          }
          queue.push(nx);
        }
      }
      return null;
    }
    function idsToNodes(ids) {
      var seen = {}, list = [];
      (ids || []).forEach(function (id) { if (!seen[id] && byId[id]) { seen[id] = 1; list.push(byId[id]); } });
      return list;
    }

    var NUM = { loc: "loc", cog: "cog", fanin: "fanIn", fanout: "fanOut" };

    // Returns { nodes: [...], note: "" } or { error: "..." }.
    // SQL-flavoured aliases for the underlying verbs, so users can ask in SQL terms without the
    // engine itself changing: "referencedby: Orders" behaves exactly like "importedby: Orders".
    var VERB_ALIASES = [
      [/^references:/i, "imports:"],
      [/^referencedby:/i, "importedby:"],
      [/^reads:/i, "imports:"],
      [/^(readby|writtenby|writes):/i, "importedby:"],
      [/^affects:/i, "reaches:"],
      [/^affectedby:/i, "reachedby:"],
      [/^schema:/i, "folder:"],
      [/^kind:/i, "lang:"],
    ];

    function run(raw) {
      var q = (raw || "").trim();
      VERB_ALIASES.forEach(function (pair) { q = q.replace(pair[0], pair[1]); });
      if (!q) { return { nodes: [] }; }
      var lower = q.toLowerCase();

      // Numeric filter: <field> <op> <n>
      var num = /^(loc|cog|fanin|fanout)\s*(>=|<=|>|<|=)\s*(\d+)$/i.exec(q);
      if (num) {
        var field = NUM[num[1].toLowerCase()], op = num[2], n = parseInt(num[3], 10);
        var hits = data.nodes.filter(function (nd) {
          var v = nd[field] || 0;
          return op === ">" ? v > n : op === ">=" ? v >= n : op === "<" ? v < n : op === "<=" ? v <= n : v === n;
        });
        return { nodes: sortNodes(hits) };
      }

      var m;
      if ((m = /^orphans(?:\s+in\s+(.+))?$/i.exec(q))) {
        var folder = m[1] ? m[1].trim().toLowerCase() : null;
        var orph = data.nodes.filter(function (nd) {
          if (hasEdge[nd.id]) { return false; }
          return !folder || (nd.folder || "").toLowerCase() === folder || (nd.path || "").toLowerCase().indexOf(folder) >= 0;
        });
        return { nodes: sortNodes(orph) };
      }
      if ((m = /^folder:\s*(.+)$/i.exec(q))) {
        var f = m[1].trim().toLowerCase();
        return { nodes: sortNodes(data.nodes.filter(function (nd) { return (nd.folder || "").toLowerCase() === f; })) };
      }
      if ((m = /^lang:\s*(.+)$/i.exec(q))) {
        var lg = m[1].trim().toLowerCase();
        return { nodes: sortNodes(data.nodes.filter(function (nd) { return (nd.lang || "").toLowerCase().indexOf(lg) >= 0; })) };
      }
      if ((m = /^path:\s*(\S+)\s+(\S+)$/i.exec(q))) {
        var a = matchNodes(m[1]), b = matchNodes(m[2]);
        if (!a.length || !b.length) { return { nodes: [], note: "No file matches one of those names." }; }
        var p = shortestPath(a[0].id, b[0].id);
        return p ? { nodes: p, note: "shortest path (" + p.length + " nodes)" }
                 : { nodes: [], note: "No dependency path from " + a[0].path + " to " + b[0].path + "." };
      }
      if ((m = /^(imports|importedby|usedby|reaches|reachedby):\s*(.+)$/i.exec(q))) {
        var verb = m[1].toLowerCase(), anchors = matchNodes(m[2]);
        if (!anchors.length) { return { nodes: [], note: "No file matches “" + m[2].trim() + "”." }; }
        var ids = [];
        if (verb === "imports") { anchors.forEach(function (nd) { ids = ids.concat(out[nd.id] || []); }); return { nodes: sortNodes(idsToNodes(ids)) }; }
        if (verb === "importedby" || verb === "usedby") { anchors.forEach(function (nd) { ids = ids.concat(inc[nd.id] || []); }); return { nodes: sortNodes(idsToNodes(ids)) }; }
        var adj = verb === "reaches" ? out : inc;
        var acc = [];
        anchors.forEach(function (nd) { acc = acc.concat(closure([nd.id], adj)); });
        return { nodes: sortNodes(idsToNodes(acc.map(function (nd) { return nd.id; }))) };
      }
      return { error: "Unrecognised query. Open “Query reference” for the supported forms." };
    }

    function sortNodes(list) {
      return list.slice().sort(function (x, y) { return (x.path || "").localeCompare(y.path || ""); });
    }

    function render(res) {
      resultsEl.innerHTML = "";
      if (res.error) { countEl.textContent = ""; resultsEl.innerHTML = '<li class="palette-empty">' + res.error + "</li>"; return; }
      var n = res.nodes.length;
      countEl.textContent = n + " file" + (n === 1 ? "" : "s") + (res.note ? " · " + res.note : "");
      if (n === 0 && !res.note) { resultsEl.innerHTML = '<li class="palette-empty">No matches.</li>'; return; }
      res.nodes.forEach(function (nd) {
        var li = document.createElement("li");
        var a = document.createElement("a");
        a.href = nd.href; a.textContent = nd.path;
        li.appendChild(a);
        var meta = document.createElement("span");
        meta.className = "palette-detail";
        meta.textContent = nd.lang + " · " + (nd.loc || 0) + " LOC" + (nd.cog ? " · cog " + nd.cog : "");
        li.appendChild(meta);
        resultsEl.appendChild(li);
      });
    }

    // Land on a real answer instead of an empty console: a query box with nothing typed and
    // nothing shown teaches a new reader nothing about what it can ask. The last query run in
    // this browser is restored across visits; failing that, the most depended-upon files is the
    // default — interesting on any repo and never empty, unlike a blank result set.
    var STORE_KEY = "arch-explore-last-query";
    function persistQuery(q) {
      try { if (q) { localStorage.setItem(STORE_KEY, q); } else { localStorage.removeItem(STORE_KEY); } } catch (e) { }
    }
    function topByFanIn(n) {
      return data.nodes.slice().sort(function (a, b) { return (b.fanIn || 0) - (a.fanIn || 0); }).slice(0, n);
    }

    function go() { var q = input.value; persistQuery(q.trim()); render(run(q)); }
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); go(); } });
    box.querySelectorAll(".query-example").forEach(function (btn) {
      btn.addEventListener("click", function () { input.value = btn.textContent; go(); input.focus(); });
    });

    var restoredQuery = "";
    try { restoredQuery = localStorage.getItem(STORE_KEY) || ""; } catch (e) { }
    if (restoredQuery) {
      input.value = restoredQuery;
      render(run(restoredQuery));
    } else {
      render({ nodes: topByFanIn(10), note: "most depended-upon files — edit the box above to ask something else" });
    }
  })();
})();

// ---- Trace: pick a start and optional end, find the shortest honest chain between
// them. Two plain BFS passes (never a weighted search): first over only high-confidence
// edges, then, only if that finds nothing within the hop cap, over every edge. Modelled
// on the Explore console's shortestPath/closure above. Self-guards on #trace-console. ----
(function () {
  var data = window.ARCH_TRACE;
  var box = document.getElementById("trace-console");
  if (!box || !data || !data.nodes) { return; }

  var byId = {};
  data.nodes.forEach(function (n) { byId[n.id] = n; });
  var out = {};
  data.edges.forEach(function (e) {
    (out[e.source] = out[e.source] || []).push(e);
  });

  var MAX_HOPS = 12;

  function isCertain(e) { return e.kind !== "call" || (e.candidates || 1) <= 2; }

  function bfs(fromId, toId, edgeFilter) {
    if (fromId === toId) { return [{ node: byId[fromId], via: null }]; }
    var prev = {}, seen = {}, queue = [fromId];
    seen[fromId] = true;
    while (queue.length) {
      var cur = queue.shift();
      var edges = (out[cur] || []).filter(edgeFilter);
      for (var i = 0; i < edges.length; i++) {
        var e = edges[i], nb = e.target;
        if (seen[nb]) { continue; }
        seen[nb] = true; prev[nb] = { from: cur, edge: e };
        if (nb === toId) {
          var chain = [{ node: byId[nb], via: e }];
          for (var at = cur; at != null; ) {
            var p = prev[at];
            chain.unshift({ node: byId[at], via: p ? p.edge : null });
            at = p ? p.from : null;
          }
          return chain;
        }
        if (Object.keys(seen).length <= MAX_HOPS * 50) { queue.push(nb); } // breadth guard, not a hop-count guard
      }
    }
    return null;
  }

  // Downstream-only closure (no target given): everything reachable, for the
  // "off the spine" summary. Same shape as Explore's closure(), reused, not reinvented.
  function closure(fromId, edgeFilter) {
    var seen = {}, queue = [fromId], result = [];
    seen[fromId] = true;
    while (queue.length) {
      var cur = queue.shift();
      (out[cur] || []).filter(edgeFilter).forEach(function (e) {
        if (!seen[e.target]) { seen[e.target] = true; result.push(byId[e.target]); queue.push(e.target); }
      });
    }
    return result;
  }

  function trace(fromId, toId) {
    if (!toId) { return { spine: null, reachable: closure(fromId, function () { return true; }) }; }
    var certain = bfs(fromId, toId, isCertain);
    if (certain) { return { spine: certain, degraded: false }; }
    var any = bfs(fromId, toId, function () { return true; });
    return { spine: any, degraded: !!any };
  }

  window.ArchTrace = { trace: trace, closure: closure, nodeById: function (id) { return byId[id]; } };

  // ---- Wiring: ranked matching + an inline autocomplete dropdown (modelled on the Ctrl+K
  // search palette's combobox pattern — aria-activedescendant, arrow keys, click-to-select),
  // so "which node did my typed text resolve to" is never silent. ----
  var fromInput = document.getElementById("trace-from");
  var toInput = document.getElementById("trace-to");
  var fromList = document.getElementById("trace-from-list");
  var toList = document.getElementById("trace-to-list");
  var results = document.getElementById("trace-results");
  var diagramCard = document.getElementById("trace-diagram");
  var countEl = document.getElementById("trace-count");
  var examplesEl = document.getElementById("trace-examples");
  var cbImports = document.getElementById("trace-imports");
  var cbCalls = document.getElementById("trace-calls");
  var cbData = document.getElementById("trace-data");

  // Ranked, not array-order: exact-prefix beats mid-string, name beats path-only. Explore's
  // path:/imports: verbs can afford a silent first-match (you see the whole result set right
  // there); Trace hinges its entire answer on this one pick, so ranking plus the dropdown below
  // are what make a wrong guess visible and fixable instead of a silently mistraced chain.
  function scoreNode(n, q) {
    var name = (n.label || n.path || n.id || "").toLowerCase();
    var path = (n.path || "").toLowerCase();
    var i = name.indexOf(q);
    if (i === 0) { return 100; }
    if (i > 0) { return 60 - Math.min(40, i); }
    var pi = path.indexOf(q);
    if (pi === 0) { return 55; }
    if (pi > 0) { return 30 - Math.min(20, pi); }
    return 0;
  }

  function matchNodes(term) {
    var t = (term || "").trim().toLowerCase();
    if (!t) { return []; }
    var scored = [];
    for (var i = 0; i < data.nodes.length; i++) {
      var s = scoreNode(data.nodes[i], t);
      if (s > 0) { scored.push([s, data.nodes[i]]); }
    }
    scored.sort(function (a, b) { return b[0] - a[0]; });
    return scored.map(function (x) { return x[1]; });
  }

  function matchNode(term) { return matchNodes(term)[0] || null; }

  // One dropdown behaviour, attached to both the "from" and "to" fields.
  function attachAutocomplete(input, listEl) {
    if (!input || !listEl) { return { setExternally: function () {} }; }
    var hits = [], sel = -1;

    function close() {
      listEl.hidden = true; listEl.innerHTML = "";
      input.setAttribute("aria-expanded", "false");
      input.removeAttribute("aria-activedescendant");
      sel = -1;
    }
    function paint() {
      listEl.innerHTML = "";
      if (!hits.length) { close(); return; }
      hits.forEach(function (n, i) {
        var li = document.createElement("li");
        li.id = input.id + "-opt-" + i;
        li.setAttribute("role", "option");
        li.setAttribute("aria-selected", i === sel ? "true" : "false");
        if (i === sel) { li.className = "selected"; }
        var name = document.createElement("span");
        name.className = "palette-name";
        name.textContent = n.label || n.path || n.id;
        var detail = document.createElement("span");
        detail.className = "palette-detail";
        detail.textContent = n.path && n.path !== name.textContent ? n.path : (n.lang || n.layer || "");
        li.appendChild(name); li.appendChild(detail);
        // mousedown (not click) fires before the input's blur, so choosing an option never
        // races the blur-triggered close() below.
        li.addEventListener("mousedown", function (e) { e.preventDefault(); choose(n); });
        li.addEventListener("mousemove", function () { if (sel !== i) { sel = i; paint(); } });
        listEl.appendChild(li);
      });
      listEl.hidden = false;
      input.setAttribute("aria-expanded", "true");
      if (sel >= 0) { input.setAttribute("aria-activedescendant", input.id + "-opt-" + sel); }
      else { input.removeAttribute("aria-activedescendant"); }
    }
    function choose(n) {
      input.value = n.label || n.path || n.id;
      close();
      render();
    }
    function refresh() {
      hits = matchNodes(input.value).slice(0, 8);
      sel = -1;
      paint();
    }
    input.addEventListener("input", function () { refresh(); render(); });
    input.addEventListener("keydown", function (e) {
      if (listEl.hidden && (e.key === "ArrowDown" || e.key === "ArrowUp")) { refresh(); }
      if (e.key === "ArrowDown" && hits.length) { e.preventDefault(); sel = Math.min(hits.length - 1, sel + 1); paint(); }
      else if (e.key === "ArrowUp" && hits.length) { e.preventDefault(); sel = Math.max(0, sel - 1); paint(); }
      else if (e.key === "Enter" && sel >= 0 && hits[sel]) { e.preventDefault(); choose(hits[sel]); }
      else if (e.key === "Escape" && !listEl.hidden) { close(); }
    });
    // A blur means focus left the field for good (the mousedown above already beat this for
    // clicks on an option), so it's safe to always close here.
    input.addEventListener("blur", function () { close(); });
    return { setExternally: choose };
  }

  var fromAC = attachAutocomplete(fromInput, fromList);
  var toAC = attachAutocomplete(toInput, toList);

  // "Try:" chips from the real graph — routes first (the page's own copy leads with
  // "an endpoint"), padded out with the most downstream-reaching files. Never empty unless the
  // codebase genuinely has neither, same "land on a real example" reasoning as Explore's default.
  if (examplesEl) {
    var routePicks = data.nodes.filter(function (n) { return n.layer === "route"; }).slice(0, 2);
    var filePicks = data.nodes.filter(function (n) { return n.layer === "file"; })
      .slice().sort(function (a, b) { return (b.fanOut || 0) - (a.fanOut || 0); });
    var picks = routePicks.concat(filePicks).slice(0, 4);
    if (picks.length) {
      var lbl = document.createElement("span");
      lbl.textContent = "Try:";
      examplesEl.appendChild(lbl);
      picks.forEach(function (n) {
        var btn = document.createElement("button");
        btn.type = "button";
        btn.className = "btn";
        btn.style.cssText = "padding:.15rem .5rem;font-size:.75rem";
        btn.textContent = n.label || n.path;
        btn.addEventListener("click", function () { fromAC.setExternally(n); fromInput.focus(); });
        examplesEl.appendChild(btn);
      });
    }
  }

  function activeEdgeFilter() {
    var kinds = {};
    if (!cbImports || cbImports.checked) { kinds["import"] = 1; }
    if (!cbCalls || cbCalls.checked) { kinds["call"] = 1; }
    if (!cbData || cbData.checked) { kinds["data-access"] = 1; kinds["route"] = 1; }
    return function (e) { return !!kinds[e.kind]; };
  }

  // Node labels/paths and edge metadata come from the scanned codebase's own file paths,
  // route templates and method names — arbitrary text, not markup — so every value below
  // goes through esc() before it reaches innerHTML, matching the Explain popover's own
  // esc() a few blocks down (rather than the DOM-builder style Explore's render() uses,
  // since this markup is link+badge-shaped, not a plain text node per row).
  function esc(s) { return (s == null ? "" : String(s)).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }

  function nodeLabel(n) {
    if (!n) { return "?"; }
    return esc(n.label || n.path || n.id);
  }

  function nodeHref(n) {
    if (!n) { return null; }
    if (n.href) { return esc(n.href); }
    return null;
  }

  // Two files can share a bare label ("Program.cs" in two different projects) — the label
  // alone would make such a hop look identical to its neighbour, so the full path rides
  // alongside it whenever it says more than the label already does.
  function hopMeta(n) {
    if (!n || !n.path || n.path === (n.label || n.path)) { return ""; }
    return ' <span class="hop-meta">' + esc(n.path) + '</span>';
  }

  // ---- Path diagram: render the found from->to spine as a small Mermaid flowchart in
  // #trace-diagram (see TracePage.cs — the card ships empty/hidden at build time since the
  // chain itself is only known once a query runs). Only for the point-to-point case: the
  // open-ended "everything downstream" closure can run into the hundreds of nodes, which is
  // exactly what the 3D graph's own flow-trace mode exists for (see the "View in 3D graph"
  // link below) rather than a mermaid flowchart. ----

  // Mirrors MermaidRenderer.ClassDefs (Arch.Code/Rendering/MermaidRenderer.cs) so a traced
  // path uses the same file/service/database colour language as every other diagram on the
  // site. Duplicated rather than shared: the path is only known at runtime in the browser,
  // and MermaidRenderer only ever runs at build time.
  var TRACE_CLASSDEFS =
    "classDef service fill:var(--accent-soft),stroke:var(--accent),color:var(--text);\n" +
    "classDef database fill:var(--diagram-db-soft),stroke:var(--diagram-db),color:var(--diagram-db-ink);\n" +
    "classDef file fill:var(--ok-soft),stroke:var(--ok),color:var(--ok-ink);";

  // Same escaping as MermaidRenderer.Escape — a label inside a quoted mermaid node/edge label.
  function mmdEscape(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/"/g, "#quot;").replace(/</g, "#lt;").replace(/>/g, "#gt;")
      .replace(/\{/g, "#123;").replace(/\}/g, "#125;").replace(/\|/g, "#124;")
      .replace(/`/g, "'").replace(/\r/g, " ").replace(/\n/g, " ");
  }

  function traceNodeShape(n) {
    if (n.layer === "table") { return { open: '[("', close: '")]', css: "database" }; }
    if (n.layer === "route") { return { open: '["', close: '"]', css: "service" }; }
    return { open: '["', close: '"]', css: "file" };
  }

  // Same evidence a hop's badge shows in the text view (renderHop), condensed to a short
  // edge label — dashed for the same "not fully certain" hops the text view flags as a warning.
  function traceEdgeStyle(via) {
    if (!via) { return { label: "", dashed: false }; }
    if (via.kind === "call") {
      var cand = via.candidates || 1;
      return cand > 1 ? { label: cand + " candidates", dashed: true } : { label: "call", dashed: false };
    }
    if (via.kind === "import") { return { label: "import", dashed: false }; }
    if (via.kind === "route") { return { label: "route", dashed: false }; }
    if (via.kind === "data-access") {
      return via.confidence === 0 ? { label: "blind spot", dashed: true } : { label: via.ops || "data access", dashed: false };
    }
    return { label: "", dashed: false };
  }

  function hideTraceDiagram() {
    if (diagramCard) { diagramCard.hidden = true; }
  }

  function updateTraceDiagram(spine) {
    if (!diagramCard) { return; }
    var lines = ["flowchart LR", TRACE_CLASSDEFS];
    var tooltips = {}, hrefs = {}, adjacency = {};
    spine.forEach(function (entry, i) {
      var n = entry.node || {}, alias = "t" + i, shape = traceNodeShape(n);
      lines.push(alias + shape.open + mmdEscape(n.label || n.path || n.id || "?") + shape.close + ":::" + shape.css);
      if (n.path) { tooltips[alias] = n.path; }
      if (n.href) { hrefs[alias] = n.href; }
    });
    for (var i = 1; i < spine.length; i++) {
      var from = "t" + (i - 1), to = "t" + i, style = traceEdgeStyle(spine[i].via);
      var arrow = style.dashed ? "-.->" : "-->";
      lines.push(style.label
        ? from + ' ' + arrow + '|"' + mmdEscape(style.label) + '"| ' + to
        : from + ' ' + arrow + ' ' + to);
      (adjacency[from] = adjacency[from] || []).push(to);
      (adjacency[to] = adjacency[to] || []).push(from);
    }

    var srcEl = diagramCard.querySelector(".mermaid-src");
    var tipEl = diagramCard.querySelector("script.tooltips");
    var hrefEl = diagramCard.querySelector("script.hrefs");
    var adjEl = diagramCard.querySelector("script.adjacency");
    if (srcEl) { srcEl.textContent = lines.join("\n"); }
    if (tipEl) { tipEl.textContent = JSON.stringify(tooltips); }
    if (hrefEl) { hrefEl.textContent = JSON.stringify(hrefs); }
    if (adjEl) { adjEl.textContent = JSON.stringify(adjacency); }
    diagramCard.hidden = false;
    // ArchViewer.rerenderCard (site.js's own diagram viewer, above) re-runs mermaid.render on
    // the updated source and rebinds tooltips/hrefs/hover-highlight from the maps just written.
    if (window.ArchViewer) { window.ArchViewer.rerenderCard(diagramCard); }
  }

  function renderHop(entry, isFirst) {
    var n = entry.node;
    var label = nodeHref(n) ? '<a href="' + nodeHref(n) + '">' + nodeLabel(n) + '</a>' : nodeLabel(n);
    label += hopMeta(n);
    var evidence = "";
    if (entry.via) {
      var kind = entry.via.kind;
      if (kind === "call") {
        var cand = entry.via.candidates || 1;
        evidence = cand > 1
          ? ' <span class="badge warn">call · ' + cand + ' candidates</span>'
          : ' <span class="badge">call</span>';
      } else if (kind === "import") {
        evidence = ' <span class="badge">import</span>';
      } else if (kind === "route") {
        evidence = ' <span class="badge">route</span>';
      } else if (kind === "data-access") {
        evidence = entry.via.confidence === 0
          ? ' <span class="badge warn">data access · blind spot</span>'
          : ' <span class="badge">data access · ' + esc(entry.via.ops || "") + '</span>';
      }
    }
    var sep = isFirst ? "" : '<span class="crumb-sep">→</span> ';
    return "<li>" + sep + label + evidence + "</li>";
  }

  function setCount(text) { if (countEl) { countEl.textContent = text; } }

  function render() {
    if (!results) { return; }
    var fromNode = matchNode(fromInput ? fromInput.value : "");
    if (!fromNode) {
      setCount("");
      hideTraceDiagram();
      results.innerHTML = '<div class="panel empty-state"><div class="big">🧭</div>'
        + "<p>Type a class, method, route, or file name above to trace from it.</p></div>";
      return;
    }
    var toTerm = toInput ? toInput.value : "";
    var toNode = matchNode(toTerm);
    var filter = activeEdgeFilter();

    if (toTerm.trim() && !toNode) {
      setCount("");
      hideTraceDiagram();
      results.innerHTML = '<p class="note">No file, route, or table matches “' + esc(toTerm) + '”.</p>';
      return;
    }

    if (!toNode) {
      hideTraceDiagram();
      var reachable = closure(fromNode.id, filter);
      setCount(reachable.length + " reachable");
      // The 3D graph's own file+call node ids match this page's "file" layer 1:1 (see
      // TraceDataWriter — Trace reuses GraphDataWriter's file nodes verbatim), so a plain
      // #flow= deep link works with no lookup; the synthetic route:/table: nodes Trace adds
      // don't exist over there, so the link only appears for a file-layer starting point.
      var flowLink = fromNode.layer === "file"
        ? ' <a class="btn" href="graph.html#flow=' + encodeURIComponent(fromNode.id) + '" ' +
          'title="Open the 3D graph, coloured by hops from this file">↯ View in 3D graph →</a>'
        : "";
      var html = '<p class="lede">Everything reachable downstream of ' + nodeLabel(fromNode) + "." + flowLink + "</p><ul class=\"member-list\">";
      reachable.slice(0, 200).forEach(function (n) {
        var href = nodeHref(n);
        html += "<li>" + (href ? '<a href="' + href + '">' + nodeLabel(n) + "</a>" : nodeLabel(n)) + hopMeta(n) + "</li>";
      });
      html += "</ul>";
      if (reachable.length > 200) { html += '<p class="note">Showing the first 200 of ' + reachable.length + ".</p>"; }
      results.innerHTML = html;
      return;
    }

    var result = traceFiltered(fromNode.id, toNode.id, filter);
    if (!result.spine) {
      setCount("");
      hideTraceDiagram();
      results.innerHTML = '<p class="note">No path from <strong>' + nodeLabel(fromNode) + "</strong> to <strong>"
        + nodeLabel(toNode) + "</strong> within " + MAX_HOPS + " hops. This is a heuristic graph — a real path "
        + "may exist through code this scan can't see (reflection, dynamic dispatch, configuration-driven wiring). "
        + "Try widening what Trace follows, above.</p>";
      return;
    }
    var hops = result.spine.length - 1;
    setCount(hops + " hop" + (hops === 1 ? "" : "s"));
    updateTraceDiagram(result.spine);
    var header = result.degraded
      ? '<p class="lede"><span class="badge warn">ambiguous path</span> No fully-certain path exists — at least '
        + "one hop below matched more than one declared method. Each ambiguous hop names how many candidates it "
        + "could have meant.</p>"
      : "";
    var body = header + '<ul class="member-list">'
      + result.spine.map(function (entry, i) { return renderHop(entry, i === 0); }).join("")
      + "</ul>";
    results.innerHTML = body;
  }

  // window.ArchTrace.trace (above) always prefers certain edges over every edge, with no
  // filter — the stable public API. The page's own checkboxes need one more axis (which
  // edge KINDS to follow at all), so the UI wiring below layers that on top rather than
  // growing trace()'s signature.
  function traceFiltered(fromId, toId, filter) {
    if (!toId) { return { spine: null, reachable: closure(fromId, filter) }; }
    var certain = bfs(fromId, toId, function (e) { return filter(e) && isCertain(e); });
    if (certain) { return { spine: certain, degraded: false }; }
    var any = bfs(fromId, toId, filter);
    return { spine: any, degraded: !!any };
  }

  // fromInput/toInput are wired above by attachAutocomplete (their "input" handler already
  // calls render()) — only the checkboxes need generic wiring here.
  [cbImports, cbCalls, cbData].forEach(function (el) {
    if (!el) { return; }
    el.addEventListener("input", render);
    el.addEventListener("change", render);
  });
  render();
})();

// ---- Explain (ⓘ) popovers: Simple + Go deeper, from the embedded glossary ----
(function () {
  var pop = document.getElementById("explain-pop");
  var dataEl = document.getElementById("arch-glossary");
  if (!pop || !dataEl) { return; }
  var glossary = {};
  try { glossary = JSON.parse(dataEl.textContent) || {}; } catch (e) { glossary = {}; }

  function title(term) {
    return term.replace(/-/g, " ").replace(/\b\w/g, function (c) { return c.toUpperCase(); });
  }
  function esc(s) { return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }

  var current = null;
  function close() {
    // Hand focus back to the ⓘ that opened this. The popover lives at the end of <body>
    // while its trigger sits mid-content, so anything focused inside it was orphaned in
    // tab order — closing without restoring dropped the user to the top of the document.
    var opener = current;
    pop.hidden = true;
    current = null;
    if (opener && opener.focus && pop.contains(document.activeElement)) { opener.focus(); }
  }

  function open(btn) {
    var term = btn.getAttribute("data-term");
    var entry = glossary[term];
    if (!entry) { return; }
    current = btn;
    pop.innerHTML =
      '<div class="exp-term">' + esc(title(term)) + '</div>' +
      '<div class="exp-simple">' + esc(entry.simple) + '</div>' +
      (entry.detail ? '<button class="exp-more" type="button">Go deeper ▾</button>' +
        '<div class="exp-detail" hidden>' + esc(entry.detail) + '</div>' : '');
    pop.hidden = false;
    var more = pop.querySelector(".exp-more");
    if (more) {
      more.setAttribute("aria-expanded", "false");
      more.addEventListener("click", function () {
        var d = pop.querySelector(".exp-detail");
        var show = d.hidden;
        d.hidden = !show;
        more.setAttribute("aria-expanded", show ? "true" : "false");
        more.textContent = show ? "Show less ▴" : "Go deeper ▾";
      });
      // Reachable by Tab from the trigger rather than stranded at the end of the document:
      // Tab out of the popover closes it, so focus rejoins the page where it left off.
      more.addEventListener("keydown", function (e) {
        if (e.key === "Tab" && !e.shiftKey) { close(); }
      });
      more.focus();
    }
    // Position below the button, clamped to the viewport.
    var r = btn.getBoundingClientRect();
    var x = Math.min(r.left, window.innerWidth - pop.offsetWidth - 10);
    var y = r.bottom + 6;
    var above = false;
    if (y + pop.offsetHeight > window.innerHeight - 8) { y = Math.max(8, r.top - pop.offsetHeight - 6); above = true; }
    pop.style.left = Math.max(8, x) + "px";
    pop.style.top = y + "px";
    // Scale in from whichever edge sits against the trigger, not the popover's own center.
    pop.style.setProperty("--transform-origin", above ? "bottom" : "top");
  }

  document.addEventListener("click", function (e) {
    var btn = e.target.closest && e.target.closest(".explain");
    if (btn) { e.preventDefault(); if (current === btn) { close(); } else { open(btn); } return; }
    if (!pop.hidden && !pop.contains(e.target)) { close(); }
  });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") { close(); } });
})();

// ---- Mobile: off-canvas sidebar toggle ----
(function () {
  var toggle = document.getElementById("nav-toggle");
  var layout = document.querySelector(".layout");
  var overlay = document.getElementById("nav-overlay");
  if (!toggle || !layout) { return; }
  function open() { layout.classList.add("nav-open"); toggle.setAttribute("aria-expanded", "true"); if (overlay) { overlay.hidden = false; } }
  function close() {
    var wasOpen = layout.classList.contains("nav-open");
    layout.classList.remove("nav-open");
    toggle.setAttribute("aria-expanded", "false");
    // Hand focus back to the button that opened the drawer, rather than letting it fall to
    // <body> when the element holding it slides off-canvas. Guarded on wasOpen so the
    // document-level Escape handler doesn't steal focus on every Esc press page-wide.
    if (wasOpen) { toggle.focus(); }
  }
  toggle.addEventListener("click", function () { layout.classList.contains("nav-open") ? close() : open(); });
  if (overlay) { overlay.addEventListener("click", close); }
  document.querySelectorAll(".sidebar nav a").forEach(function (a) { a.addEventListener("click", close); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") { close(); } });
})();

// ---- Neighborhood diagram: a Mermaid subgraph centered on one object, N hops in/out, built
// entirely from window.ARCH_QUERY. Exposes window.ArchNeighborhood.render so both object.html and
// any future launcher (ER/Dependencies/Explore) can drive it. Self-guards: does nothing if the
// payload or the diagram card isn't on the page. ----
(function () {
  var data = window.ARCH_QUERY;
  if (!data || !data.nodes) { return; }

  var byId = {};
  data.nodes.forEach(function (n) { byId[n.id] = n; });
  var out = {}, inc = {};
  (data.edges || []).forEach(function (e) {
    (out[e.source] = out[e.source] || []).push(e);
    (inc[e.target] = inc[e.target] || []).push(e);
  });

  var MAX_NODES = 40;

  function neighborsWithin(centerId, hops, direction) {
    var seen = {}; seen[centerId] = 0;
    var queue = [centerId];
    var edgesUsed = [];
    while (queue.length) {
      var cur = queue.shift();
      var depth = seen[cur];
      if (depth >= hops) { continue; }
      var forward = direction !== "in" ? (out[cur] || []) : [];
      var backward = direction !== "out" ? (inc[cur] || []) : [];
      forward.concat(backward).forEach(function (e) {
        var nb = e.source === cur ? e.target : e.source;
        edgesUsed.push(e);
        if (!(nb in seen)) { seen[nb] = depth + 1; queue.push(nb); }
      });
    }
    var ids = Object.keys(seen);
    if (ids.length > MAX_NODES) {
      // Most-connected-first, center always kept.
      ids.sort(function (a, b) {
        var wa = (out[a] || []).length + (inc[a] || []).length;
        var wb = (out[b] || []).length + (inc[b] || []).length;
        return wb - wa;
      });
      ids = [centerId].concat(ids.filter(function (id) { return id !== centerId; }).slice(0, MAX_NODES - 1));
    }
    var idSet = {}; ids.forEach(function (id) { idSet[id] = 1; });
    var edges = edgesUsed.filter(function (e) { return idSet[e.source] && idSet[e.target]; });
    // De-dup edges (source,target,kind).
    var edgeSeen = {}, dedupEdges = [];
    edges.forEach(function (e) {
      var k = e.source + ">" + e.target + ">" + e.kind;
      if (!edgeSeen[k]) { edgeSeen[k] = 1; dedupEdges.push(e); }
    });
    return { ids: ids, edges: dedupEdges, capped: ids.length < Object.keys(seen).length };
  }

  function shapeFor(token, node) {
    var label = (node.path || node.label || node.id).replace(/"/g, "'");
    if (node.lang === "table") { return token + "[\"" + label + "\"]"; }
    if (node.lang === "view") { return token + "(\"" + label + "\")"; }
    return token + "{{\"" + label + "\"}}";
  }

  function buildMermaid(centerId, ids, edges) {
    var token = {};
    ids.forEach(function (id, i) { token[id] = "n" + (100 + i); });
    var lines = ["flowchart LR"];
    ids.forEach(function (id) {
      var node = byId[id];
      if (node) { lines.push("  " + shapeFor(token[id], node) + (id === centerId ? ":::center" : "")); }
    });
    var cascadeIdx = [];
    edges.forEach(function (e, i) {
      lines.push("  " + token[e.source] + " --> " + token[e.target]);
      if (e.kind === "fk-cascade") { cascadeIdx.push(i); }
    });
    lines.push("  classDef center stroke-width:3px;");
    cascadeIdx.forEach(function (i) { lines.push("  linkStyle " + i + " stroke:var(--danger),stroke-width:2px;"); });
    return { source: lines.join("\n"), token: token };
  }

  function render(opts) {
    var card = document.getElementById(opts.cardId);
    if (!card || !window.ArchViewer) { return; }
    var centerId = opts.centerId, hops = opts.hops || 1, direction = opts.direction || "both";
    var center = byId[centerId];
    if (!center) { return; }

    var nb = neighborsWithin(centerId, hops, direction);
    var built = buildMermaid(centerId, nb.ids, nb.edges);
    var src = card.querySelector(".mermaid-src");
    if (src) { src.textContent = built.source; }
    // Stored on the card (not captured in the click closure below) so a later render() call from a
    // recenter updates what clicks resolve against — the closure is bound once, but reads this
    // field fresh on every click.
    card._archTokenMap = built.token;

    // Adjacency (token -> neighbour tokens, both directions) drives the shared hover-highlight in
    // attachTooltips. Without it, hovering a node dims every other node (including its neighbours),
    // which reads as "the target nodes disappeared". Injected as the card's <script.adjacency> so
    // attachTooltips picks it up when the card re-renders.
    var adjacency = {};
    nb.edges.forEach(function (e) {
      var s = built.token[e.source], t = built.token[e.target];
      if (!s || !t) { return; }
      (adjacency[s] = adjacency[s] || []).push(t);
      (adjacency[t] = adjacency[t] || []).push(s);
    });
    var adjEl = card.querySelector("script.adjacency");
    if (!adjEl) {
      adjEl = document.createElement("script");
      adjEl.className = "adjacency";
      adjEl.type = "application/json";
      card.appendChild(adjEl);
    }
    adjEl.textContent = JSON.stringify(adjacency);

    window.ArchViewer.rerenderCard(card);

    // Recenter on node click via event delegation (survives re-renders; bound once per card).
    var target = card.querySelector(".render-target");
    if (target && !target.dataset.neighborhoodBound) {
      target.dataset.neighborhoodBound = "1";
      target.addEventListener("click", function (e) {
        var el = e.target.closest && e.target.closest("[id^='flowchart-']");
        if (!el) { return; }
        var m = /^flowchart-(n\d+)-/.exec(el.id);
        if (!m) { return; }
        var clickedToken = m[1];
        var tokenMap = card._archTokenMap || {};
        var clickedId = null;
        Object.keys(tokenMap).forEach(function (id) { if (tokenMap[id] === clickedToken) { clickedId = id; } });
        if (clickedId && opts.onRecenter) { opts.onRecenter(clickedId); }
      });
    }

    if (opts.onRendered) { opts.onRendered(nb, center); }
  }

  window.ArchNeighborhood = { render: render, neighborsOf: function (id, hops, direction) { return neighborsWithin(id, hops, direction); }, nodeById: function (id) { return byId[id]; } };
})();

// ---- Object page: renders object.html entirely from ?id=, using window.ARCH_QUERY (graph/metrics)
// and window.ARCH_OBJDETAIL (columns/PK/findings/purpose). Self-guards on #object-page. ----
(function () {
  var root = document.getElementById("object-page");
  var data = window.ARCH_QUERY;
  if (!root || !data || !data.nodes) { return; }

  var byId = {};
  data.nodes.forEach(function (n) { byId[n.id] = n; });
  var detail = window.ARCH_OBJDETAIL || {};

  var notFound = document.getElementById("obj-notfound");
  var content = document.getElementById("obj-content");
  var els = {
    title: document.getElementById("obj-title"),
    purpose: document.getElementById("obj-purpose"),
    tiles: document.getElementById("obj-tiles"),
    columns: document.getElementById("obj-columns-wrap"),
    findings: document.getElementById("obj-findings-wrap"),
    depsIn: document.getElementById("obj-deps-in"),
    depsOut: document.getElementById("obj-deps-out"),
    impactLink: document.getElementById("obj-impact-link"),
    hops: document.getElementById("obj-hops"),
    hopsVal: document.getElementById("obj-hops-val"),
    direction: document.getElementById("obj-direction"),
  };

  function esc(s) { return (s == null ? "" : String(s)).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }

  function currentId() {
    var m = /[?&]id=([^&]+)/.exec(window.location.search);
    return m ? decodeURIComponent(m[1]) : "";
  }

  function tile(num, label) { return '<div class="tile' + (num === 0 || num === "0" ? " tile-zero" : "") + '"><div class="num">' + esc(num) + '</div><div class="lbl">' + esc(label) + '</div></div>'; }

  function renderDepsList(el, edges, otherSide) {
    if (!edges.length) { el.innerHTML = '<li class="palette-empty">None found.</li>'; return; }
    el.innerHTML = "";
    edges.forEach(function (e) {
      var otherId = e[otherSide];
      var node = byId[otherId];
      var li = document.createElement("li");
      var a = document.createElement("a");
      a.href = node ? node.href : "#";
      a.textContent = node ? node.path : otherId;
      li.appendChild(a);
      var meta = document.createElement("span");
      meta.className = "palette-detail";
      meta.textContent = e.kind;
      li.appendChild(meta);
      el.appendChild(li);
    });
  }

  function show(id) {
    var node = byId[id];
    if (!node) { notFound.hidden = false; content.hidden = true; return; }
    notFound.hidden = true; content.hidden = false;
    var d = detail[id] || {};

    els.title.textContent = node.path;
    els.purpose.textContent = d.purpose || "";
    els.tiles.innerHTML = tile(node.lang, "Kind") + tile(node.fanIn, "Fan-in") + tile(node.fanOut, "Fan-out")
      + (node.execs >= 0 ? tile(node.execs.toLocaleString(), "Executions") : "");

    if (d.columns && d.columns.length) {
      var rows = d.columns.map(function (c) {
        return "<tr><td>" + esc(c.name) + "</td><td>" + esc(c.type) + "</td><td>" + (c.nullable ? "Yes" : "No") + "</td><td>" + (c.pk ? "✓" : "") + "</td></tr>";
      }).join("");
      els.columns.innerHTML = '<table class="grid"><tr><th>Column</th><th>Type</th><th>Nullable</th><th>PK</th></tr>' + rows + "</table>";
    } else { els.columns.innerHTML = ""; }

    if (d.findings && d.findings.length) {
      els.findings.innerHTML = "<p><strong>Lint findings:</strong></p><ul>" + d.findings.map(function (f) {
        return '<li><span class="badge warn">' + esc(f.ruleId) + "</span> " + esc(f.message) + "</li>";
      }).join("") + "</ul>";
    } else { els.findings.innerHTML = ""; }

    if (d.fileHref) {
      var link = document.getElementById("obj-source-link");
      if (link) { link.href = d.fileHref; link.hidden = false; }
    }
    if (els.impactLink) { els.impactLink.href = "impact.html?id=" + encodeURIComponent(id); }

    var inEdges = (data.edges || []).filter(function (e) { return e.target === id; });
    var outEdges = (data.edges || []).filter(function (e) { return e.source === id; });
    renderDepsList(els.depsIn, inEdges, "source");
    renderDepsList(els.depsOut, outEdges, "target");

    renderDiagram(id);
  }

  function renderDiagram(id) {
    var hops = els.hops ? parseInt(els.hops.value, 10) || 1 : 1;
    var direction = els.direction ? els.direction.value : "both";
    if (els.hopsVal) { els.hopsVal.textContent = hops; }
    window.ArchNeighborhood.render({
      cardId: "neighborhood-card",
      centerId: id,
      hops: hops,
      direction: direction,
      onRecenter: function (newId) {
        history.replaceState(null, "", "object.html?id=" + encodeURIComponent(newId));
        show(newId);
      },
    });
  }

  if (els.hops) { els.hops.addEventListener("input", function () { renderDiagram(currentId()); }); }
  if (els.direction) { els.direction.addEventListener("change", function () { renderDiagram(currentId()); }); }

  show(currentId());
})();

// ---- Sortable, paginated tables: <table class="grid sortable" data-page-size="20">. Click a
// header to sort by that column (toggles ascending/descending); rows beyond the page size are
// hidden behind a "Show all" toggle. Self-contained per table; does nothing to tables without the
// "sortable" class. ----
(function () {
  var tables = document.querySelectorAll("table.sortable");
  tables.forEach(function (table) {
    var thead = table.querySelector("thead");
    var tbody = table.querySelector("tbody");
    if (!thead || !tbody) { return; }
    var headers = thead.querySelectorAll("th");
    var pageSize = parseInt(table.getAttribute("data-page-size"), 10) || 0;
    var showingAll = false;

    function rows() { return Array.prototype.slice.call(tbody.querySelectorAll("tr")); }

    function cellValue(tr, idx) {
      var td = tr.children[idx];
      if (!td) { return ""; }
      var raw = td.getAttribute("data-sort-value");
      return raw != null ? raw : td.textContent.trim();
    }

    function applyPagination() {
      // With no page size the table isn't paginated; leave row visibility alone so a co-located
      // filter (.filter-input) that hides non-matching rows is not overridden on sort.
      if (pageSize <= 0) { return; }
      var all = rows();
      all.forEach(function (tr, i) {
        tr.style.display = (showingAll || i < pageSize) ? "" : "none";
      });
      var more = table.parentNode.querySelector(".table-more[data-for='" + table.id + "']");
      if (pageSize > 0 && all.length > pageSize) {
        if (!more) {
          more = document.createElement("button");
          more.type = "button";
          more.className = "btn table-more";
          more.setAttribute("data-for", table.id);
          table.parentNode.insertBefore(more, table.nextSibling);
          more.addEventListener("click", function () {
            showingAll = !showingAll;
            applyPagination();
          });
        }
        var hiddenCount = all.length - pageSize;
        more.textContent = showingAll ? "Show top " + pageSize : "Show all (" + hiddenCount + " more)";
        more.hidden = false;
      } else if (more) { more.hidden = true; }
    }

    if (pageSize > 0 && !table.id) { table.id = "sortable-" + Math.random().toString(36).slice(2, 9); }

    headers.forEach(function (th, idx) {
      th.classList.add("sortable-th");
      th.setAttribute("tabindex", "0");
      // role + aria-sort: the sort state was carried only by a CSS ::after arrow and a colour
      // change, so a screen reader had no way to know a column was sorted, or which way.
      th.setAttribute("role", "button");
      th.setAttribute("aria-sort", "none");
      var dir = 1;
      function sort() {
        var all = rows();
        all.sort(function (a, b) {
          var va = cellValue(a, idx), vb = cellValue(b, idx);
          var na = parseFloat(va.replace(/,/g, "")), nb = parseFloat(vb.replace(/,/g, ""));
          var cmp = (!isNaN(na) && !isNaN(nb)) ? na - nb : va.localeCompare(vb);
          return cmp * dir;
        });
        headers.forEach(function (h) { h.classList.remove("sort-asc", "sort-desc"); h.setAttribute("aria-sort", "none"); });
        th.classList.add(dir === 1 ? "sort-asc" : "sort-desc");
        th.setAttribute("aria-sort", dir === 1 ? "ascending" : "descending");
        all.forEach(function (tr) { tbody.appendChild(tr); });
        dir = -dir;
        applyPagination();
      }
      th.addEventListener("click", sort);
      th.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); sort(); } });
    });

    applyPagination();
  });
})();
