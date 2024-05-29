using LaserTag.Networking;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class RadarDriver : MonoBehaviour
{
	private Camera mainCamera;
	private Transform camTransform => mainCamera.transform;

	[SerializeField]
	private RectTransform radarViewTransform;
	[SerializeField]
	private RectTransform radarForwardTransform;
	[SerializeField]
	private Image radarGridMaterial;

	[SerializeField]
	private GameObject handObject;

	[SerializeField]
	private Transform unitPlayfieldTransform;

	[SerializeField]
	private GameObject playerMarkerPrefab;

	[SerializeField]
	private List<GameObject> players;

	[SerializeField]
	private GameObject baseMarkerPrefab;

	[SerializeField]
	private List<GameObject> bases;

	[SerializeField] private float RadarScale = 0.5f;


	private void Awake()
	{
		mainCamera = Camera.main;
	}

	void Update()
	{
		radarViewTransform.localRotation = Quaternion.Euler(0, 0, transform.parent.eulerAngles.y - 90);
		radarForwardTransform.localRotation = Quaternion.Euler(0, 0, -camTransform.eulerAngles.y);

		radarGridMaterial.material.SetVector("_GridOffset", transform.parent.position * RadarScale);

		TrackAll(Player.AllPlayers.Where(player => !player.IsOwner).Select(player => player.HeadTransform), playerMarkerPrefab, ref players);

        TrackAll(Base.AllBases.Select(_base => _base.transform), baseMarkerPrefab, ref bases);
    }

	void TrackAll(IEnumerable<Transform> objects, GameObject prefab, ref List<GameObject> markerList)
	{
		int objectCount = objects.Count();

		if (markerList.Count < objectCount)
		{
			for (int i = 0; i < objectCount - markerList.Count; i++)
			{
				markerList.Add(Instantiate(prefab, unitPlayfieldTransform));
			}
		}
		else if (markerList.Count > objectCount)
		{
			for (int i = 0; i < markerList.Count - objectCount; i++)
			{
				Destroy(players[0]);

				markerList.RemoveAt(0);
			}
		}

		for (int i = 0; i < objectCount; i++)
		{
			Vector3 objectOffset = objects.ElementAt(i).position - handObject.transform.position;

            markerList[i].transform.localPosition = new Vector3(objectOffset.x * 100 * RadarScale, objectOffset.z * 100 * RadarScale, 0);
        }
    }
}
