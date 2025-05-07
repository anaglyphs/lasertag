using UnityEngine;

namespace Anaglyph.Lasertag
{
    public interface IBulletHitHandler
    {
		public void OnOwnedBulletHit(Bullet bullet, Vector3 worldHitPoint);
    }
}
