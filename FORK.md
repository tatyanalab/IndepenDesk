# About this fork

This is a fork of [harungecit/IndepenDesk](https://github.com/harungecit/IndepenDesk) (MIT).
The upstream idea — independent virtual desktops per monitor, macOS-Spaces style — is kept
as is. All changes here are in the **Overview panel** (`Ctrl+Alt+↑`) and in how desktops are
managed from it, plus a few behaviour fixes found along the way.

Forked from upstream **v0.4.0**; this fork is versioned **0.4.1**.

## Why

The Overview panel was the weak spot: it could show desktops but barely let you reorganise
them. Dragging a desktop card did not visibly work, there was no way to remove a desktop at
all, and cards hid part of their window list behind a dead "… and N more windows" label.

## What changed

**Desktops can be reordered.** Drag a card title onto another card: the left half inserts it
before that card, the right half after. Inside one monitor this is a pure reorder — windows
stay where they are, only the order and the global numbers change; the active desktop is
tracked by reference so the wrong windows are never hidden. Across monitors the desktop moves
with all its windows, now to the dropped position instead of always to the end.

*Why it did not work before:* the only drop target for a desktop was the monitor row, but the
row is completely covered by its child flow panel, which had no `AllowDrop`, and the cards
themselves accepted only windows. In practice you could only drop into the 4px padding at the
row's edge. Cards now accept desktops directly, and dragging starts only after the system drag
threshold, so a plain click no longer begins a drag.

**Desktops can be closed.** Each card has a ✕. Its windows are never lost — they move to the
neighbouring desktop (the previous one, or the one after it when closing the first), and the
tooltip names the exact target desktop. The last desktop of a monitor has no ✕, since its
windows would have nowhere to go. Upstream had no delete operation at all: desktops could only
disappear on their own when an empty one happened to be last in the row.

**Cards show every window.** The list scrolls inside the card and the row's height is derived
from the busiest desktop of that monitor, so nothing hides behind a counter. The collapse
toggle is gone. Window titles use proper ellipsis with the full title in the tooltip, and the
list is sorted by title — the underlying set has no order, so the list used to be reshuffled
on every refresh.

**Clicking a window jumps to it.** It switches to the desktop holding that window, restores it
if minimised, and raises it. The context menu's "Go to this window" did the same thing badly:
it only raised the window, which is a no-op while the window is hidden on an inactive desktop.

**A window dropped on a desktop arrives open.** Windows are shown in whatever state they had,
so a minimised one stayed minimised. Windows moved by hand are now restored when that desktop
becomes active — only the ones you moved, not the ones you minimised yourself.

**Dropping a window on "+" creates a desktop for it.** The new desktop holds that window and
is not switched to.

**Switch animation is configurable** via the tray menu: *None* (the new default), *Fade*, or
*Slide*. The upstream slide effect only moved the outgoing screenshot, which reads as a curtain
rather than a transition, and it rebuilt the clipping region ~23 times per run without
releasing the previous one. Fade is a new mode; Slide is the old one with the region leak and
the timer interval fixed.

**Overview closes on click.** Clicking empty space in the panel closes it, next to `Esc`.
See the note below for why a gesture cannot do this.

## Notes on Windows behaviour

Measured while working on this, both worth knowing:

- **While the Overview panel is in the foreground, Windows stops delivering the keyboard
  shortcuts assigned to touchpad gestures.** With the panel closed, a four-finger swipe up
  reached the app 6 times out of 6; with the panel open, not once. The panel is borderless,
  topmost and nearly fullscreen, which is likely why the system suppresses gesture shortcuts.
  So the panel cannot be closed by a gesture, whatever direction or finger count — hence
  click-to-close. Keyboard shortcuts are unaffected.
- **A four-finger downward swipe never sends its custom shortcut**; Windows keeps that
  direction for "show desktop". The registry held the same kind of custom-shortcut entry for
  all four directions, and the other three worked. `Ctrl+Alt+↓` and `Ctrl+Alt+Shift+↓` are
  still registered by the app, so they work from the keyboard.

## Building

Needs the .NET 8 SDK (`net8.0-windows`, WinForms).

```
dotnet publish -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish\x64
```

## Not changed

The desktop engine (window ownership, hiding and restoring, persistence, recovery), hotkey
registration, tray, OSD, update check and packaging are upstream's. Strings for new UI were
added in English and Russian; the other six languages fall back to English for those keys.
