using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR.CompositionLayers;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Anaglyph.XR.UILayers
{
	/// <summary>
	/// Submits <see cref="HeadLockedQuadLayerData"/> layers in the OpenXR VIEW
	/// space, so the compositor places them against the newest head pose every
	/// display refresh and they stay put on the display through frame drops.
	/// Requires "Composition Layers Support" to be enabled as well.
	/// </summary>
#if UNITY_EDITOR
	[OpenXRFeature(UiName = "Head-Locked Composition Layers",
		BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
		Company = "Anaglyph",
		Desc = "Submits head-locked quad layers in VIEW space so they never reproject.",
		Category = FeatureCategory.Feature,
		FeatureId = featureId,
		Version = "1.0.0")]
#endif
	public class HeadLockedLayersFeature : OpenXRFeature
	{
		public const string featureId = "com.anaglyph.xrtemplate.headlockedlayers";

		/// <summary>0 when the feature is disabled or no session is running.</summary>
		public static ulong ViewSpace { get; private set; }

		public static bool IsAvailable => ViewSpace != 0 && OpenXRLayerProvider.isStarted;

		private ulong instance;
		private XrCreateReferenceSpaceDelegate createReferenceSpace;
		private XrDestroySpaceDelegate destroySpace;

		protected override bool OnInstanceCreate(ulong xrInstance)
		{
			instance = xrInstance;

			XrGetInstanceProcAddrDelegate getProcAddr =
				Marshal.GetDelegateForFunctionPointer<XrGetInstanceProcAddrDelegate>(xrGetInstanceProcAddr);

			if (!TryGetFunction(getProcAddr, "xrCreateReferenceSpace", out createReferenceSpace) ||
			    !TryGetFunction(getProcAddr, "xrDestroySpace", out destroySpace))
			{
				Debug.LogError("HeadLockedLayersFeature: could not resolve OpenXR space functions");
				return false;
			}

			OpenXRLayerProvider.Started += RegisterLayerHandler;
			if (OpenXRLayerProvider.isStarted)
				RegisterLayerHandler();

			return true;
		}

		protected override void OnInstanceDestroy(ulong xrInstance)
		{
			OpenXRLayerProvider.Started -= RegisterLayerHandler;
			createReferenceSpace = null;
			destroySpace = null;
			instance = 0;
		}

		protected override void OnSessionCreate(ulong xrSession)
		{
			XrReferenceSpaceCreateInfo createInfo = new()
			{
				type = XrStructureType.ReferenceSpaceCreateInfo,
				next = IntPtr.Zero,
				referenceSpaceType = XrReferenceSpaceType.View,
				poseInReferenceSpace = new XrPosef(Pose.identity),
			};

			XrResult result = createReferenceSpace(xrSession, ref createInfo, out ulong space);

			if (result != XrResult.Success)
			{
				Debug.LogError($"HeadLockedLayersFeature: xrCreateReferenceSpace returned {result}");
				return;
			}

			ViewSpace = space;
		}

		protected override void OnSessionDestroy(ulong xrSession)
		{
			if (ViewSpace == 0)
				return;

			destroySpace(ViewSpace);
			ViewSpace = 0;
		}

		private static void RegisterLayerHandler()
		{
			OpenXRLayerProvider.RegisterLayerHandler(typeof(HeadLockedQuadLayerData),
				new HeadLockedQuadLayerHandler());
		}

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
		private delegate XrResult XrDestroySpaceDelegate(ulong space);

		[StructLayout(LayoutKind.Sequential)]
		private struct XrReferenceSpaceCreateInfo
		{
			public XrStructureType type;
			public IntPtr next;
			public XrReferenceSpaceType referenceSpaceType;
			public XrPosef poseInReferenceSpace;
		}
	}
}
