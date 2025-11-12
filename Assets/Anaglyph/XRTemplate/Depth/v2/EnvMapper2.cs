using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
    public class EnvMapper2 : MonoBehaviour
    {
	    public EnvMapper2 Instance { get; private set; }
	    
	    [SerializeField] private ComputeShader comp;
	    
	    private ComputeKernel Integrate;
	    private ComputeBuffer blocks;
	    
	    private const int NumBlocks = 1024 * 1024;

	    [GenerateHLSL(PackingRules.Exact, false)]
	    public struct Block
	    {
		    public int3 pos_ws;   // 24
		    public uint offset;   //  8
		    public uint ptr;      //  8
		    public uint3 padding; // 24
		    public const int Size =  32;
	    }

	    private void Awake()
	    {
		    Instance = this;

		    blocks = new ComputeBuffer(NumBlocks, Block.Size);
	    }
    }
}
