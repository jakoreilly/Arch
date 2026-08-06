---
name: arch-site-design
description: The design system for the HTML sites Arch generates — the token palette, light/dark theming, the component vocabulary (panel, tile, badge, grid, note), accessibility baseline, and the rule that every colour comes from a token. Use before adding or changing any generated markup or styling in this repo: a new page under Site/Pages, a new component, a colour, a chart, or an edit to site.css.
allowed-tools: Bash, PowerShell, Read, Edit, Grep, Glob
---

# Arch's generated-site design system

Arch generates a static, **fully offline** site — no CDN, no network, opened straight
from `file://`. Everything is one shared stylesheet plus vendored libs.

| What | Where |
|---|---|
| The stylesheet — **one sheet, all three sites** | `src/Arch.Core/Web/assets/site.css` |
| Shared behaviour (search palette, theme toggle, diagram pan/zoom) | `src/Arch.Core/Web/assets/site.js` |
| The page shell (head, sidebar, nav, breadcrumbs) | `src/Arch.Core/Web/PageShell.cs` |
| Per-product shell wrappers | `src/Arch.Code/Site/PageTemplate.cs`, `src/Arch.Sql/Site/PageTemplate.cs`, `src/Arch.Cli/HubPage.cs` |
| Asset merge (`assets/` + `assets-code/` / `assets-sql/`) | `src/Arch.Core/Web/SiteAssets.cs` |

**`site.css` is shared by the code site, the SQL site and the hub.** A change to it lands
on every page of all three. That is the point — but it means a "small tweak" has a wide
blast radius, and it always shows up in `tools/golden.sh`.

## Any change here changes the golden output

`assets/site.css` is copied into the generated site, so editing it makes `golden.sh`
report a diff — in both `golden/code` and `golden/sql`. That is expected, not a problem.
Follow the review protocol in the **arch-verify** skill: confirm the changed-file list is
only what you expect, read every removed line, then accept.

## Colour: always a token, never a literal

The palette is defined twice — `:root, :root[data-theme="light"]` and
`:root[data-theme="dark"]` — and every component styles through the tokens, never inside
a theme block. Adding a colour means adding a token to **both** themes.

| Token | Role |
|---|---|
| `--bg` / `--bg-panel` / `--bg-sunken` | page ground / raised card / recessed strip (table headers, toolbars) |
| `--border` | every hairline |
| `--text` / `--text-soft` | body / secondary and label text |
| `--accent` / `--accent-soft` | the one brand blue, and its tinted fill |
| `--ok` / `--warn` / `--danger` | semantic hues, **separate from the accent** |
| `--*-ink` | the same semantic meaning as **text on that hue's own tint** — see below |
| `--shadow`, `--diagram-bg` | elevation, and the diagram canvas ground |

C# page code may reference tokens inline — `style="border-color:var(--warn)"`,
`fill="var(--ok)"` — and that is the correct way to colour a chart mark or an SVG. What
it must **never** do is emit a raw hex value; that breaks the dark theme silently, since
nothing re-themes a literal.

### Fills and ink are different jobs

`--ok`/`--warn`/`--danger` are used two ways, and one value cannot serve both:

- **fills, borders, chart marks, legend dots** — the vivid hue *is* the signal
- **text on a low-opacity tint of that same hue** (`.badge.warn` and friends) — needs far
  more contrast than the vivid hue has

So each has an `-ink` variant, used **only** for text-on-tint. Light theme darkens,
dark theme lightens. All four are defined in both themes so components can name the ink
token unconditionally. Every pair clears WCAG AA 4.5:1 on all three grounds a badge can
land on; the base hues did not (warn was 2.75:1). Use the `contrast-check` skill before
changing any of these values.

## Theming

Set by a **pre-paint inline script** so there is no flash — it reads `localStorage`
key `archdiagram-theme`, falls back to `prefers-color-scheme`, and stamps `data-theme` on
the root element. That script is duplicated verbatim in **three** places (both
`PageTemplate.cs` files and `HubPage.cs`) — deliberately, because ArchDiagram's copy also
carries the `hide-tests` tail and a leading comment that ArchSql's lacks, and unifying
them would change the other product's bytes. If you change the theming logic, change all
three.

The theme key is `archdiagram-theme` and stays that way — renaming it silently resets
every existing user's preference.

Always check a change in **both** themes. `--diagram-bg` exists because mermaid re-renders
with its own dark theme and the canvas has to follow.

## Component vocabulary — reuse before inventing

Reach for these before writing new CSS. Most pages need nothing new:

| Class | Use |
|---|---|
| `.panel` | a bordered, elevated card — the default container |
| `.tiles` / `.tile` (`.tile .num` + `.tile .lbl`) | the KPI row at the top of a page |
| `.badge` (+ `.accent` / `.ok` / `.warn` / `.danger`) | inline status chip |
| `table.grid` (+ `.sortable`) | every data table |
| `.note` | an inline caveat or explanation |
| `.lede` | the one-paragraph summary under an `h1` (capped at 72ch) |
| `.diagram-card` + `.toolbar` + `.stage` | a pannable/zoomable diagram |
| `.empty-state` | nothing to show |
| `.heat-grid` / `.heat-tile` | dense label-sized tiles |

`src/Arch.Code/Site/Severity.cs` is the model to copy: it maps severity onto the existing
badge classes and adds no CSS at all.

Summary before detail — a page opens with a `.tiles` row, then diagrams, then tables.

## Accessibility baseline (already in the sheet — don't regress it)

- **Keyboard focus** — one `:focus-visible` ring covers every focusable element. It uses
  `:where(...)` so it has zero specificity and any component can override it. Do not add
  `outline: none` without providing a replacement ring.
- **Reduced motion** — a `prefers-reduced-motion: reduce` block flattens every transition.
  New transitions are covered automatically; don't add an animation that escapes it.
- **Contrast** — see the ink tokens above. Measure with `contrast-check`, don't estimate.
- **Digits** — `font-variant-numeric: tabular-nums` on `table.grid` cells, `.tile .num` and
  `.heat-count`, because these are columns of counts read vertically.
- **Print** — the `@media print` block **re-declares the tokens** for white paper. Without
  it, printing while the dark theme is active sends near-white text to the printer. Style
  through tokens and print keeps working for free; hardcode a colour and it won't.

## Wide content must not scroll the page

Tables, diagrams and matrices get `overflow-x: auto` on their own container — see
`ModulesPage.cs`'s coupling matrix, which wraps itself in one. On narrow viewports
`table.grid` becomes its own scroll region.

## Layout

`.layout` is a flex row: a 230px sticky sidebar plus `.content` capped at 1500px. Below
820px the sidebar becomes an off-canvas drawer behind `.nav-toggle`. Space siblings with
flex/grid `gap`, not per-element margins.
