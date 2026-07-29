"use strict";

const SVG_NS = "http://www.w3.org/2000/svg";
const W = 960, H = 420, PAD = { top: 16, right: 64, bottom: 28, left: 8 };
const RSI_H = 140;

// Overlay definitions: column names from the API mapped to palette slots.
// Bollinger renders as one band (fill + boundary lines) but toggles as one series.
const OVERLAYS = [
  { id: "sma20", label: "SMA 20", columns: ["SMA_20"], cssVar: "--s1", on: true },
  { id: "sma50", label: "SMA 50", columns: ["SMA_50"], cssVar: "--s2", on: true },
  { id: "ema12", label: "EMA 12", columns: ["EMA_12"], cssVar: "--s3", on: false },
  { id: "ema26", label: "EMA 26", columns: ["EMA_26"], cssVar: "--s4", on: false },
  { id: "st", label: "SuperTrend", short: "ST", columns: ["SUPERTREND"], cssVar: "--s5", on: false },
  { id: "psar", label: "PSAR", columns: ["PSAR"], cssVar: "--muted", dots: true, on: false },
  { id: "bb", label: "BB 20", columns: ["BB_upper_20", "BB_lower_20", "BB_mid_20"], cssVar: "--muted", band: true, fillVar: "--bb-fill", on: false },
  { id: "dc", label: "Donchian 20", columns: ["DONCHIAN_upper_20", "DONCHIAN_lower_20", "DONCHIAN_mid_20"], cssVar: "--muted", band: true, fillVar: "--dc-fill", on: false },
];

const state = {
  datasets: [],
  series: null,     // full API payload for the selected dataset
  range: 252,       // bars shown; "all" for everything
  overlays: Object.fromEntries(OVERLAYS.map(o => [o.id, o.on])),
};

const $ = id => document.getElementById(id);
const fmt = (v, digits = 2) => v == null ? "–" : v.toLocaleString("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
const fmtDate = iso => iso.slice(0, 10);
const cssColor = v => getComputedStyle(document.documentElement).getPropertyValue(v).trim();

function el(tag, attrs = {}, parent = null) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  if (parent) parent.appendChild(node);
  return node;
}

// ---------- data access ----------

async function api(path, options) {
  const response = await fetch(path, options);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error || `${response.status} ${response.statusText}`);
  return body;
}

async function loadDatasets(selectTicker) {
  state.datasets = await api("/api/datasets");
  const select = $("dataset-select");
  select.replaceChildren();
  for (const d of state.datasets) {
    const option = document.createElement("option");
    option.value = `${d.ticker}|${d.interval}`;
    option.textContent = `${d.ticker} (${d.interval}, ${d.bars} bars)`;
    select.appendChild(option);
  }
  $("empty-state").hidden = state.datasets.length > 0;
  if (state.datasets.length === 0) { showCards(false); $("screener-card").hidden = true; return; }
  state.screen = await api("/api/screen").catch(() => []);
  renderScreener();
  if (selectTicker) {
    const match = [...select.options].find(o => o.value.startsWith(selectTicker + "|"));
    if (match) select.value = match.value;
  }
  await loadSeries();
}

async function loadSeries() {
  const [ticker, interval] = $("dataset-select").value.split("|");
  setStatus("Loading…");
  try {
    const [series, analysts, verdict, dividends, social] = await Promise.all([
      api(`/api/series/${encodeURIComponent(ticker)}?interval=${encodeURIComponent(interval)}`),
      api(`/api/analyze/${encodeURIComponent(ticker)}?interval=${encodeURIComponent(interval)}`).catch(() => []),
      api(`/api/verdict/${encodeURIComponent(ticker)}?interval=${encodeURIComponent(interval)}`).catch(() => null),
      api(`/api/dividends/${encodeURIComponent(ticker)}`).catch(() => null),
      api(`/api/social/${encodeURIComponent(ticker)}`).catch(() => null),
    ]);
    state.series = series;
    state.analysts = analysts;
    state.verdict = verdict;
    state.dividends = dividends;
    state.social = social;
    $("report-link").href = `/report/${encodeURIComponent(ticker)}?interval=${encodeURIComponent(interval)}`;
    setStatus("");
    renderAll();
  } catch (err) {
    setStatus(err.message, true);
  }
}

function setStatus(text, isError = false) {
  const status = $("status");
  status.textContent = text;
  status.classList.toggle("error", isError);
}

function showCards(show) {
  for (const id of ["price-card", "rsi-card", "signals-card", "table-card", "simulator-card"])
    $(id).hidden = !show;
  if (show) {
    $("sim-headline").hidden = true;
    $("sim-details").hidden = true;
  }
}

// ---------- rendering ----------

function visibleSlice() {
  const candles = state.series.candles;
  const n = state.range === "all" ? candles.length : Math.min(candles.length, state.range);
  return { from: candles.length - n, to: candles.length };
}

function columnByName(name) {
  const col = state.series.columns.find(c => c.name === name);
  return col ? col.values : null;
}

function renderAll() {
  showCards(true);
  renderVerdict();
  renderDividends();
  renderSocial();
  renderLegend();
  renderPriceChart();
  renderRsiChart();
  renderAnalysts();
  renderStances();
  renderTriggerTable();
  renderBarsTable();
}

function renderVerdict() {
  const card = $("verdict-card");
  const verdict = state.verdict;
  card.hidden = !verdict;
  if (!verdict) return;

  const headline = $("verdict-headline");
  headline.className = `verdict-headline ${verdict.direction.toLowerCase()}`;
  headline.textContent = `${DIR_ICON[verdict.direction]} ${verdict.headline}`;

  const list = $("verdict-details");
  list.replaceChildren();
  for (const detail of verdict.details) {
    const item = document.createElement("li");
    item.textContent = detail;
    list.appendChild(item);
  }
}

function renderScreener() {
  const card = $("screener-card");
  const rows = state.screen || [];
  card.hidden = rows.length < 2;
  if (card.hidden) return;

  const agents = rows[0].agents;
  const thead = $("screener-table").querySelector("thead");
  thead.replaceChildren();
  const headRow = document.createElement("tr");
  for (const text of ["Ticker", "Interval", ...agents.map(a => a.displayName)]) {
    const th = document.createElement("th");
    th.textContent = text;
    headRow.appendChild(th);
  }
  thead.appendChild(headRow);

  const tbody = $("screener-table").querySelector("tbody");
  tbody.replaceChildren();
  for (const row of rows) {
    const tr = document.createElement("tr");
    tr.style.cursor = "pointer";
    tr.addEventListener("click", () => {
      $("dataset-select").value = `${row.ticker}|${row.interval}`;
      loadSeries();
    });
    const tdTicker = document.createElement("td");
    tdTicker.textContent = row.ticker;
    const tdInterval = document.createElement("td");
    tdInterval.textContent = row.interval;
    tr.append(tdTicker, tdInterval);
    for (const agent of row.agents) {
      const td = document.createElement("td");
      td.className = `dir-${agent.direction.toLowerCase()}`;
      td.textContent = agent.direction === "Neutral"
        ? "▬ Neutral"
        : `${DIR_ICON[agent.direction]} ${agent.direction} ${Math.round(agent.confidence)}%`;
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
  }
}

function renderDividends() {
  const card = $("dividends-card");
  const dividends = state.dividends;
  card.hidden = !dividends || dividends.paymentCount === 0;
  if (card.hidden) return;

  const pct = v => v == null ? null : `${v >= 0 ? "+" : ""}${v.toFixed(1)}%`;
  $("dividend-headline").textContent =
    (dividends.yieldPercent != null ? `${dividends.yieldPercent.toFixed(1)}% yield` : "Yield n/a")
    + ` · ${fmt(dividends.trailingYearTotal, 4)} per share over the trailing year`
    + ` · ${dividends.paymentCount} payments on record`;

  const growthParts = [];
  if (pct(dividends.growthYoYPercent)) growthParts.push(`${pct(dividends.growthYoYPercent)} YoY`);
  if (pct(dividends.growth3YPercent)) growthParts.push(`${pct(dividends.growth3YPercent)}/yr over 3y`);
  if (pct(dividends.growth5YPercent)) growthParts.push(`${pct(dividends.growth5YPercent)}/yr over 5y`);
  $("dividend-growth").textContent =
    `Dividend growth: ${growthParts.length ? growthParts.join(" · ") : "not enough history"}`
    + ` · ${dividends.consecutiveGrowthYears} consecutive growth year(s)`;

  const annuals = $("dividend-annuals");
  annuals.replaceChildren();
  for (const annual of dividends.annualTotals) {
    const chip = document.createElement("span");
    chip.className = "year";
    const label = document.createElement("b");
    label.textContent = `${annual.year} `;
    chip.append(label, document.createTextNode(fmt(annual.total, 4)));
    annuals.appendChild(chip);
  }

  const tbody = $("dividend-table").querySelector("tbody");
  tbody.replaceChildren();
  for (const payment of [...dividends.recentPayments].reverse()) {
    const tr = document.createElement("tr");
    const tdDate = document.createElement("td");
    tdDate.textContent = fmtDate(payment.date);
    const tdAmount = document.createElement("td");
    tdAmount.className = "num";
    tdAmount.textContent = fmt(payment.amount, 4);
    tr.append(tdDate, tdAmount);
    tbody.appendChild(tr);
  }
}

function currentTicker() {
  return ($("dataset-select").value || "").split("|")[0];
}

async function runSimulation(event) {
  event.preventDefault();
  const ticker = currentTicker();
  if (!ticker) return;
  const button = $("sim-btn");
  button.disabled = true;
  try {
    const params = new URLSearchParams({ amount: $("sim-amount").value || "10000" });
    if ($("sim-start").value) params.set("start", $("sim-start").value);
    if ($("sim-end").value) params.set("end", $("sim-end").value);
    const r = await api(`/api/simulate/${encodeURIComponent(ticker)}?${params}`);

    const money = v => `$${v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    const pctSigned = v => `${v >= 0 ? "+" : ""}${v.toFixed(1)}%`;
    const headline = $("sim-headline");
    headline.hidden = false;
    headline.className = `sim-headline ${r.totalReturnPercent >= 0 ? "gain" : "loss"}`;
    headline.textContent =
      `${money(r.invested)} → ${money(r.endTotalValue)} (${pctSigned(r.totalReturnPercent)})`;

    const lines = [
      `Bought ${r.shares.toFixed(4)} shares on ${fmtDate(r.buyDate)} at ${money(r.buyPrice)}; held until ${fmtDate(r.sellDate)} at ${money(r.sellPrice)}.`,
      `Stock value ${money(r.endStockValue)} (${pctSigned(r.priceReturnPercent)}) + dividends collected ${money(r.dividendCash)} over ${r.dividendPayments} payment(s) (${pctSigned(r.dividendYieldOnCostPercent)} on cost).`,
      `If every dividend had been reinvested: ${money(r.reinvestedValue)} (${pctSigned(r.reinvestedReturnPercent)}).`,
    ];
    const list = $("sim-details");
    list.hidden = false;
    list.replaceChildren();
    for (const line of lines) {
      const item = document.createElement("li");
      item.textContent = line;
      list.appendChild(item);
    }
  } catch (err) {
    const headline = $("sim-headline");
    headline.hidden = false;
    headline.className = "sim-headline loss";
    headline.textContent = err.message;
    $("sim-details").hidden = true;
  } finally {
    button.disabled = false;
  }
}

function renderSocial() {
  const card = $("social-card");
  const social = state.social;
  card.hidden = !social;
  if (card.hidden) return;

  const headline = $("social-headline");
  if (social.postCount === 0) {
    headline.textContent = "No posts stored yet — pull the latest chatter to see what the crowd thinks.";
  } else {
    const ratio = social.bullishRatioPercent != null ? `${Math.round(social.bullishRatioPercent)}% bullish` : "no directional reads";
    headline.textContent =
      `${social.postCount} posts (${fmtDate(social.oldestPost)} – ${fmtDate(social.newestPost)}) · ${ratio}`
      + ` · tagged ${social.taggedBullish}▲/${social.taggedBearish}▼, lexicon ${social.lexiconBullish}▲/${social.lexiconBearish}▼`;
  }

  const container = $("social-posts");
  container.replaceChildren();
  for (const post of social.recentPosts || []) {
    const box = document.createElement("div");
    box.className = "social-post";
    const who = document.createElement("span");
    who.className = "who";
    who.textContent = `${fmtDate(post.date)} @${post.author} `;
    box.appendChild(who);
    if (post.tagged !== "Neutral") {
      const tag = document.createElement("span");
      tag.className = `tag ${post.tagged.toLowerCase()}`;
      tag.textContent = `[${post.tagged}] `;
      box.appendChild(tag);
    }
    box.appendChild(document.createTextNode(post.text));
    container.appendChild(box);
  }
}

async function pullSocial() {
  const ticker = currentTicker();
  if (!ticker) return;
  const button = $("social-btn");
  button.disabled = true;
  setStatus(`Pulling chatter for ${ticker}…`);
  try {
    const result = await api(`/api/social/${encodeURIComponent(ticker)}/refresh`, { method: "POST" });
    setStatus(`Pulled ${result.pulled} posts.`);
    state.social = await api(`/api/social/${encodeURIComponent(ticker)}`);
    renderSocial();
    renderAnalysts();
  } catch (err) {
    setStatus(err.message, true);
  } finally {
    button.disabled = false;
  }
}

function renderAnalysts() {
  const card = $("analysts-card");
  const container = $("analysts");
  container.replaceChildren();
  const analysts = state.analysts || [];
  card.hidden = analysts.length === 0;
  for (const agent of analysts) {
    const box = document.createElement("div");
    box.className = "analyst";

    const head = document.createElement("div");
    head.className = "head";
    const name = document.createElement("span");
    name.className = "name";
    name.textContent = agent.displayName;
    const verdict = document.createElement("span");
    verdict.className = `verdict ${agent.direction.toLowerCase()}`;
    verdict.textContent = `${DIR_ICON[agent.direction]} ${agent.direction}`;
    const conf = document.createElement("span");
    conf.className = "conf";
    conf.textContent = `${Math.round(agent.confidence)}% confidence`;
    head.append(name, verdict, conf);

    const reasoning = document.createElement("div");
    reasoning.className = "reasoning";
    reasoning.textContent = agent.reasoning;

    const details = document.createElement("details");
    const summary = document.createElement("summary");
    summary.textContent = "Scorecards";
    details.appendChild(summary);
    for (const score of agent.scores) {
      const line = document.createElement("div");
      line.className = "cardline";
      const sign = score.score > 0 ? "+" : "";
      line.textContent = `${score.title} [${sign}${score.score}/${score.maxScore}]`;
      details.appendChild(line);
      const list = document.createElement("ul");
      for (const detail of score.details) {
        const item = document.createElement("li");
        item.textContent = detail;
        list.appendChild(item);
      }
      details.appendChild(list);
    }

    box.append(head, reasoning, details);
    container.appendChild(box);
  }
}

function renderLegend() {
  const legend = $("overlay-legend");
  legend.replaceChildren();
  for (const overlay of OVERLAYS) {
    if (!columnByName(overlay.columns[0])) continue;
    const label = document.createElement("label");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = state.overlays[overlay.id];
    checkbox.addEventListener("change", () => {
      state.overlays[overlay.id] = checkbox.checked;
      renderPriceChart();
    });
    const key = document.createElement("span");
    key.className = "key";
    key.style.borderTopColor = `var(${overlay.cssVar})`;
    label.append(checkbox, key, document.createTextNode(overlay.label));
    legend.appendChild(label);
  }
}

function renderPriceChart() {
  const svg = $("price-chart");
  svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
  svg.replaceChildren();
  const { from, to } = visibleSlice();
  const candles = state.series.candles.slice(from, to);
  const count = candles.length;
  if (count === 0) return;

  $("price-title").textContent =
    `${state.series.ticker} · ${state.series.interval} · ${fmtDate(candles[0].t)} – ${fmtDate(candles[count - 1].t)}`;

  const activeOverlays = OVERLAYS.filter(o => state.overlays[o.id] && columnByName(o.columns[0]));

  // y-domain: candle range plus every visible overlay value.
  let lo = Infinity, hi = -Infinity;
  for (const c of candles) { lo = Math.min(lo, c.l); hi = Math.max(hi, c.h); }
  for (const overlay of activeOverlays)
    for (const name of overlay.columns) {
      const values = columnByName(name);
      if (!values) continue;
      for (let i = from; i < to; i++) {
        const v = values[i];
        if (v != null) { lo = Math.min(lo, v); hi = Math.max(hi, v); }
      }
    }
  const padY = (hi - lo) * 0.06 || 1;
  lo -= padY; hi += padY;

  const plotW = W - PAD.left - PAD.right, plotH = H - PAD.top - PAD.bottom;
  const x = i => PAD.left + ((i - from) + 0.5) * plotW / count;
  const y = v => PAD.top + (hi - v) * plotH / (hi - lo);
  const slotW = plotW / count;
  const bodyW = Math.max(1.5, Math.min(9, slotW * 0.65));

  // gridlines + y labels (recessive, labels outside right)
  for (const tick of niceTicks(lo, hi, 5)) {
    el("line", { x1: PAD.left, x2: W - PAD.right, y1: y(tick), y2: y(tick), class: "gridline" }, svg);
    const label = el("text", { x: W - PAD.right + 6, y: y(tick) + 4, class: "axis-label" }, svg);
    label.textContent = fmt(tick, tick >= 1000 ? 0 : 2);
  }
  // x labels: ~6 date ticks
  const step = Math.max(1, Math.floor(count / 6));
  for (let i = 0; i < count; i += step) {
    // Clamp so the first label isn't clipped by the left edge.
    const lx = Math.max(x(from + i), 36);
    const label = el("text", { x: lx, y: H - 8, "text-anchor": "middle", class: "axis-label" }, svg);
    label.textContent = fmtDate(candles[i].t);
  }

  // Bands first, under everything
  for (const overlay of activeOverlays.filter(o => o.band)) {
    const upper = columnByName(overlay.columns[0]), lower = columnByName(overlay.columns[1]);
    const up = pathFor(upper, from, to, x, y);
    const down = pathFor(lower, from, to, x, y, true);
    if (up && down)
      el("path", { d: `${up} L ${down.slice(2)} Z`, fill: `var(${overlay.fillVar})`, stroke: "none" }, svg);
    for (const name of overlay.columns.slice(0, 2)) {
      const d = pathFor(columnByName(name), from, to, x, y);
      if (d) el("path", { d, fill: "none", stroke: `var(${overlay.cssVar})`, "stroke-width": 1, "stroke-dasharray": "4 3" }, svg);
    }
  }

  // candles
  for (let i = from; i < to; i++) {
    const c = state.series.candles[i];
    const cls = c.c >= c.o ? "candle-up" : "candle-down";
    el("line", { x1: x(i), x2: x(i), y1: y(c.h), y2: y(c.l), class: cls, "stroke-width": 1 }, svg);
    const top = y(Math.max(c.o, c.c)), bottom = y(Math.min(c.o, c.c));
    el("rect", {
      x: x(i) - bodyW / 2, y: top, width: bodyW, height: Math.max(1, bottom - top),
      class: cls, rx: bodyW > 3 ? 1 : 0,
    }, svg);
  }

  // overlay lines (2px) or dot series + direct end-of-line labels (the relief rule)
  const usedLabelYs = [];
  for (const overlay of activeOverlays.filter(o => !o.band)) {
    const values = columnByName(overlay.columns[0]);
    if (overlay.dots) {
      for (let i = from; i < to; i++)
        if (values[i] != null)
          el("circle", { cx: x(i), cy: y(values[i]), r: 1.8, fill: `var(${overlay.cssVar})` }, svg);
    } else {
      const d = pathFor(values, from, to, x, y);
      if (d) el("path", { d, fill: "none", stroke: `var(${overlay.cssVar})`, "stroke-width": 2 }, svg);
    }
    let last = to - 1;
    while (last >= from && values[last] == null) last--;
    if (last >= from) {
      let ly = y(values[last]) + 4;
      while (usedLabelYs.some(used => Math.abs(used - ly) < 12)) ly += 12;
      usedLabelYs.push(ly);
      const label = el("text", {
        x: W - PAD.right + 6, y: ly, class: "series-label", fill: `var(${overlay.cssVar})`,
      }, svg);
      label.textContent = overlay.short ?? overlay.label;
    }
  }

  // trigger markers: bullish ▲ under the low, bearish ▼ above the high
  const perBar = {};
  for (const trigger of state.series.triggers) {
    const i = state.series.candles.findIndex(c => c.t === trigger.t);
    if (i < from || i >= to) continue;
    perBar[i] = perBar[i] || { bull: 0, bear: 0 };
    const c = state.series.candles[i];
    const size = 5;
    if (trigger.direction === "Bullish") {
      const cy = y(c.l) + 8 + perBar[i].bull++ * 11;
      el("path", {
        d: `M ${x(i)} ${cy} l ${size} ${size * 1.5} l ${-size * 2} 0 Z`,
        class: "marker-bullish",
      }, svg).append(titleNode(`${trigger.indicator} Bullish`));
    } else {
      const cy = y(c.h) - 8 - perBar[i].bear++ * 11;
      el("path", {
        d: `M ${x(i)} ${cy} l ${size} ${-size * 1.5} l ${-size * 2} 0 Z`,
        class: "marker-bearish",
      }, svg).append(titleNode(`${trigger.indicator} Bearish`));
    }
  }

  attachCrosshair(svg, { from, to, x, y, activeOverlays });
}

function titleNode(text) {
  const t = document.createElementNS(SVG_NS, "title");
  t.textContent = text;
  return t;
}

function pathFor(values, from, to, x, y, reverse = false) {
  if (!values) return null;
  const points = [];
  for (let i = from; i < to; i++)
    if (values[i] != null) points.push([x(i), y(values[i])]);
  if (points.length < 2) return null;
  if (reverse) points.reverse();
  return "M " + points.map(p => `${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(" L ");
}

function niceTicks(lo, hi, target) {
  const raw = (hi - lo) / target;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 2.5, 5, 10].map(m => m * mag).find(s => (hi - lo) / s <= target) || raw;
  const ticks = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi; v += step) ticks.push(v);
  return ticks;
}

// Crosshair finds the X; one tooltip lists every visible series at that bar.
function attachCrosshair(svg, ctx) {
  const { from, to, x, y, activeOverlays } = ctx;
  const hair = el("line", { y1: PAD.top, y2: H - PAD.bottom, class: "crosshair", visibility: "hidden" }, svg);
  const hit = el("rect", {
    x: PAD.left, y: PAD.top, width: W - PAD.left - PAD.right, height: H - PAD.top - PAD.bottom,
    fill: "transparent",
  }, svg);
  const tooltip = $("tooltip");

  const move = event => {
    const rect = svg.getBoundingClientRect();
    const px = (event.clientX - rect.left) * W / rect.width;
    const count = to - from;
    const i = Math.max(from, Math.min(to - 1,
      from + Math.floor((px - PAD.left) / ((W - PAD.left - PAD.right) / count))));
    hair.setAttribute("x1", x(i));
    hair.setAttribute("x2", x(i));
    hair.setAttribute("visibility", "visible");
    fillTooltip(i, activeOverlays);
    tooltip.hidden = false;
    const wrap = svg.parentElement.getBoundingClientRect();
    const tipX = (x(i) / W) * wrap.width;
    tooltip.style.left = `${Math.min(wrap.width - tooltip.offsetWidth - 8, Math.max(8, tipX + 14))}px`;
    tooltip.style.top = "12px";
  };
  hit.addEventListener("pointermove", move);
  hit.addEventListener("pointerleave", () => {
    hair.setAttribute("visibility", "hidden");
    tooltip.hidden = true;
  });
}

function fillTooltip(i, activeOverlays) {
  const tooltip = $("tooltip");
  const c = state.series.candles[i];
  tooltip.replaceChildren();
  const date = document.createElement("div");
  date.className = "tt-date";
  date.textContent = fmtDate(c.t);
  tooltip.appendChild(date);

  const table = document.createElement("table");
  const row = (name, value, colorVar) => {
    const tr = document.createElement("tr");
    const tdName = document.createElement("td");
    tdName.className = "tt-name";
    if (colorVar) {
      const key = document.createElement("span");
      key.className = "key";
      key.style.borderTopColor = `var(${colorVar})`;
      tdName.appendChild(key);
    }
    tdName.appendChild(document.createTextNode(name));
    const tdVal = document.createElement("td");
    tdVal.className = "tt-val";
    tdVal.textContent = value;
    tr.append(tdName, tdVal);
    table.appendChild(tr);
  };
  row("Open", fmt(c.o));
  row("High", fmt(c.h));
  row("Low", fmt(c.l));
  row("Close", fmt(c.c));
  row("Volume", c.v.toLocaleString("en-US"));
  for (const overlay of activeOverlays.filter(o => !o.band)) {
    const values = columnByName(overlay.columns[0]);
    row(overlay.label, fmt(values?.[i]), overlay.cssVar);
  }
  tooltip.appendChild(table);
}

function renderRsiChart() {
  const svg = $("rsi-chart");
  svg.setAttribute("viewBox", `0 0 ${W} ${RSI_H}`);
  svg.replaceChildren();
  const values = columnByName("RSI_14");
  if (!values) { $("rsi-card").hidden = true; return; }
  const { from, to } = visibleSlice();
  const count = to - from;
  const plotW = W - PAD.left - PAD.right;
  const x = i => PAD.left + ((i - from) + 0.5) * plotW / count;
  const y = v => 10 + (100 - v) * (RSI_H - 34) / 100;

  for (const guide of [30, 70]) {
    el("line", { x1: PAD.left, x2: W - PAD.right, y1: y(guide), y2: y(guide), class: "rsi-guide" }, svg);
    const label = el("text", { x: W - PAD.right + 6, y: y(guide) + 4, class: "axis-label" }, svg);
    label.textContent = guide;
  }
  const d = pathFor(values, from, to, x, y);
  if (d) el("path", { d, class: "rsi-line" }, svg);
}

const DIR_ICON = { Bullish: "▲", Bearish: "▼", Neutral: "▬" };

function renderStances() {
  const container = $("stances");
  container.replaceChildren();
  const { candles, stances } = state.series;
  for (const name of Object.keys(stances).sort()) {
    const series = stances[name];
    const current = series[series.length - 1];
    let since = series.length - 1;
    while (since > 0 && series[since - 1] === current) since--;

    const chip = document.createElement("span");
    chip.className = `stance ${current.toLowerCase()}`;
    const label = document.createElement("span");
    label.textContent = name;
    const dir = document.createElement("span");
    dir.className = "dir";
    dir.textContent = `${DIR_ICON[current]} ${current}`;
    const sinceEl = document.createElement("span");
    sinceEl.className = "since";
    sinceEl.textContent = `since ${fmtDate(candles[since].t)}`;
    chip.append(label, dir, sinceEl);
    container.appendChild(chip);
  }
}

function renderTriggerTable() {
  const tbody = $("trigger-table").querySelector("tbody");
  tbody.replaceChildren();
  const recent = [...state.series.triggers].reverse().slice(0, 30);
  for (const trigger of recent) {
    const tr = document.createElement("tr");
    const cells = [fmtDate(trigger.t), trigger.indicator, `${DIR_ICON[trigger.direction]} ${trigger.direction}`];
    cells.forEach((text, i) => {
      const td = document.createElement("td");
      td.textContent = text;
      if (i === 2) td.className = `dir-${trigger.direction.toLowerCase()}`;
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  }
}

function renderBarsTable() {
  const tbody = $("bars-table").querySelector("tbody");
  tbody.replaceChildren();
  for (const c of state.series.candles.slice(-15).reverse()) {
    const tr = document.createElement("tr");
    const cells = [fmtDate(c.t), fmt(c.o), fmt(c.h), fmt(c.l), fmt(c.c), c.v.toLocaleString("en-US")];
    cells.forEach((text, i) => {
      const td = document.createElement("td");
      td.textContent = text;
      if (i > 0) td.className = "num";
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  }
}

// ---------- wiring ----------

$("dataset-select").addEventListener("change", loadSeries);

$("range-presets").addEventListener("click", event => {
  const button = event.target.closest("button");
  if (!button) return;
  for (const b of $("range-presets").querySelectorAll("button")) b.classList.remove("selected");
  button.classList.add("selected");
  state.range = button.dataset.range === "all" ? "all" : Number(button.dataset.range);
  if (state.series) renderAll();
});

$("fetch-form").addEventListener("submit", async event => {
  event.preventDefault();
  const ticker = $("fetch-ticker").value.trim().toUpperCase();
  if (!ticker) return;
  const button = $("fetch-btn");
  button.disabled = true;
  setStatus(`Fetching ${ticker}…`);
  try {
    const result = await api("/api/fetch", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ticker, period: $("fetch-period").value, interval: "1d" }),
    });
    setStatus(`${result.ticker}: ${result.bars} bars saved`);
    $("fetch-ticker").value = "";
    await loadDatasets(result.ticker);
  } catch (err) {
    setStatus(err.message, true);
  } finally {
    button.disabled = false;
  }
});

$("simulator-form").addEventListener("submit", runSimulation);
$("social-btn").addEventListener("click", pullSocial);

$("refresh-btn").addEventListener("click", async () => {
  const button = $("refresh-btn");
  button.disabled = true;
  setStatus("Refreshing all datasets…");
  try {
    const result = await api("/api/refresh", { method: "POST" });
    const fetched = result.refreshed.filter(r => r.mode === "fetched").length;
    const derived = result.refreshed.length - fetched;
    let message = `Refreshed ${fetched} dataset(s)` + (derived ? `, re-derived ${derived}` : "");
    if (result.socialPulls?.length) message += `, pulled chatter for ${result.socialPulls.length} ticker(s)`;
    if (result.warnings.length) message += ` — ${result.warnings.length} failed: ${result.warnings[0]}`;
    setStatus(message, result.refreshed.length === 0 && result.warnings.length > 0);
    const selected = $("dataset-select").value;
    await loadDatasets();
    if ([...$("dataset-select").options].some(o => o.value === selected)) {
      $("dataset-select").value = selected;
      await loadSeries();
    }
  } catch (err) {
    setStatus(err.message, true);
  } finally {
    button.disabled = false;
  }
});

loadDatasets().catch(err => setStatus(err.message, true));
