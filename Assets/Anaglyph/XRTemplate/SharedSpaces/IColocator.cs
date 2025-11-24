using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public interface IColocator
	{
		public event Action Colocated;
		public void Colocate();
		public void StopColocation();
	}
}