SamplerState envLinearClampSampler;
SamplerState envPointClampSampler;

Texture3D<float> envVolume; // tsdf
uint3 envVoxCount; // dimensions of volume texture
float envVoxSize;
float envVoxDist;
StructuredBuffer<float3> envFrustumVolume;

Texture2D<float4> envDilatedDepth;

int envNumPlayers;
float3 envPlayerHeads[512];

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


half envSampleVolumeDist(float3 worldPos)
{
	float3 uvw = envWorldToVoxelUVW(worldPos);
	float rawVal = envVolume.SampleLevel(envLinearClampSampler, uvw, 0);
	float val = rawVal == -1.0 ? 1.0 : rawVal;
	return val * envVoxDist;
}

float sampleDilatedDepth(float2 uv)
{
	return envDilatedDepth.SampleLevel(envPointClampSampler, uv, 0).z;
}
