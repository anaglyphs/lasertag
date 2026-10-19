using System;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning.Registration
{
	/// <summary>
	/// A gravity-aligned rigid transform: p' = rotateY(p, yaw) + translation. Tracking
	/// observes gravity, so drift, registration and every correction for them only ever
	/// live in these four degrees of freedom. Yaw follows Unity's sense
	/// (<c>quaternion.RotateY</c>, <c>Quaternion.Euler(0, yaw, 0)</c>).
	/// </summary>
	[Serializable]
	public struct Pose4 : IEquatable<Pose4>
	{
		public float3 translation;
		public float yaw; // radians about +Y, Unity's sense

		public Pose4(float3 translation, float yaw)
		{
			this.translation = translation;
			this.yaw = yaw;
		}

		public static Pose4 Identity => new(float3.zero, 0);

		public quaternion Rotation => quaternion.RotateY(yaw);
		public Pose AsPose => new(translation, Rotation);
		public float4x4 Matrix => float4x4.TRS(translation, Rotation, 1);
		public float4 Packed => new(translation, yaw);

		public static Pose4 FromPacked(float4 packed) => new(packed.xyz, packed.w);

		/// <summary>The yaw-only part of a pose: its position, and its rotation's twist about world up.</summary>
		public static Pose4 FromPose(Vector3 position, Quaternion rotation) => new(position, TwistYaw(rotation));

		/// <summary>Only exact for matrices that rotate about world up alone.</summary>
		public static Pose4 FromMatrix(Matrix4x4 matrix) => new(matrix.GetPosition(), TwistYaw(matrix.rotation));

		public float3 Apply(float3 p) => RotateY(p, yaw) + translation;
		public float3 ApplyInverse(float3 p) => RotateY(p - translation, -yaw);

		/// <summary>Applies this transform to a full pose: its position moves, its rotation turns about world up.</summary>
		public Pose Apply(Pose pose) => new(Apply((float3)pose.position), (Quaternion)Rotation * pose.rotation);

		public Pose4 Inverse => new(-RotateY(translation, -yaw), -yaw);

		/// <summary><paramref name="a"/> after <paramref name="b"/>: a(b(p)).</summary>
		public static Pose4 operator *(Pose4 a, Pose4 b) => new(a.Apply(b.translation), WrapAngle(a.yaw + b.yaw));

		/// <summary><paramref name="from"/>⁻¹ ∘ <paramref name="to"/>: <paramref name="to"/> expressed in <paramref name="from"/>'s frame.</summary>
		public static Pose4 Between(Pose4 from, Pose4 to) => new(from.ApplyInverse(to.translation), WrapAngle(to.yaw - from.yaw));

		public static float3 RotateY(float3 v, float angle)
		{
			math.sincos(angle, out float s, out float c);
			return new float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
		}

		/// <summary>
		/// Rotation about world up from the swing-twist decomposition. Unlike an Euler
		/// heading it stays continuous when the head looks straight up or down.
		/// </summary>
		public static float TwistYaw(Quaternion q)
		{
			if (math.abs(q.y) < 1e-8f && math.abs(q.w) < 1e-8f) return 0f;
			return WrapAngle(2f * math.atan2(q.y, q.w));
		}

		public static float WrapAngle(float a)
		{
			a = math.fmod(a + math.PI, 2f * math.PI);
			if (a < 0f) a += 2f * math.PI;
			return a - math.PI;
		}

		/// <summary>How far this transform moves a point <paramref name="leverArm"/> meters from its pivot, at most.</summary>
		public float Magnitude(float leverArm) => math.length(translation) + math.abs(WrapAngle(yaw)) * leverArm;

		public static bool Approximately(Pose4 a, Pose4 b, float translationTolerance, float yawToleranceRadians) =>
			math.distance(a.translation, b.translation) <= translationTolerance &&
			math.abs(WrapAngle(a.yaw - b.yaw)) <= yawToleranceRadians;

		public bool Equals(Pose4 other) => translation.Equals(other.translation) && yaw.Equals(other.yaw);
		public override bool Equals(object obj) => obj is Pose4 other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(translation, yaw);

		public override string ToString() => $"t=({translation.x:F2}, {translation.y:F2}, {translation.z:F2}) yaw={math.degrees(yaw):F1}°";
	}
}
