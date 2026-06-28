using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[ExecuteAlways]
	public class FlagAnimator : MonoBehaviour
	{
		private Transform clothBoneParent;
		[SerializeField] private Transform rootClothBone;
		private Transform[] clothBones;

		[SerializeField] private float boneOffset = 0.1f;
		[SerializeField] private float timeFactor = 5f;
		[SerializeField] private Vector3 eulerRotation = Vector3.up;

		private void Awake()
		{
			Setup();
		}

		private void OnValidate()
		{
			Setup();
		}

		private void Setup()
		{
			clothBoneParent = rootClothBone.parent;

			List<Transform> bones = new();
			bones.Add(rootClothBone);
			while (bones[^1].childCount == 1) bones.Add(bones[^1].GetChild(0));

			clothBones = bones.ToArray();
		}

		private void Update()
		{
			if (clothBones == null) return;

			float t = Time.time * timeFactor;
			for (int i = 0; i < clothBones.Length; i++)
			{
				float s = Mathf.Sin(t + i * boneOffset);

				Quaternion rot = Quaternion.Euler(eulerRotation * s);

				Quaternion localRot = clothBoneParent.rotation * rot;

				clothBones[i].rotation = localRot;
			}
		}
	}
}