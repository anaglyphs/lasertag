using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.XRTemplate
{
	/// <summary>
	/// Children of this component's gameobject use this to check what
	/// handedness this hierarchy represents
	/// </summary>
	public class HandedHierarchy : MonoBehaviour
	{
		[SerializeField] private InteractorHandedness _handedness;
		public event Action<InteractorHandedness> OnHandednessChange = delegate { };

		private XRNode _node;
		public XRNode Node => _node;

		public InteractorHandedness Handedness
		{
			get => _handedness;
			set
			{
				if (_handedness != value)
				{
					_handedness = value;
					UpdateNode();
					OnHandednessChange?.Invoke(value);
				}
			}
		}

		private void UpdateNode()
		{
			switch (_handedness)
			{
				case InteractorHandedness.None:
					_node = default;
					break;

				case InteractorHandedness.Left:
					_node = XRNode.LeftHand;
					break;

				case InteractorHandedness.Right:
					_node = XRNode.RightHand;
					break;
			}
		}

	}
}