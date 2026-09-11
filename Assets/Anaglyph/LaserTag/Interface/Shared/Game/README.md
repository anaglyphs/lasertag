# Reusing game forms

Share a field or cohesive section when its meaning and actions are the same. Keep
page layout, navigation, and device tools with the menu that presents them. The XR
and operator map pages demonstrate different compositions of the same sections.

| UXML section root | Binder | Responsibility |
| --- | --- | --- |
| `map-catalog-section` | `MapPickerBinder` (via `MapManagerUI`) | Create maps; select, load, and delete saved maps. |
| `map-name-section` | `MapNameBinder` | Rename the current map on focus loss. |
| `alignment-method-section` | `AlignmentMethodBinder` | Request a method and show active/saved state and blockers. |
| `tag-configuration-section` | `TagConfigurationBinder` | Set tag size, display count, and remove registrations. |

Instantiate a UXML fragment with a `Template`/`Instance` using its canonical asset
URL. Query its named section root within the owning page, then pass that root to
its binder. Shared names are intentionally scoped: two instances can coexist when
each binder receives its own section. `MapFields.uss` provides the small shared
layout rules; platform pages choose placement and navigation.

Bind after the document exists and after configuring button press behavior. Retain
binders for that tree's enabled lifetime, refresh while presented, and dispose on
disable. Refreshes update values without emitting user changes and preserve fields
being typed in. Do not keep binders attached to a replaced visual tree.

`MapManagerUI` retains catalog selection between bindings and supplies the platform's
map filter. The catalog selection is not an editing target: name/alignment/tag
fields operate on `LaserTagMapCoordinator.CurrentMap`.

Before changing alignment or leaving settings, flush a pending tag size using
`TagConfigurationBinder.FlushPendingSize`. `AlignmentMethodBinder.Changing` runs
before submitting the method request; `Changed` lets the owner refresh surrounding
sections afterward. Backend state can change independently, so owners also refresh
while visible. Pass `AlignmentMethodBinder.Status` into tag configuration's refresh
to avoid repeating a shared blocker. The page decides when tag controls are visible.

Physical actions belong to the headset composition: `MapProbeBinder` scans for maps;
`TagRegistrationBinder` manages setup, measurement, and tool activation. Desktop pages
omit those controls entirely. Neither shared binders nor UXML need operator flags.

Backend eligibility, session negotiation, and persistence remain in the coordinator
and map systems. These binders submit requests and display their results; they do
not duplicate business rules. Menus presenting these actions bind a Game-area
`MenuErrorPresenter` so rejected requests have a visible destination.
