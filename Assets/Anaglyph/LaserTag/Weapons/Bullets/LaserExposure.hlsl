// exposure desaturation: the emission leaks a little into every channel, so once
// the dominant channels clip the leak keeps rising and the core burns to white
// while the dim halo stays saturated
half3 LaserExposure(float glow, half3 color, half whiteness)
{
	const half3 spectrum = lerp(color, 1, whiteness);
	return 1 - exp(-glow * spectrum);
}
