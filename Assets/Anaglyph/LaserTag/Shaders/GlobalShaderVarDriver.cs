using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class GlobalShaderVarDriver : MonoBehaviour
	{
		private static readonly int StereoMatrixInvVPId = Shader.PropertyToID("StereoMatrixInvVP");
		private static readonly int UnityStereoMatrixInvVPId = Shader.PropertyToID("unity_StereoMatrixInvVP");

		private void Update()
		{
			Matrix4x4[] mats = Shader.GetGlobalMatrixArray("unity_StereoMatrixInvVP");
			Shader.SetGlobalMatrixArray(StereoMatrixInvVPId, mats);

			//Shader.SetGlobalMatrixArray(unity_StereoCameraInvProjection, s_invViewProjMatrix);
		}
	}
}