# Arch — the idiot's guide

**You point Arch at a folder. It reads what's in there and builds you a website that
explains it.** Nothing is uploaded, nothing is changed, nothing needs the internet.

You do not need to understand the code in the folder. That is rather the point.

---

## Before you start (once)

You need the **.NET SDK, version 10 or newer**. To find out whether you already have it,
open a terminal and type:

```
dotnet --version
```

If that prints something like `10.0.300`, you're set. If it says the command isn't
recognised, install it from <https://dotnet.microsoft.com/download>, then close and
reopen the terminal.

---

## Step 1 — build it (once)

**Double-click `build.cmd`.**

It takes about a minute the first time and prints `Build OK` when it's finished. If it
fails, the message tells you what to do — the usual cause is a file being locked by an
open editor, in which case close it and double-click again.

You only ever do this again after the code changes.

## Step 2 — run it (every time)

**Drag the folder you want to understand onto `run.cmd`.**

Or double-click `run.cmd` and paste a folder path when it asks. (Press Enter without
typing anything and it analyses Arch itself, which is a fine way to see what the output
looks like.)

A browser tab opens by itself when it's done. Small folders take a second or two; large
ones take under a minute.

## Step 3 — read it

The tab that opens is the **Overview**. Start there, then use the menu down the left.

If you only look at three pages, look at these:

| Page | What it answers |
|---|---|
| **Overview** | How big is this, what's it made of, and is it healthy? |
| **Guide** | What every other page means. Written for a newcomer. |
| **Structure** | What's actually in the folder, arranged as a tree. |

Everything is clickable. Diagrams pan and zoom, and can be saved as PNG. `Ctrl+K` opens a
search box.

---

## Where did it put the site?

In a new folder next to wherever you ran it, named after what you analysed —
`site-MyProject`. Inside is an `index.html`; that's the page that opened.

The whole folder is self-contained. You can **zip it and email it**, put it on a shared
drive, or open it on a machine with no internet. It'll work. There's nothing to install
at the other end.

## Five things people ask

**Did it change my code?**
No. Both analysers only ever read. Nothing is written anywhere except the output folder.

**It said "Nothing to analyze here."**
Arch looks for source files (C#, TypeScript, Python, Go, Java, Rust, …) and SQL scripts
(`*.sql`). That folder had neither. The usual cause is pointing one level too high or too
low — check you picked the folder that actually contains the project.

**Two links appeared instead of the report.**
Your folder has *both* code and SQL in it, so Arch built a site for each and gave you a
landing page with a card per site. Click either card. If the code connects to a database
that the SQL side also covers, that landing page tells you so.

**Can I send this to someone?**
Yes — see above. It's just files. Nothing phones home, and passwords are never written
into the output (connection strings are reported by shape only, never with credentials).

**Do I have to re-run it when the code changes?**
Yes. The site is a snapshot of the moment you ran it. Run it again and it overwrites.

**Something went wrong and I want the details.**
The window that `run.cmd` opens stays put and prints the reason. Read the last few lines
before pressing a key.

---

## Next

That's everything you need for normal use. When you want to point Arch at a live SQL
Server, run it in a build pipeline, or change what it includes, read
[HOW-TO-USE.md](HOW-TO-USE.md).
