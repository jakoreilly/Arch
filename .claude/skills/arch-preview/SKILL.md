---
name: arch-preview
description: Generate an Arch site and actually look at it — run the CLI against a real folder, open it in a browser, and take headless screenshots in both light and dark themes. Use when asked to run the app, preview or screenshot the site, or to confirm a styling, layout or rendering change looks right rather than merely passing tests.
allowed-tools: Bash, PowerShell, Read, Edit, Glob, Grep
---

# Previewing a generated site

`golden.sh` proves the bytes didn't change. It says nothing about whether the page
*looks* right. For any visual change, generate a site and look at it.

## Generate

Output goes under `work/` — gitignored, so it never pollutes the repo or the golden tree.

```bash
dotnet build Arch.slnx --nologo -v q
rm -rf work/demo
dotnet run --project src/Arch.Cli --no-build -- . --out work/demo --no-open
```

**Point it at this repo itself (`.`).** Arch contains both C# and `.sql` fixtures, so it
exercises **combined mode** — hub page + `code/` + `sql/` — on ~290 real files with real
diagrams, badges and a densely populated coupling matrix. The test fixtures are far too
small to show a layout problem.

Then open it:

```bash
cmd.exe /c start "" "$(pwd -W)/work/demo/index.html"
```

Dropping `--no-open` makes the CLI open a tab itself — that path is worth exercising
occasionally, since it is real product behaviour.

## Screenshots

There is no `chromium-cli` here. Edge is Chromium and does the job:

```bash
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
WIN="$(pwd -W)"
"$EDGE" --headless=new --disable-gpu --hide-scrollbars \
        --virtual-time-budget=6000 --window-size=1500,1150 \
        --screenshot="$WIN/work/shots/page.png" \
        "file:///$WIN/work/demo/code/scorecard.html"
```

`--virtual-time-budget` is not optional — mermaid renders asynchronously, and without it
you screenshot an empty diagram stage.

**Then actually read the PNG.** A screenshot you don't open proves nothing.

## Four traps, all hit in practice

**Headless reports `prefers-color-scheme: dark` here.** The pre-paint script honours it,
so an unmodified screenshot gives you the *dark* theme — easy to mistake for the default.
Force a theme explicitly whenever the theme matters.

**Forcing a theme needs a script, not a flag.** There is no Chromium switch for
`prefers-color-scheme`. Inject a setter *after* the page's own pre-paint script so it
wins, into a **scratch copy** under `work/` — never the generated output:

```bash
sed "s|</head>|<script>document.documentElement.setAttribute('data-theme','light')</script></head>|" \
    work/demo/code/modules.html > work/demo/_scratch.html
```

**`--screenshot` captures the viewport only** — there is no full-page flag. Content below
the fold (the coupling matrix sits under a 66vh diagram stage) will not appear. Don't
just grow `--window-size`: `.stage` is sized in `vh`, so a taller window grows the
diagram too and pushes the target further down. Hide what's above it instead:

```css
.diagram-card,.metrics-scatter,.tiles,.lede,.sidebar,.breadcrumbs{display:none!important}
```

**Watch the `sed` delimiter.** CSS selectors and injected markup are full of `/` and `#`.
Use `|`, or `s|...|...|` fails with a baffling "unknown option to `s'".

## What to look at

Check **both themes** — the palette is defined twice and only one half is on screen at a
time. Worth a look every time:

| Page | Why |
|---|---|
| `code/scorecard.html` | every badge state (pass / watch / fail / n/a) in one table |
| `code/modules.html` | the coupling-matrix heat ramp — text over a tinted cell |
| `code/metrics.html` | the scatter plot and formula cards |
| `code/index.html` | tiles, language bar, the standard page opening |
| `index.html` (hub) | combined mode, and the no-search shell degradation |

Colour problems are measured, not eyeballed — use the `contrast-check` skill.

## Clean up

Delete scratch copies when done; leave `work/` itself alone (gitignored, and the golden
harness uses `work/golden`).
