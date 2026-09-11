# Main menu structure

`Menu.prefab` composes the headset's Gate, Connection, Game, and Settings panels. Each
panel has a `UIDocument`, `UIToolkitPanelXRSetup`, and a menu controller. Its root UXML
declares navigation pages and instantiates smaller page documents.

## Ownership

| Owner | Responsibility |
| --- | --- |
| `MainMenuController` | Menu input, headset-relative positioning, panel layout, opening/closing animation. |
| `GateMenu` | Permission/password access, limited-support acknowledgement, and the single decision about panel visibility. |
| `SessionConnectionController` | Discovery activity and automatic pinned-host connection/retry policy. |
| `ConnectionMenu` | Connection forms, discovery/session/alignment progress, and connectivity warnings. |
| `ConnectionConnectivityMonitor` | Connectivity polling, Bluetooth state, and Android settings launch. It reports state; the menu chooses presentation. |
| `GameMenu` | Match/editing navigation, user actions, and composition of shared game binders. |
| `SettingsMenu` | Graphics, diagnostics, console visibility, and operator provisioning controls. |
| `MenuErrorPresenter` | An area-specific error queue that survives hiding and rebinding. |

Game policies remain in `LaserTagMapCoordinator`, `MatchReferee`, and related gameplay
systems. The menus submit requests and display eligibility/blocker messages.

## Shared forms and platform pages

XR and Operator own their page layouts. The headset's map pages live in `Game/`;
`OperatorMapsPage` and `OperatorMapSettingsPage` live beside `OperatorMenu`. The
operator settings page combines name and alignment in one scrollable form, while
XR retains nested settings navigation and physical setup tools.

`../Shared/Game` contains the reusable sections and their binders:

- `MapCatalog` / `MapPickerBinder`: saved-map selection, creation, loading, and deletion.
- `MapNameField` / `MapNameBinder`: current-map naming and its blocker message.
- `AlignmentMethodField` / `AlignmentMethodBinder`: alignment choice, active method,
  saved preference, and status. It requires no tag controls or navigation.
- `TagConfiguration` / `TagConfigurationBinder`: manual size, count, and removal.
- `MatchSettingsBinder`: match form values and callbacks.

A binder receives its section root, so field names are local to that instance.
Page controllers choose the sections to instantiate, coordinate presentation, call
`Refresh`, and dispose their bindings. See `../Shared/Game/README.md` for the reuse
contract. Gameplay requests still go to the existing coordinator.

`MapManagerUI` retains the prefab's operator-mode setting and picker instance; it
supplies the catalog filter and empty-state copy. Game and Operator explicitly call
`Bind` after preparing their buttons and `Unbind` when disabled.

Headset-only `MapProbeBinder` owns probing. `MapEditingMenuBinder` composes the XR
editor's navigation and shared fields; `TagRegistrationBinder` owns registration,
measurement, and physical tool presentation. Operator instantiates none of those
controls or binders. It can administer tag data without entering XR editing mode.

`../Shared/MenuErrorPage.uxml` is used by Connection, Game, and the operator map panel.
Shared sections retain their localization keys when filenames or locations change.

## Binding and visibility lifetimes

Closing a panel hides it through `UIToolkitPanelXRSetup`; it does not disable the
document or controller. Logical visibility and input can end before a closing
animation finishes rendering. Gate remains the single owner of those decisions.

Controllers bind the current visual tree on enable and remove UI callbacks on disable.
`Anaglyph.Menu.UIEventBindings` pairs anonymous button/value handlers with cleanup;
named callbacks are explicitly unregistered. Match and editing binders are disposed
by their owner. `UIQuery.Require<T>` reports missing named controls consistently.

The map picker preserves selection across bindings. The headset probe binder cancels
its pending operation on disposal; an old completion cannot update a replacement tree. Connection polling
and the operator's refresh loop also end with their enabled lifetime. Error queues
have a longer lifetime: unbinding removes their view, while destruction disposes the queue's subscriptions.

Each `NavView` owns its direct-child `NavPage` elements and modal priorities. The editing
page on XR contains its own navigation view. Reset its history only for a new editing
session; temporarily presenting an error must preserve the originating subpage.

## Copy and verification

See `../README.md` for localization tables, Smart String arguments, and error ownership.
`MenuPresentationTests` covers localization, errors, and nested navigation.
`MenuBindingLifetimeTests` covers disposing and rebinding controls on a surviving tree.
`MapMenuCompositionTests` covers platform composition, isolated alignment fields,
localization, and independent bindings for repeated sections.
Editor tests do not establish Quest permission dialogs, Android settings launch,
physical tag measurement, or multiplayer connection/alignment timing.


## Alignment references and simulator startup

Switching prerequisites are based on the map's reference state, not its preferred method's name. A map with no tags or saved anchors can establish its first reference, even when it contains objects. When references already exist, adding a reference for another method requires alignment to the existing frame. The setup action explains this requirement before enabling registration. A device that cannot recover the references must use a capable headset to add the new reference; simulation does not rebase the original map.

Editor XR simulation restores the most recently used saved map before automatic hosting. It does not rely on native-anchor discovery to select a saved map. Real headset startup still uses the physical-space probe, and operator startup retains its own map-selection policy.

The size measurement tool retains the map ID, because map reads return detached snapshots. Reading another snapshot or changing size does not cancel measurement; replacing or unloading the map does.
