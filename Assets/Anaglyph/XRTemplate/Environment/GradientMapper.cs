using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class GradientMapper : SingletonBehavior<GradientMapper>
	{
		[SerializeField] private ComputeShader compute;

		protected override void SingletonAwake()
		{
			
		}

		private void Start()
		{
			
		}

		protected override void OnSingletonDestroy()
		{

		}
	}
}
