using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Composites;
using System.ComponentModel;

namespace EnvisionCenter
{
#if UNITY_EDITOR
	[InitializeOnLoad] // Automatically register in editor.
#endif

	[DisplayStringFormat("{modifier1}+{modifier2}+{binding}")]
	[DisplayName("Binding With Two Modifiers as Pressed")]
	public class TwoModifiersAsPressedComposite : TwoModifiersComposite
	{
		public override float EvaluateMagnitude(ref InputBindingCompositeContext context)
		{
			return context.ReadValueAsButton(modifier1) && context.ReadValueAsButton(modifier2) ? 1f : 0f;
		}

		static TwoModifiersAsPressedComposite()
		{
			InputSystem.RegisterBindingComposite<TwoModifiersAsPressedComposite>();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Init() { }
	}

#if UNITY_EDITOR
	[InitializeOnLoad] // Automatically register in editor.
#endif

	[DisplayStringFormat("{modifier1}+{modifier2}+{binding}")]
	[DisplayName("Binding With One Modifier as Pressed")]
	public class OneModifierAsPressedComposite : OneModifierComposite
	{
		public override float EvaluateMagnitude(ref InputBindingCompositeContext context)
		{
			return context.ReadValueAsButton(modifier) ? 1f : 0f;
		}

		static OneModifierAsPressedComposite()
		{
			InputSystem.RegisterBindingComposite<OneModifierAsPressedComposite>();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Init() { }
	}
}
