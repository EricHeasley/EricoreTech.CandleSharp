// Minimal JS interop: everything that actually needs the DOM (measuring the
// chart's rendered box, and a couple of app-wide keyboard shortcuts) lives
// here. All chart math, drawing, drag-pan and wheel-zoom are done in
// C#/Razor — this file has no chart logic of its own.
//
// Mouse coordinates use clientX/clientY (viewport-relative, well-defined
// regardless of which nested SVG element the pointer is actually over) minus
// this box's left/top, rather than MouseEventArgs.OffsetX/Y — offsetX/Y are
// relative to whichever element is the real event target, which inside an
// SVG can be a candle rect or a path several levels deep, not the wrapping
// div the C# code measures against.
//
// Blazor Server can't hand a raw JS function or observer back across the
// wire, so anything that needs disposing later (a ResizeObserver, an event
// listener) is wrapped in an object with its own dispose() method; C# holds
// that as an opaque IJSObjectReference and calls .InvokeVoidAsync("dispose").
window.candlesharp = {
  box(el) {
    if (!el) return { width: 0, left: 0, top: 0 };
    const r = el.getBoundingClientRect();
    return { width: r.width, left: r.left, top: r.top };
  },

  // Keeps a .NET component's cached box in sync when the window or a
  // sidebar toggle resizes/moves the chart, without polling from C#.
  observeResize(el, dotNetRef) {
    if (!el) return null;
    let last = el.clientWidth;
    const report = () => {
      const r = el.getBoundingClientRect();
      dotNetRef.invokeMethodAsync("OnResized", { width: r.width, left: r.left, top: r.top });
    };
    const ro = new ResizeObserver(() => {
      const w = el.clientWidth;
      if (Math.abs(w - last) > 1) {
        last = w;
        report();
      }
    });
    ro.observe(el);
    // Scrolling/resizing elsewhere on the page can move the box without changing its
    // width, which ResizeObserver won't catch; a cheap poll keeps clientX math correct.
    const interval = setInterval(report, 1000);
    return { dispose: () => { ro.disconnect(); clearInterval(interval); } };
  },

  // App-wide shortcuts: "/" focuses the ticker search, up/down arrows step
  // through the watchlist (handled in C# via OnArrowKey).
  bindShortcuts(dotNetRef) {
    const handler = e => {
      const tag = (e.target.tagName || "").toLowerCase();
      const typing = tag === "input" || tag === "textarea" || tag === "select" || e.target.isContentEditable;
      if (typing) return;
      if (e.key === "/") {
        e.preventDefault();
        document.getElementById("ticker-search")?.focus();
      } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        dotNetRef.invokeMethodAsync("OnArrowKey", e.key === "ArrowDown" ? 1 : -1);
      }
    };
    document.addEventListener("keydown", handler);
    return { dispose: () => document.removeEventListener("keydown", handler) };
  },
};
