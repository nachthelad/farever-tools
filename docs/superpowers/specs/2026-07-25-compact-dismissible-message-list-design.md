# Compact dismissible message list

## Goal

Make the Farever translation panel denser and faster to scan during gameplay.
Messages must use a flat list, show translations directly below their original
text, and allow the player to dismiss an irrelevant occurrence without hiding
future messages.

## Approved layout

The selected direction is the flat list from option B.

Each row uses two columns:

- The content column shows `sender · channel`, the original message, and the
  translated text when one exists.
- A narrow action column places `×` in the upper-right corner and `T` below it.

Rows use a subtle bottom divider instead of individual card backgrounds.
Untranslated rows do not reserve space for a translation or show a
“Sin traducir” placeholder. This keeps the normal row to two text lines plus
compact padding. A completed translation becomes a third line in green.

The `T` and `×` controls use fixed square footprints so their states never
change the row width. Tooltips expose the full labels “Traducir” and
“Descartar mensaje”. While a request is running, `T` changes to `…` and is
disabled. A translation error appears as a short red line below the message.

## Dismissal behavior

Clicking `×` immediately removes that row from the visible collection and adds
its `ChatMessageSnapshot.Identity` to an in-memory dismissed set.

Every reader reconciliation filters dismissed identities before rebuilding the
visible list. This prevents the same active history entry from returning on the
next 10 Hz sample, after garbage collection, or after a reader relocation.

Dismissals last only for the current host process. They are not written to
settings or logs. A future chat entry receives a different identity and appears
normally, even when the sender and text are identical.

The dismissed set may remain for the life of the session. This is bounded by
the number of chat entries seen during that session and avoids mistakenly
restoring a row after a temporary empty or invalid reader sample.

## Panel height

The window remains manually resizable and continues saving the chosen size.
Its minimum height is reduced from 220 to 150 device-independent pixels, and
the default for new settings is reduced from 280 to 200. Existing custom panel
heights remain unchanged except that future saves accept the lower minimum.

The panel does not automatically grow or shrink when translations arrive. This
avoids movement beside the game chat and leaves size control with the player.

## Components and data flow

`MainWindow.xaml` owns the visual template: a flat row, content column, compact
action column, dividers, tooltips, and fixed-size translation states.

`MessageRowViewModel` continues owning the translation, status, and busy state.
Its compact action label returns `T` normally and `…` while translating.

`MainWindow` owns a session-only dismissed identity set. `Dismiss_Click`
records the identity, removes the row, and refreshes the empty state.
`ReconcileMessages` excludes dismissed identities before updating
`Messages` and `knownRows`.

`AppSettings` and the WPF window constraints accept the smaller height. No
chat content, dismissal state, or translation content is persisted by this
change.

## Error and empty states

- Translation failures remain attached to their row and wrap below the
  original or translated text.
- Dismissing a row while it has a cached or completed translation discards only
  the visible row; the encrypted translation cache is unchanged.
- If the last row is dismissed, the existing waiting message becomes visible.
- Dismissal does not cancel or alter the reader, watchdog, F6 toggle, cursor
  behavior, or Google request policy.

## Verification

Automated checks cover:

- a dismissed identity is excluded from later reconciliations;
- a distinct identity with the same sender and text remains visible;
- dismissal state is session-only and is never serialized;
- the compact action label changes from `T` to `…` and disables during work;
- settings accept a 150-pixel panel height and still clamp smaller values;
- all existing reader, watchdog, Google, DPAPI, proxy, and installer tests
  continue to pass.

Manual verification in Farever confirms:

- untranslated rows use only two text lines;
- translations appear immediately below their originals;
- `×` is top-right and `T` remains directly below it;
- removing a row does not make it reappear on later samples;
- future messages from the same player still appear;
- the panel can be resized to the new minimum at 100%, 125%, and 150% DPI;
- the overlay remains click-through whenever Farever hides its cursor.

## Out of scope

This change does not add bulk clearing, persistent sender blocking, automatic
translation, message restoration, or automatic panel height changes.
