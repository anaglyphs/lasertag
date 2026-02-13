SamplerState linearClampSampler;

RWTexture3D<float> envVolumeRW; // tsdf
Texture3D<float> envVolume; // tsdf
uint3 envVoxCount; // dimensions of volume texture
float envVoxSize;
float envVoxDist;
StructuredBuffer<float3> envFrustumVolume;

int envNumPlayers;
float3 envPlayerHeads[512];

#define PLAYER_TOP 0.2
#define PLAYER_BOTTOM 1.7
#define PLAYER_RADIUS 0.5
#define MIN_DOT 0.5

float3 envVoxelToWorld(uint3 indices)
{
	float3 pos = indices;
	pos += 0.5f; // voxel center
	pos -= envVoxCount / 2.0f;
	pos *= envVoxSize;

	return pos;
}

float3 envWorldToVoxelFloat(float3 pos)
{
	pos /= envVoxSize;
	pos += (float3)envVoxCount / 2.0f;
	// do not subtract half
	return pos;
}

uint3 envWorldToVoxel(float3 pos)
{
	pos = envWorldToVoxelFloat(pos);

	uint3 id = uint3(floor(pos));
	id = clamp(id, 0, envVoxCount);
	return id;
}

float3 envWorldToVoxelUVW(float3 pos)
{
	pos = envWorldToVoxelFloat(pos);
	pos /= envVoxCount;

	return saturate(pos);
}
