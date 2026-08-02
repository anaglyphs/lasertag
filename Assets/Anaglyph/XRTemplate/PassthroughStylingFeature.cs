using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Anaglyph.XRTemplate
{
	/// <summary>
	/// Styles the passthrough composition layer created internally by the Meta
	/// OpenXR AR Foundation provider. This is the XR_FB_passthrough equivalent of
	/// the basic tint and edge controls on OVRPassthroughLayer; it does not depend
	/// on OVRPassthroughLayer or XR_META_passthrough_color_lut.
	/// </summary>
#if UNITY_EDITOR
	[OpenXRFeature(UiName = "AR Foundation Passthrough Styling",
		BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
		Company = "Anaglyph",
		Desc = "Applies XR_FB_passthrough color and edge tinting to the passthrough " +
		       "composition layer owned by the Meta OpenXR AR Foundation provider.",
		Category = FeatureCategory.Feature,
		FeatureId = featureId,
		OpenxrExtensionStrings = passthroughExtension,
		Priority = 100,
		Version = "1.0.0")]
#endif
	public sealed class PassthroughStylingFeature : OpenXRFeature
	{
		public const string featureId = "com.anaglyph.xrtemplate.passthrough-styling";
		public const string passthroughExtension = "XR_FB_passthrough";

		private const int PassthroughStyleType = 1000118020;
		private const int PassthroughColorMapMonoToRgbaType = 1000118021;
		private const int ColorMapSize = 256;

		private const string CreateLayerFunctionName = "xrCreatePassthroughLayerFB";
		private const string DestroyLayerFunctionName = "xrDestroyPassthroughLayerFB";
		private const string SetStyleFunctionName = "xrPassthroughLayerSetStyleFB";

		private static readonly object sync = new();
		private static readonly HashSet<ulong> layers = new();

		// These static delegate references must remain rooted for native OpenXR calls.
		private static readonly XrGetInstanceProcAddrDelegate getInstanceProcAddrHook =
			GetInstanceProcAddrHook;
		private static readonly XrCreatePassthroughLayerDelegate createLayerHook =
			CreatePassthroughLayerHook;
		private static readonly XrDestroyPassthroughLayerDelegate destroyLayerHook =
			DestroyPassthroughLayerHook;

		private static readonly IntPtr getInstanceProcAddrHookPointer =
			Marshal.GetFunctionPointerForDelegate(getInstanceProcAddrHook);
		private static readonly IntPtr createLayerHookPointer =
			Marshal.GetFunctionPointerForDelegate(createLayerHook);
		private static readonly IntPtr destroyLayerHookPointer =
			Marshal.GetFunctionPointerForDelegate(destroyLayerHook);

		private static XrGetInstanceProcAddrDelegate originalGetInstanceProcAddr;
		private static XrCreatePassthroughLayerDelegate originalCreateLayer;
		private static XrDestroyPassthroughLayerDelegate originalDestroyLayer;
		private static XrPassthroughLayerSetStyleDelegate setStyle;

		private static float opacity = 1f;
		private static Color passthroughTint = Color.white;
		private static float passthroughTintAmount;
		private static Color edgeTint = Color.clear;

		/// <summary>True while at least one AR Foundation passthrough layer is available.</summary>
		public static bool HasPassthroughLayer
		{
			get
			{
				lock (sync)
					return layers.Count != 0;
			}
		}

		/// <summary>
		/// Applies a solid colorization and edge tint. XR_FB_passthrough represents
		/// this colorization as a luminance-to-RGBA map, so a nonzero tint amount
		/// replaces the original camera chroma. A tint amount of zero keeps the
		/// original camera colors. Edge rendering is disabled when
		/// <paramref name="newEdgeTint"/> has zero alpha. The requested style is
		/// retained and applied when AR Foundation creates or recreates its layer.
		/// </summary>
		/// <returns>True if the style was applied to a live passthrough layer.</returns>
		public static bool SetStyle(Color tint, float tintAmount, Color newEdgeTint,
			float textureOpacity = 1f)
		{
			lock (sync)
			{
				passthroughTint = tint;
				passthroughTintAmount = Mathf.Clamp01(tintAmount);
				edgeTint = newEdgeTint;
				opacity = Mathf.Clamp01(textureOpacity);
				return ApplyStyleToAllLayers();
			}
		}

		/// <summary>Changes only the passthrough colorization.</summary>
		public static bool SetPassthroughTint(Color tint, float tintAmount = 1f)
		{
			lock (sync)
			{
				passthroughTint = tint;
				passthroughTintAmount = Mathf.Clamp01(tintAmount);
				return ApplyStyleToAllLayers();
			}
		}

		/// <summary>
		/// Changes only the detected-edge tint. Pass <see cref="Color.clear"/> to
		/// disable edge rendering.
		/// </summary>
		public static bool SetEdgeTint(Color tint)
		{
			lock (sync)
			{
				edgeTint = tint;
				return ApplyStyleToAllLayers();
			}
		}

		/// <summary>Restores unmodified, fully opaque passthrough with no edges.</summary>
		public static bool ClearStyle() => SetStyle(Color.white, 0f, Color.clear);

		protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
		{
			if (func == IntPtr.Zero || func == getInstanceProcAddrHookPointer)
				return func;

			originalGetInstanceProcAddr =
				Marshal.GetDelegateForFunctionPointer<XrGetInstanceProcAddrDelegate>(func);
			return getInstanceProcAddrHookPointer;
		}

		protected override bool OnInstanceCreate(ulong xrInstance)
		{
			if (!OpenXRRuntime.IsExtensionEnabled(passthroughExtension))
			{
				Debug.LogWarning($"{nameof(PassthroughStylingFeature)}: " +
				                 $"{passthroughExtension} is not enabled");
				return false;
			}

			if (!TryGetFunction(xrInstance, SetStyleFunctionName, out setStyle))
			{
				Debug.LogError($"{nameof(PassthroughStylingFeature)}: could not resolve " +
				               SetStyleFunctionName);
				return false;
			}

			return true;
		}

		protected override void OnInstanceDestroy(ulong xrInstance)
		{
			lock (sync)
				layers.Clear();

			setStyle = null;
			originalCreateLayer = null;
			originalDestroyLayer = null;
		}

		private static bool TryGetFunction<T>(ulong xrInstance, string name, out T function)
			where T : Delegate
		{
			function = null;
			if (originalGetInstanceProcAddr == null)
				return false;

			IntPtr namePointer = Marshal.StringToHGlobalAnsi(name);
			try
			{
				XrResult result = originalGetInstanceProcAddr(xrInstance, namePointer,
					out IntPtr functionPointer);
				if (result != XrResult.Success || functionPointer == IntPtr.Zero)
					return false;

				function = Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(namePointer);
			}
		}

		[AOT.MonoPInvokeCallback(typeof(XrGetInstanceProcAddrDelegate))]
		private static XrResult GetInstanceProcAddrHook(ulong xrInstance, IntPtr name,
			out IntPtr function)
		{
			XrResult result = originalGetInstanceProcAddr(xrInstance, name, out function);
			if (result != XrResult.Success || function == IntPtr.Zero)
				return result;

			string functionName = Marshal.PtrToStringAnsi(name);
			if (functionName == CreateLayerFunctionName && function != createLayerHookPointer)
			{
				originalCreateLayer =
					Marshal.GetDelegateForFunctionPointer<XrCreatePassthroughLayerDelegate>(function);
				function = createLayerHookPointer;
			}
			else if (functionName == DestroyLayerFunctionName && function != destroyLayerHookPointer)
			{
				originalDestroyLayer =
					Marshal.GetDelegateForFunctionPointer<XrDestroyPassthroughLayerDelegate>(function);
				function = destroyLayerHookPointer;
			}

			return result;
		}

		[AOT.MonoPInvokeCallback(typeof(XrCreatePassthroughLayerDelegate))]
		private static XrResult CreatePassthroughLayerHook(ulong session, IntPtr createInfo,
			out ulong layer)
		{
			XrResult result = originalCreateLayer(session, createInfo, out layer);
			if (result == XrResult.Success && layer != 0)
			{
				lock (sync)
				{
					layers.Add(layer);
					ApplyStyle(layer);
				}
			}

			return result;
		}

		[AOT.MonoPInvokeCallback(typeof(XrDestroyPassthroughLayerDelegate))]
		private static XrResult DestroyPassthroughLayerHook(ulong layer)
		{
			lock (sync)
				layers.Remove(layer);

			return originalDestroyLayer(layer);
		}

		private static bool ApplyStyleToAllLayers()
		{
			bool applied = false;
			foreach (ulong layer in layers)
				applied |= ApplyStyle(layer);

			return applied;
		}

		private static bool ApplyStyle(ulong layer)
		{
			if (setStyle == null || layer == 0)
				return false;

			IntPtr colorMapPointer = IntPtr.Zero;
			try
			{
				XrPassthroughStyle style = new()
				{
					type = PassthroughStyleType,
					next = IntPtr.Zero,
					textureOpacityFactor = opacity,
					edgeColor = new XrColor(edgeTint),
				};

				if (passthroughTintAmount > 0f)
				{
					XrPassthroughColorMapMonoToRgba colorMap = CreateColorMap();
					colorMapPointer = Marshal.AllocHGlobal(
						Marshal.SizeOf<XrPassthroughColorMapMonoToRgba>());
					Marshal.StructureToPtr(colorMap, colorMapPointer, false);
					style.next = colorMapPointer;
				}

				return setStyle(layer, ref style) == XrResult.Success;
			}
			finally
			{
				if (colorMapPointer != IntPtr.Zero)
				{
					Marshal.DestroyStructure<XrPassthroughColorMapMonoToRgba>(colorMapPointer);
					Marshal.FreeHGlobal(colorMapPointer);
				}
			}
		}

		private static XrPassthroughColorMapMonoToRgba CreateColorMap()
		{
			XrColor[] colors = new XrColor[ColorMapSize];
			Color tintScale = Color.Lerp(Color.white, passthroughTint, passthroughTintAmount);

			for (int i = 0; i < colors.Length; i++)
			{
				float luminance = i / (ColorMapSize - 1f);
				colors[i] = new XrColor(tintScale.r * luminance,
					tintScale.g * luminance, tintScale.b * luminance, 1f);
			}

			return new XrPassthroughColorMapMonoToRgba
			{
				type = PassthroughColorMapMonoToRgbaType,
				next = IntPtr.Zero,
				textureColorMap = colors,
			};
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrGetInstanceProcAddrDelegate(ulong instance, IntPtr name,
			out IntPtr function);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrCreatePassthroughLayerDelegate(ulong session,
			IntPtr createInfo, out ulong layer);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrDestroyPassthroughLayerDelegate(ulong layer);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrPassthroughLayerSetStyleDelegate(ulong layer,
			ref XrPassthroughStyle style);

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughStyle
		{
			public int type;
			public IntPtr next;
			public float textureOpacityFactor;
			public XrColor edgeColor;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughColorMapMonoToRgba
		{
			public int type;
			public IntPtr next;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = ColorMapSize)]
			public XrColor[] textureColorMap;
		}

		[StructLayout(LayoutKind.Sequential)]
		private readonly struct XrColor
		{
			public readonly float r;
			public readonly float g;
			public readonly float b;
			public readonly float a;

			public XrColor(Color color) : this(color.r, color.g, color.b, color.a) { }

			public XrColor(float r, float g, float b, float a)
			{
				this.r = r;
				this.g = g;
				this.b = b;
				this.a = a;
			}
		}
	}
}
