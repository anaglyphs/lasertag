# Lasertag

Lasertag is a mixed reality colocated multiplayer FPS for Meta Quest 3 (and possibly other headsets). It's an experimental multiplayer mixed-reality game for people in the same physical space.

# [Join the beta ➔](https://www.meta.com/s/59aIPXxXi)
### [Website & video ➔](https://anagly.ph/)
### [Join the discord ➔](https://discord.com/invite/DgTqG5A6BD)

## Code

Lasertag is a Unity project using the Netcode for GameObjects multiplayer library. 

The Netcode for GameObjects configuration is unusual: Lasertag uses NGO's Distributed Authority mode but via LAN ('DAHost' mode) instead of through Unity's cloud services (CMB Services). While undocumented, everything but host migration works.

Lasertag features a custom live environment scanning system that progressively scans and meshes the environment during play. The environment scan is used for physics collision and visual occlusion.

## License

Lasertag uses the PolyForm Noncommercial License 1.0.0. If you would like to license Lasertag code for commercial projects, please reach out to me!

## Credits

- UI sounds from [Fourier](https://opengameart.org/users/fourier) on [opengameart.org](opengameart.org)
- "Level up sound effects" by [Bart Kelsey](https://opengameart.org/users/bart). Commissioned by Will Corwin for [OpenGameArt.org](http://opengameart.org)
- [Meshia Mesh Simplification](https://github.com/RamType0/Meshia.MeshSimplification) — called by the environment scanner system for mesh simplification. Excellent package!

