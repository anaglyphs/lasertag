# Menu ownership and copy

See [Main menu structure](Main%20Menu/README.md) for controller composition, shared forms, and binding lifetimes.

- **Gate** owns permission access, unsupported-device acknowledgement, and the operator password gate.
- **Connection** owns discovery, hosting, joining, disconnecting, network/Bluetooth warnings, and alignment progress during connection.
- **Settings** owns headset graphics, diagnostics, and operator provisioning. The debug submenu intentionally includes host-only mesh visibility controls that affect everyone.
- **Game** owns maps, matches, editing, alignment settings, and map/alignment errors.

## Editing words

All runtime UXML documents use Unity Localization. Open **Window > Asset Management > Localization Tables**, then select `GateMenu`, `ConnectionMenu`, `SettingsMenu`, `GameMenu`, `HUDMenu`, `OperatorMenu`, or `PaletteMenu`. English is the initial locale. The collections live in `Assets/Anaglyph/LaserTag/Localization`.

Static text binds to an entry in UXML. Edit the table entry rather than adding a second `text`, `label`, or `title` value. Keys are stable identifiers: do not rename them when revising the sentence or rearranging the page. Custom `NavHeader` bindings target its `Title` property.

Dynamic text uses Smart Strings. `SettingsHomePage.uxml` contains a complete named-variable example: the `version` entry is `Version: {version}`, the UXML declares the variable, and `SettingsMenu` supplies its value through `MenuCopy.SetVariable`. Bindings update when the variable or selected locale changes.

State-dependent text (discovery, map actions, permission results, alignment status) is selected by key through `MenuCopy`. Formatted entries such as `maps.current` and `maps.aligning` use positional Smart String arguments; translators can reorder them. Keep complete sentences in entries instead of concatenating sentence fragments in C#.

Argument meanings:

| Entry | Arguments |
| --- | --- |
| `version` | named `version`: game version |
| `session.relay` | 0: host's room code |
| `error.build-details` | 0: host version, 1: local version |
| `provisioning.host`, `provisioning.host-locked` | 0: configured host address |
| `maps.current`, `maps.aligning` | 0: map name |
| `maps.row*` | 0: map name, 1: localized age, 2: tag count |
| `age.minutes`, `age.hours`, `age.days` | 0: elapsed units |
| `alignment.tag-count`, `alignment.tag-blocked` | 0: tag count; 1: blocker for the blocked variant |
| `alignment.saved-blocked` | 0: blocker |
| `match.round` | 0: current round, 1: total rounds |
| `error.alignment-details` | 0: host-provided rejection message |

Map policy returns stable `blocker.*` keys (or null when allowed). The coordinator resolves those keys for presentation. Log-only diagnostics and programmer exceptions remain in C#.

## Messages and errors

Do not subscribe a panel to every `UserErrors` event. `MenuErrorPresenter` filters by the explicit `UserErrorArea`, queues distinct messages, suppresses duplicates while pending, and retains the queue when its visual tree is rebound. Normal producers use `RaiseLocalized`; raw `Raise` is available for external messages.

The Connection panel owns Bluetooth and network availability warnings. Its home page has one contextual internet warning for internet hosting. Alignment settings show a shared blocker once rather than repeating it beside every disabled control. A tag count may remain visible because it conveys different information.

Alignment rejection details currently travel as rendered text from the host. Their source copy is in `GameMenu`, but the received detail keeps the host's language. Changing that requires a versioned network message carrying a reason key and arguments. Do not silently change the existing wire contract as a copy edit.

## Localization and verification

The Localization package brings Addressables. English tables are preloaded, and initialization is synchronous for the project's native desktop/Quest targets so early controller lookups have their text. Build Addressables content when making a player build. Adding other languages requires translations and font coverage; there is no runtime language picker in this pass. Use Localization Scene Controls for editor previews, or `LocalizationSettings.SelectedLocale` for runtime switching.

`MenuPresentationTests` checks UXML bindings, named-variable updates, pseudo-locale refresh, radio-choice behavior, error ownership, queue preservation, and dismissal. The existing map workflow tests also check that copy keys do not change action eligibility. Editor checks do not establish Quest interaction, clean-install permission behavior, or multiplayer timing.

## Alignment selection

During a session, the alignment dropdown shows the committed session method. A selection requests that exact method; it does not silently substitute a compatible fallback. The host checks capability and prepares references before saving the preference and committing the switch. A failed request leaves the previous preference and method intact, with a Game-panel warning modal. Rejections are scoped to the requesting client, request ID, and map, so delayed replies for older requests are ignored.

Offline, the dropdown edits the map preference. During a session, a differing map preference is shown separately. Tag controls follow the displayed method. A shared-anchor map can explicitly open Set up AprilTags to register references before switching. Automatic fallback uses the same capability-aware method selection as session startup and does not raise request-failure dialogs. Hosts that cannot share anchors start blank or tagged maps in AprilTag mode; existing tagless maps retain their reference frame.

The alignment request/rejection payloads include request IDs; multiplayer peers must use matching protocol versions.


## HUD, operator, and palette copy

| Collection | Coverage and dynamic arguments |
| --- | --- |
| HUDMenu | Head HUD prompts, countdown Go, respawn and results; shared ScoreDisplay in HandHUD. `respawn.countdown`: 0 = seconds; `match.round`: 0 = current round, 1 = total. The countdown is a plain Label and never flashes. |
| OperatorMenu | Operator buttons, tabs, headings, list column titles, session/configuration messages, host errors and telemetry. `client.count` uses plural forms; `alignment.count`: 0 = agreeing references, 1 = total; address/client/score/battery entries take the displayed value as argument 0. `error.host-start`: 0 = external exception detail. |
| PaletteMenu | Palette heading and empty states, plus existing database category and object captions. |

Column titles are assigned by OperatorMenu because UI Toolkit Columns are not VisualElements and cannot use the text-element binding pattern. They refresh on locale changes. Native UXML bindings handle the tabs and ordinary controls. Host/configuration failures retain a LocalizedString reference until dismissed/replaced, so changing locale preserves the message and its arguments.

Each MapObjectDatabase category and entry has a Localized Name reference. Edit its table entry for translated display copy. Its legacy name is a fallback for newly authored entries without an assigned reference. Existing references use stable table entry IDs and survive reordering; assign a new reference for new content.

Audit: all 29 UXML documents under Assets were checked; no nonnumeric text/label/title/tooltip literals remain. Controller-authored prose was converted in HUD, ScoreDisplay, OperatorMenu, OperatorHost, HeadsetConfiguration, and PaletteMenu. Shared main-menu templates retain their existing tables. Numbers, clock formatting, team/client IDs, IPs, room codes, user map names, and runtime transport type names remain data. Console logs, programmer exceptions, Editor tools, and the separate TMP-based Mavrik calibration diagnostic are outside this UXML pass. External exception text and already-rendered network rejection details retain their source language.

The new tables are English-only and preloaded through Addressables. Pseudo-locale tests cover native HUD/operator bindings, plural counts, respawn formatting, and retained error messages. A locale change invalidates HUD/ScoreDisplay caches and refreshes the operator and palette without changing gameplay state.
