using UnityEngine;

public class UnifiedMemoryTest : MonoBehaviour
{
	[SerializeField] ComputeShader shader;
	int kernel;
	GraphicsBuffer buffer;

	public static bool DirectReadSupported = false;

	void Start()
	{
		kernel = shader.FindKernel("TestKernel");
		var target = GraphicsBuffer.Target.Structured;
		var usageFlag = GraphicsBuffer.UsageFlags.LockBufferForWrite;
		buffer = new GraphicsBuffer(target, usageFlag, 1, sizeof(uint));
		shader.SetBuffer(kernel, "data", buffer);

		// write
		var submit = buffer.LockBufferForWrite<uint>(0, 1);
		submit[0] = 1234;
		buffer.UnlockBufferAfterWrite<uint>(1);

		// dispatch
		shader.Dispatch(kernel, 1, 1, 1);

		// read
		var results = buffer.LockBufferForWrite<uint>(0, 1);
		uint val = results[0];
		buffer.UnlockBufferAfterWrite<uint>(1);

		// test
		DirectReadSupported = val == 9999;

		Debug.Log($"[UnifiedMemoryTest] GPU direct read works: {DirectReadSupported}");

		buffer?.Dispose();
	}
}
