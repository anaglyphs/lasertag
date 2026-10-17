#ifndef ANAGLYPH_WIREFRAME_INCLUDED
#define ANAGLYPH_WIREFRAME_INCLUDED

// 1 on a triangle edge, 0 inside, antialiased over a pixel. barycentric is the
// interpolated corner color of a mesh built by WireframeMesh; width is in pixels.
half WireframeEdge(half3 barycentric, half width)
{
	float3 b = barycentric;
	float3 pixels = b / max(fwidth(b), 1e-5);
	float nearest = min(pixels.x, min(pixels.y, pixels.z));
	return 1.0 - saturate(nearest - width + 0.5);
}

// Premultiplied output for the passthrough compositor: the fill at fillAlpha, the
// wire solid on top of it
half4 WireframeComposite(half3 fillColor, half3 wireColor, half3 barycentric, half width, half fillAlpha)
{
	half wire = WireframeEdge(barycentric, width);
	half3 rgb = lerp(fillColor * fillAlpha, wireColor, wire);
	half a = lerp(fillAlpha, 1.0, wire);
	return half4(rgb, a);
}

#endif
