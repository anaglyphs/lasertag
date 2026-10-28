#ifndef AG_VIEW_QUAD_INCLUDED
#define AG_VIEW_QUAD_INCLUDED

// A screen-covering quad built in the eye's view space, for fullscreen passes that need
// each pixel's world-space view ray. Unlike unprojecting clip coordinates through
// UNITY_MATRIX_I_VP, it only uses the view matrix (never y-flipped) and the forward
// projection the rasterizer actually uses, so it stays correct whichever way the
// current target's projection is flipped. Draw 6 vertices with Cull Off.

// world-space position of this vertex, 2x the near plane out and wide enough for
// half-angles up to 84 degrees
float3 agViewQuadWorld(uint vertexID)
{
	static const float2 corners[6] =
	{
		float2(-1, -1), float2(1, -1), float2(1, 1),
		float2(-1, -1), float2(1, 1), float2(-1, 1)
	};
	float distance = 2.0 * _ProjectionParams.y;
	float3 viewPos = float3(corners[vertexID] * distance * 10.0, -distance);
	return mul(UNITY_MATRIX_I_V, float4(viewPos, 1)).xyz;
}

#endif
