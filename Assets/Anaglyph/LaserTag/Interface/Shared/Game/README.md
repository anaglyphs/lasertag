# Reusing game forms

Share a field or cohesive section when its meaning and actions are the same. Keep
page layout, navigation, and device tools with the menu that presents them. The XR
and operator map pages demonstrate different compositions of the same sections.

| UXML section root | Binder | Responsibility |
| --- | --- | --- |
| `map-catalog-section` | `MapPickerBinder` (via `MapManagerUI`) | Create maps; select, load, and delete saved maps and spaces. |
| `space-details` (page) | `SpaceDetailsBinder` | Name/reset the current space; host alignment settings. |
| `map-name-section` | `MapNameBinder` | Rename the current map on focus loss. |
| `alignment-settings-section` | `AlignmentSettingsBinder` | Select alignment, show method-specific settings, and own operator tag size/count/removal. |

Instantiate a UXML fragment with a `Template`/`Instance` using its canonical asset
URL. Query its named section root within the owning page, then pass that root to
its binder. Shared names are intentionally scoped: two instances can coexist when
each binder receives its own section. `MapFields.uss` provides the small shared
layout rules; platform pages choose placement and navigation.

Bind after the document exists and after configuring button press behavior. Retain
binders for that tree's enabled lifetime, refresh while presented, and dispose on
disable. Refreshes update values without emitting user changes and preserve fields
being typed in. Do not keep binders attached to a replaced visual tree.

`MapManagerUI` retains catalog selection between bindings. Space and map rows share
one selection. Editing a selected space loads it and its most recently used map before
opening space details. Loading a map switches to its space when necessary. Deleting a selected space opens `DeleteSpacePage` as a modal in the parent `NavView`,
without activating the space. The modal names the space, warns that its maps are also
deleted, and rechecks eligibility on confirmation. Cancel or Back leaves it untouched.
The catalog selection is the deletion target; map naming
uses `CurrentMap`, while space naming, alignment and tags use `CurrentSpace`.

`AlignmentSettingsBinder` flushes pending tag size before submitting a method change.
The page also calls `FlushPendingSize` when navigating away; disposal flushes it too.
Delayed edits retain their original reference context. `Changing` and `Changed` let
headset owners coordinate physical tools. Backend state can change independently,
so owners refresh while visible. The binder owns method-specific visibility: both
tag methods expose operator size controls, registered AprilTags expose count/removal,
and Two AprilTags exposes pair selection.

Physical actions belong to the headset composition: `MapProbeBinder` probes spaces;
`GameMenu` activates tag tools from space settings and routes automatic registration
there. `PaletteMenu` manages measurement and physical tool presentation.
`SpaceDetailsBinder` exposes manual tag configuration when bound for the operator;
headsets retain those controls in the palette.

Backend eligibility, session negotiation, and persistence remain in the coordinator
and map systems. These binders submit requests and display their results; they do
not duplicate business rules. Menus presenting these actions bind a Game-area
`MenuErrorPresenter` so rejected requests have a visible destination.
