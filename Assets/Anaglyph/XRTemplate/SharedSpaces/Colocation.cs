using System;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public interface IColocator
	{
		public bool IsColocated { get; }
		public event Action<bool> IsColocatedChange;

		public void Colocate();
		public void StopColocation();
	}

	public static class Colocation
	{
		private static IColocator activeColocator;
		public static IColocator ActiveColocator => activeColocator;

		private static bool _isColocated;
		public static event Action<bool> IsColocatedChange;
		private static void SetIsColocated(bool b) => IsColocated = b;
		public static bool IsColocated
		{
			get => _isColocated;
			set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed)
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		public static void SetActiveColocator(IColocator colocator)
		{
			if (activeColocator != null)
			{
				activeColocator.StopColocation();
				activeColocator.IsColocatedChange -= SetIsColocated;
			}

			activeColocator = colocator;

			if (activeColocator != null)
			{
				IsColocated = activeColocator.IsColocated;
				activeColocator.IsColocatedChange += SetIsColocated;
			}
		}
	}
}
