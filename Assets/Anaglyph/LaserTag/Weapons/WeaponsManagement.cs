namespace Anaglyph.LaserTag.Weapons
{
	public static class WeaponsManagement
	{
		// MainPlayer owns this - it stays false until the player is alive and in play
		private static bool canFire;
		public static bool CanFire
		{
			get => canFire && ColocationManager.IsColocated &&
				!(LaserTagMapCoordinator.Instance != null && LaserTagMapCoordinator.Instance.IsChangingColocation);
			set => canFire = value;
		}
	}
}
