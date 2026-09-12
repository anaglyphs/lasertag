# Lasertag

Lasertag is a mixed reality colocated multiplayer FPS for Meta Quest 3 (and possibly other headsets). It's an experimental multiplayer mixed-reality game for people in the same physical space.

# [Join the beta ➔](https://www.meta.com/s/59aIPXxXi)
### [Website & video ➔](https://anagly.ph/)
### [Join the discord ➔](https://discord.com/invite/DgTqG5A6BD)

## Code

Lasertag is a Unity project using the Netcode for GameObjects multiplayer library. 

The Netcode for GameObjects configuration is unusual: Lasertag uses NGO's Distributed Authority mode but via LAN ('DAHost' mode) instead of through Unity's cloud services (CMB Services). While undocumented, everything but host migration works.

Lasertag features a custom live environment scanning system that progressively scans and meshes the environment during play. The environment scan is used for physics collision and visual occlusion.

Shared spatial anchors also work with a PC operator host. The operator automatically selects
one ready headset to create and share anchors, retaining session and map authority itself.
In a headset-hosted session, the Host/SessionOwner is the minter. Headsets report readiness
through their connected player object, including before alignment or while sitting out.
If the minter disconnects or becomes unavailable, the operator selects another ready headset;
existing anchors and the map frame remain in place. A replacement must align before adding
anchors to an existing map. A blank map can establish its first anchor on the selected headset.

The map's alignment settings offer **Shared spatial anchors**, **AprilTags**, **System determined**,
and **Two AprilTags**.
Two AprilTags needs no tag registration. Use any two distinct IDs: the lower ID defines the
world origin (including height), and the horizontal line toward the higher ID defines forward
(+Z), with gravity defining up. Scan order does not matter. If more than two IDs are seen,
the provider uses the two lowest seen since it started; show the same pair to every headset.
Place the tags at least 10 cm apart horizontally; a longer baseline improves heading accuracy.
Set their printed edge length with the map editor palette's tag-size slider or measurement tool,
then scan both tags on each headset. Both AprilTag methods share the map's `tagSizeCm` setting.
They may be scanned separately. Unsaved local anchors keep alignment after they leave view;
both anchors must remain tracked. Scan again after loading a map, reloading the provider, or changing tag size.
Retained map registrations do not affect this method. The choice and tag size
are saved with the map and synchronized across the session; peers must run a build supporting it.

System determined resets the XR tracking origin to world position zero and identity rotation, leaving
localization to the XR system. It needs no anchor saving, loading, or sharing and works in the Editor
and player builds. Select it for XR Interaction Toolkit / AR Foundation simulation, or when an external
system already supplies a common origin across devices. The selection is saved with the map and
coordinated across the multiplayer session; existing anchor and tag records are retained for later use.
This method trusts the system's origin; it does not verify physical alignment between devices.

## License

Lasertag uses the PolyForm Noncommercial License 1.0.0. If you would like to license Lasertag code for commercial projects, please reach out to me!

## Credits

- UI sounds from [Fourier](https://opengameart.org/users/fourier) on [opengameart.org](opengameart.org)
- "Level up sound effects" by [Bart Kelsey](https://opengameart.org/users/bart). Commissioned by Will Corwin for [OpenGameArt.org](http://opengameart.org)
- [Meshia Mesh Simplification](https://github.com/RamType0/Meshia.MeshSimplification) — called by the environment scanner system for mesh simplification. Excellent package!
