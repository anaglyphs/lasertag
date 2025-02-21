using System;
using UnityEngine;
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
		public InteractorHandedness Handedness
		{
			get => _handedness;
			set
			{
				if (_handedness != value)
				{
					_handedness = value;
					OnHandednessChange?.Invoke(value);
				}
			}
		}

	}
}