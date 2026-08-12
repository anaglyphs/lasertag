using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Anaglyph.XR
{
	/// <summary>
	/// Asks the OpenXR runtime where the head was at an explicit XrTime, by
	/// locating a VIEW reference space against the app space. This is what
	/// OVRPlugin.GetNodePoseStateAtTime wraps, reached through the OpenXR
	/// function pointers instead, so it needs no Meta SDK dependency.
	///
	/// Must be enabled per build target under
	/// Project Settings > XR Plug-in Management > OpenXR. When it isn't enabled,
	/// or the runtime refuses the timestamp, <see cref="HeadPoseHistory"/> falls
	/// back to interpolating its own samples.
	/// </summary>
#if UNITY_EDITOR
	[OpenXRFeature(UiName = "Head Pose At Time",
		// Standalone too, so it works in the editor over Meta Horizon Link.
		BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
		Company = "Anaglyph",
		Desc = "Locates the view space at an explicit XrTime so camera frames can be " +
		       "paired with the head pose at capture time.",
		Category = FeatureCategory.Feature,
		FeatureId = featureId,
		Version = "1.0.0")]
#endif
	public class HeadPoseAtTimeFeature : OpenXRFeature
	{
		public const string featureId = "com.anaglyph.xrtemplate.headposeattime";

		/// <summary>Null unless the feature is enabled and the runtime is up.</summary>
		public static HeadPoseAtTimeFeature Instance { get; private set; }

		private ulong instance;
		private ulong session;
		private ulong appSpace;
		private ulong viewSpace;

		private XrCreateReferenceSpaceDelegate createReferenceSpace;
		private XrLocateSpaceDelegate locateSpace;
		private XrDestroySpaceDelegate destroySpace;

		private bool loggedLocateFailure;

		public bool IsReady => locateSpace != null && viewSpace != 0;

		/// <summary>
		/// Head pose at <paramref name="timestampNs"/> (an XrTime, which is
		/// CLOCK_MONOTONIC nanoseconds on Quest), expressed in OpenXR app space
		/// -- the same space the XR camera's local transform lives in.
		/// Main thread only. Returns false when the runtime has no pose for that
		/// time, which it will for timestamps outside its history window.
		/// </summary>
		public bool TryLocateViewPose(long timestampNs, out Pose pose)
		{
			pose = default;

			if (!IsReady)
				return false;

			ulong baseSpace = appSpace != 0 ? appSpace : GetCurrentAppSpace();

			if (baseSpace == 0)
				return false;

			XrSpaceLocation location = new() { type = XrStructureType.SpaceLocation };

			XrResult result = locateSpace(viewSpace, baseSpace, timestampNs, ref location);

			if (result != XrResult.Success)
			{
				if (!loggedLocateFailure)
				{
					loggedLocateFailure = true;
					// XR_ERROR_TIME_INVALID here means the requested time is
					// outside the window the runtime keeps.
					Debug.LogWarning($"HeadPoseAtTimeFeature: xrLocateSpace returned {result}, " +
					                 "falling back to interpolated poses");
				}

				return false;
			}

			const ulong required = (ulong)(XrSpaceLocationFlags.PositionValid |
			                               XrSpaceLocationFlags.OrientationValid);

			if ((location.locationFlags & required) != required)
				return false;

			pose = location.pose.ToSessionSpacePose();
			return true;
		}

		protected override bool OnInstanceCreate(ulong xrInstance)
		{
			instance = xrInstance;

			XrGetInstanceProcAddrDelegate getProcAddr =
				Marshal.GetDelegateForFunctionPointer<XrGetInstanceProcAddrDelegate>(xrGetInstanceProcAddr);

			if (!TryGetFunction(getProcAddr, "xrCreateReferenceSpace", out createReferenceSpace) ||
			    !TryGetFunction(getProcAddr, "xrLocateSpace", out locateSpace) ||
			    !TryGetFunction(getProcAddr, "xrDestroySpace", out destroySpace))
			{
				Debug.LogError("HeadPoseAtTimeFeature: could not resolve OpenXR space functions");
				return false;
			}

			Instance = this;
			return true;
		}

		protected override void OnInstanceDestroy(ulong xrInstance)
		{
			if (Instance == this)
				Instance = null;

			createReferenceSpace = null;
			locateSpace = null;
			destroySpace = null;
			instance = 0;
		}

		protected override void OnSessionCreate(ulong xrSession)
		{
			session = xrSession;

			XrReferenceSpaceCreateInfo createInfo = new()
			{
				type = XrStructureType.ReferenceSpaceCreateInfo,
				next = IntPtr.Zero,
				referenceSpaceType = XrReferenceSpaceType.View,
				poseInReferenceSpace = new XrPosef(Pose.identity),
			};

			XrResult result = createReferenceSpace(session, ref createInfo, out viewSpace);

			if (result != XrResult.Success)
			{
				viewSpace = 0;
				Debug.LogError($"HeadPoseAtTimeFeature: xrCreateReferenceSpace returned {result}");
			}
		}

		protected override void OnSessionDestroy(ulong xrSession)
		{
			if (viewSpace != 0)
			{
				destroySpace(viewSpace);
				viewSpace = 0;
			}

			loggedLocateFailure = false;
			session = 0;
		}

		protected override void OnAppSpaceChange(ulong xrSpace) => appSpace = xrSpace;

		private bool TryGetFunction<T>(XrGetInstanceProcAddrDelegate getProcAddr, string name, out T function)
			where T : Delegate
		{
			function = null;

			if (getProcAddr(instance, name, out IntPtr pointer) != XrResult.Success || pointer == IntPtr.Zero)
				return false;

			function = Marshal.GetDelegateForFunctionPointer<T>(pointer);
			return true;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrGetInstanceProcAddrDelegate(ulong instance,
			[MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrCreateReferenceSpaceDelegate(ulong session,
			ref XrReferenceSpaceCreateInfo createInfo, out ulong space);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrLocateSpaceDelegate(ulong space, ulong baseSpace, long time,
			ref XrSpaceLocation location);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrDestroySpaceDelegate(ulong space);

		[StructLayout(LayoutKind.Sequential)]
		private struct XrReferenceSpaceCreateInfo
		{
			public XrStructureType type;
			public IntPtr next;
			public XrReferenceSpaceType referenceSpaceType;
			public XrPosef poseInReferenceSpace;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrSpaceLocation
		{
			public XrStructureType type;
			public IntPtr next;
			// XrSpaceLocationFlags is XrFlags64, so this field is 64 bits wide
			// even though Unity's enum is not.
			public ulong locationFlags;
			public XrPosef pose;
		}
	}
}
