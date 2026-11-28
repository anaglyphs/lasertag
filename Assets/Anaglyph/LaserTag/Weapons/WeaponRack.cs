using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	/// <summary>
	/// Lays every weapon in the database along a curved wall centered on local +Z, in rows
	/// centered at arm height. A hand takes one by touching it and pressing trigger or grip.
	/// </summary>
	public class WeaponRack : MonoBehaviour
	{
		[SerializeField] private WeaponDatabase database;
		[SerializeField] private Material hoverMaterial;

		[SerializeField, Min(0.1f)] private float wallRadius = 1.8f;
		[SerializeField] private float distanceFromWall = 0.12f;
		[SerializeField] private float armHeight = 1.1f;
		[SerializeField, Min(1)] private int maxWeaponsPerRow = 4;
		[SerializeField, Min(0.01f)] private float spacingAlongWall = 0.45f;
		[SerializeField, Min(0.01f)] private float rowSpacing = 0.35f;
		[SerializeField, Min(0)] private float touchPadding = 0.03f;

		private void Start()
		{
			if (database == null)
				return;

			int weaponCount = database.Count;
			int rowCount = Mathf.CeilToInt(weaponCount / (float)maxWeaponsPerRow);
			float weaponRadius = wallRadius - distanceFromWall;
			float degreesBetweenWeapons = spacingAlongWall / weaponRadius * Mathf.Rad2Deg;

			for (int id = 0; id < weaponCount; id++)
			{
				int row = id / maxWeaponsPerRow;
				int column = id % maxWeaponsPerRow;
				int weaponsInRow = Mathf.Min(maxWeaponsPerRow, weaponCount - row * maxWeaponsPerRow);

				float degrees = (column - (weaponsInRow - 1) / 2f) * degreesBetweenWeapons;
				float height = armHeight + ((rowCount - 1) / 2f - row) * rowSpacing;
				Vector3 towardWall = Quaternion.AngleAxis(degrees, Vector3.up) * Vector3.forward;
				Vector3 alongWall = Vector3.Cross(Vector3.up, towardWall);

				CreateSlot(id, towardWall * weaponRadius + Vector3.up * height,
					Quaternion.LookRotation(alongWall, Vector3.up));
			}
		}

		private void CreateSlot(int weaponId, Vector3 localPosition, Quaternion localRotation)
		{
			GameObject weaponPrefab = database.GetWeapon(weaponId);
			GameObject viewPrefab = database.GetView(weaponId);

			if (viewPrefab == null)
				return;

			GameObject slot = new($"Weapon Slot: {weaponPrefab.name}");
			slot.transform.SetParent(transform, false);
			slot.transform.SetLocalPositionAndRotation(localPosition, localRotation);

			GameObject view = Instantiate(viewPrefab, slot.transform, false);
			view.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

			Bounds viewBounds = MeshBoundsIn(slot.transform, view);
			view.transform.localPosition = -viewBounds.center;

			BoxCollider touchArea = slot.AddComponent<BoxCollider>();
			touchArea.isTrigger = true;
			touchArea.size = viewBounds.size + Vector3.one * (touchPadding * 2);

			Renderer[] renderers = view.GetComponentsInChildren<Renderer>(true);
			Material[][] normalMaterials = System.Array.ConvertAll(renderers, r => r.sharedMaterials);

			WeaponPickup pickup = slot.AddComponent<WeaponPickup>();
			pickup.weaponPrefab = weaponPrefab;
			pickup.selectOnPress = true;
			pickup.shownWeapon = view.transform;
			pickup.HoveredChanged += hovered =>
			{
				if (hoverMaterial == null)
					return;

				for (int i = 0; i < renderers.Length; i++)
				{
					Material[] materials = normalMaterials[i];

					if (hovered)
					{
						materials = new Material[materials.Length];
						System.Array.Fill(materials, hoverMaterial);
					}

					renderers[i].sharedMaterials = materials;
				}
			};
		}

		private static Bounds MeshBoundsIn(Transform space, GameObject root)
		{
			Bounds bounds = new();
			bool hasAny = false;

			foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (meshFilter.sharedMesh == null) continue;

				Matrix4x4 meshToSpace = space.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
				Bounds meshBounds = meshFilter.sharedMesh.bounds;

				for (int corner = 0; corner < 8; corner++)
				{
					Vector3 cornerOffset = Vector3.Scale(meshBounds.extents, new Vector3(
						(corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
					Vector3 point = meshToSpace.MultiplyPoint3x4(meshBounds.center + cornerOffset);

					if (hasAny) bounds.Encapsulate(point);
					else bounds = new Bounds(point, Vector3.zero);
					hasAny = true;
				}
			}

			return bounds;
		}
	}
}
