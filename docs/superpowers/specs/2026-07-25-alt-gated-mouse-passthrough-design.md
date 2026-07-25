# Alt-gated mouse pass-through

## Goal

The translation panel must never reveal the cursor or interrupt camera movement
while Farever is in its normal camera mode. The overlay starts completely
click-through and becomes interactive only after the player presses and
releases Alt by itself. A second valid Alt press returns it to click-through.

## Alt state

A testable `AltInteractionToggle` samples keyboard state every 20 milliseconds.
It begins passive and waits until Alt has first been observed released, so a
key already held during host startup cannot enable interaction.

A valid Alt press is committed on key release. Holding the key cannot repeat
the action. Alt with Tab, F4, Enter, Shift, or Control is cancelled; this also
excludes AltGr. Ordinary movement keys do not cancel Alt.

The state resets to passive when Farever loses foreground, is minimized, is no
longer in a ready world, or its usable game window disappears. F6 changes panel
visibility without changing the Alt state while the current world and focus
remain valid.

## Native window safety

Passive mode combines two independent barriers:

- the top-level window receives `WS_EX_TRANSPARENT`;
- WPF hit testing is disabled for the whole window.

Interactive mode removes only those barriers. `WS_EX_NOACTIVATE` and
`WS_EX_TOOLWINDOW` remain present for the complete window lifetime. The window
hook returns `MA_NOACTIVATE` for `WM_MOUSEACTIVATE`, allowing intentional
button clicks without foregrounding the translator.

Native style changes are idempotent, refresh the non-client frame, and verify
the resulting extended style. If passive mode cannot be confirmed, the host
sets a safety fault, hides the panel, and logs `mouse_passthrough_failed`.
It never leaves a visible overlay intercepting Farever after a native failure.

## Interaction and calibration

`SourceInitialized` installs the window hook and passive mode before the panel
can become visible. `Activate()`, `Focus()`, and temporary removal of
`WS_EX_NOACTIVATE` are removed from calibration.

The header and resize grip initiate native non-client move and bottom-right
resize operations, respectively. They work only in calibration mode and only
after Alt enabled interaction. The calibration hint becomes:
“Presioná Alt, arrastrá desde el título y tocá Listo.”

Translation, dismissal, API-key configuration, calibration, scrolling,
dragging, and resizing all require interactive mode. The panel hides on focus
loss even when calibration is active.

## Failure handling

Keyboard polling never records key presses in logs. Only a native pass-through
failure is logged, without chat or credential content. Closing the API-key
dialog and returning to Farever follows the same focus-reset rule and therefore
returns passive.

## Verification

Automated tests cover:

- initial release gating, valid Alt edges, holding, and the second toggle;
- movement keys during Alt;
- Tab, F4, Enter, Shift, Control, and AltGr cancellation;
- foreground, minimization, and world-ready resets;
- preservation of `WS_EX_NOACTIVATE` across passive/interactive transitions;
- confirmed `WS_EX_TRANSPARENT` changes and fail-safe behavior;
- all existing reader, translation, proxy, and install-round-trip tests.

Manual Farever acceptance covers:

- uninterrupted camera movement across every overlay pixel while passive;
- no cursor, hover, tooltip, scrolling, click, drag, or resize before Alt;
- all controls work after Alt without Farever losing foreground;
- the second Alt restores pass-through immediately;
- F6, Alt+Tab, API-key dialog, calibration, loading, and zone changes;
- 100%, 125%, and 150% DPI;
- reversible installation with matching manifest, minimap, and backup hashes.

## Scope

The host CLI, persisted settings, Google API behavior, F6, HashLink reader, and
installer format remain unchanged. Public GitHub distribution is out of scope.
