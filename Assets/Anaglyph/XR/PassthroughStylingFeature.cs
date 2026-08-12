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

namespace Anaglyph.XR
{
	/// <summary>
	/// Styles the passthrough composition layer created internally by the Meta
	/// OpenXR AR Foundation provider. This is the XR_FB_passthrough equivalent of
	/// the basic tint and edge controls on OVRPassthroughLayer. Color tinting uses
	/// XR_META_passthrough_color_lut so it can blend from the original RGB image
	/// without first converting passthrough to grayscale.
	/// </summary>
#if UNITY_EDITOR
	[OpenXRFeature(UiName = "AR Foundation Passthrough Styling",
		BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
		Company = "Anaglyph",
		Desc = "Applies RGB color LUT and edge tinting to the passthrough " +
		       "composition layer owned by the Meta OpenXR AR Foundation provider.",
		Category = FeatureCategory.Feature,
		FeatureId = featureId,
		OpenxrExtensionStrings = passthroughExtension + " " + colorLutExtension,
		Priority = 100,
		Version = "1.2.0")]
#endif
	public sealed class PassthroughStylingFeature : OpenXRFeature
	{
		public const string featureId = "com.anaglyph.xrtemplate.passthrough-styling";
		public const string passthroughExtension = "XR_FB_passthrough";
		public const string colorLutExtension = "XR_META_passthrough_color_lut";

		private const int PassthroughStyleType = 1000118020;
		private const int PassthroughColorLutCreateInfoType = 1000266001;
		private const int PassthroughColorLutUpdateInfoType = 1000266002;
		private const int PassthroughColorMapLutType = 1000266100;
		private const uint ColorLutResolution = 32;
		private const int ColorLutChannelsRgb = 1;
		private const int ColorLutChannelCount = 3;

		private const string CreateLayerFunctionName = "xrCreatePassthroughLayerFB";
		private const string DestroyLayerFunctionName = "xrDestroyPassthroughLayerFB";
		private const string SetStyleFunctionName = "xrPassthroughLayerSetStyleFB";
		private const string CreateColorLutFunctionName = "xrCreatePassthroughColorLutMETA";
		private const string UpdateColorLutFunctionName = "xrUpdatePassthroughColorLutMETA";
		private const string DestroyColorLutFunctionName = "xrDestroyPassthroughColorLutMETA";

		private static readonly object sync = new();
		private static readonly Dictionary<ulong, LayerState> layers = new();

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
		private static XrCreatePassthroughColorLutDelegate createColorLut;
		private static XrUpdatePassthroughColorLutDelegate updateColorLut;
		private static XrDestroyPassthroughColorLutDelegate destroyColorLut;

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

		/// <summary>True when the active runtime exposes XR_META_passthrough_color_lut.</summary>
		public static bool IsColorLutSupported => createColorLut != null &&
			updateColorLut != null && destroyColorLut != null;

		/// <summary>
		/// Blends the original RGB passthrough image toward a luminance-preserving
		/// monochrome tint and applies an edge tint. A tint amount of zero keeps the
		/// original camera colors; one maps each input color's luminance into
		/// <paramref name="tint"/>. Edge rendering is disabled when
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

		/// <summary>
		/// Changes only the RGB colorization. The alpha channel of
		/// <paramref name="tint"/> is ignored; use <paramref name="tintAmount"/> for
		/// the blend and SetStyle's textureOpacity for passthrough opacity.
		/// </summary>
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

			if (OpenXRRuntime.IsExtensionEnabled(colorLutExtension))
			{
				bool resolved =
					TryGetFunction(xrInstance, CreateColorLutFunctionName, out createColorLut) &&
					TryGetFunction(xrInstance, UpdateColorLutFunctionName, out updateColorLut) &&
					TryGetFunction(xrInstance, DestroyColorLutFunctionName, out destroyColorLut);

				if (!resolved)
				{
					createColorLut = null;
					updateColorLut = null;
					destroyColorLut = null;
					Debug.LogWarning($"{nameof(PassthroughStylingFeature)}: " +
					                 $"could not resolve {colorLutExtension} functions; " +
					                 "edge styling will remain available");
				}
			}
			else
			{
				Debug.LogWarning($"{nameof(PassthroughStylingFeature)}: " +
				                 $"{colorLutExtension} is unavailable; " +
				                 "edge styling will remain available");
			}

			return true;
		}

		protected override void OnInstanceDestroy(ulong xrInstance)
		{
			lock (sync)
			{
				foreach (LayerState layerState in layers.Values)
					FreeColorLutMap(layerState);
				layers.Clear();
			}

			setStyle = null;
			createColorLut = null;
			updateColorLut = null;
			destroyColorLut = null;
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
			if (result == XrResult.Success && layer != 0 && createInfo != IntPtr.Zero)
			{
				XrPassthroughLayerCreateInfo layerCreateInfo =
					Marshal.PtrToStructure<XrPassthroughLayerCreateInfo>(createInfo);

				lock (sync)
				{
					layers[layer] = new LayerState(layerCreateInfo.passthrough);
					ApplyStyle(layer);
				}
			}

			return result;
		}

		[AOT.MonoPInvokeCallback(typeof(XrDestroyPassthroughLayerDelegate))]
		private static XrResult DestroyPassthroughLayerHook(ulong layer)
		{
			LayerState layerState;
			lock (sync)
			{
				layers.TryGetValue(layer, out layerState);
				layers.Remove(layer);
			}

			XrResult result = originalDestroyLayer(layer);
			FreeColorLutMap(layerState);
			if (layerState?.colorLut != 0 && destroyColorLut != null)
				destroyColorLut(layerState.colorLut);

			return result;
		}

		private static bool ApplyStyleToAllLayers()
		{
			bool applied = false;
			foreach (ulong layer in layers.Keys)
				applied |= ApplyStyle(layer);

			return applied;
		}

		private static bool ApplyStyle(ulong layer)
		{
			if (setStyle == null || layer == 0 || !layers.TryGetValue(layer, out LayerState layerState))
				return false;

			bool colorLutReady = passthroughTintAmount <= 0f;
			XrPassthroughStyle style = new()
			{
				type = PassthroughStyleType,
				next = IntPtr.Zero,
				textureOpacityFactor = opacity,
				edgeColor = new XrColor(edgeTint),
			};

			if (passthroughTintAmount > 0f)
			{
				colorLutReady = EnsureColorLut(layerState);
				if (colorLutReady)
				{
					XrPassthroughColorMapLut colorMap = new()
					{
						type = PassthroughColorMapLutType,
						next = IntPtr.Zero,
						colorLut = layerState.colorLut,
						weight = passthroughTintAmount,
					};

					if (layerState.colorLutMapPointer == IntPtr.Zero)
						layerState.colorLutMapPointer = Marshal.AllocHGlobal(
							Marshal.SizeOf<XrPassthroughColorMapLut>());

					Marshal.StructureToPtr(colorMap, layerState.colorLutMapPointer, false);
					style.next = layerState.colorLutMapPointer;
				}
			}

			return setStyle(layer, ref style) == XrResult.Success && colorLutReady;
		}

		private static void FreeColorLutMap(LayerState layerState)
		{
			if (layerState == null || layerState.colorLutMapPointer == IntPtr.Zero)
				return;

			Marshal.FreeHGlobal(layerState.colorLutMapPointer);
			layerState.colorLutMapPointer = IntPtr.Zero;
		}

		private static bool EnsureColorLut(LayerState layerState)
		{
			if (!IsColorLutSupported || layerState.passthrough == 0)
				return false;

			if (layerState.colorLut != 0 && layerState.hasUploadedTint &&
			    SameRgb(layerState.uploadedTint, passthroughTint))
				return true;

			byte[] data = CreateLuminanceTintLut(passthroughTint);
			GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
			try
			{
				XrPassthroughColorLutData lutData = new()
				{
					bufferSize = (uint)data.Length,
					buffer = dataHandle.AddrOfPinnedObject(),
				};

				XrResult result;
				if (layerState.colorLut == 0)
				{
					XrPassthroughColorLutCreateInfo createInfo = new()
					{
						type = PassthroughColorLutCreateInfoType,
						next = IntPtr.Zero,
						channels = ColorLutChannelsRgb,
						resolution = ColorLutResolution,
						data = lutData,
					};

					result = createColorLut(layerState.passthrough, ref createInfo,
						out layerState.colorLut);
				}
				else
				{
					XrPassthroughColorLutUpdateInfo updateInfo = new()
					{
						type = PassthroughColorLutUpdateInfoType,
						next = IntPtr.Zero,
						data = lutData,
					};

					result = updateColorLut(layerState.colorLut, ref updateInfo);
				}

				if (result != XrResult.Success)
					return false;

				layerState.uploadedTint = passthroughTint;
				layerState.hasUploadedTint = true;
				return true;
			}
			finally
			{
				dataHandle.Free();
			}
		}

		private static byte[] CreateLuminanceTintLut(Color tint)
		{
			int resolution = (int)ColorLutResolution;
			int entryCount = resolution * resolution * resolution;
			byte[] data = new byte[entryCount * ColorLutChannelCount];

			// The extension defines LUT inputs and outputs in sRGB. Compute luminance
			// in linear space, then encode the tinted result back to sRGB.
			for (int blueIndex = 0; blueIndex < resolution; blueIndex++)
			{
				float blueLinear = Mathf.GammaToLinearSpace(blueIndex / (resolution - 1f));
				for (int greenIndex = 0; greenIndex < resolution; greenIndex++)
				{
					float greenLinear = Mathf.GammaToLinearSpace(greenIndex / (resolution - 1f));
					for (int redIndex = 0; redIndex < resolution; redIndex++)
					{
						float redLinear = Mathf.GammaToLinearSpace(redIndex / (resolution - 1f));
						float luminanceLinear = 0.2126f * redLinear +
						                        0.7152f * greenLinear +
						                        0.0722f * blueLinear;
						float luminanceSrgb = Mathf.LinearToGammaSpace(luminanceLinear);

						int entryIndex = redIndex + greenIndex * resolution +
						                 blueIndex * resolution * resolution;
						int byteIndex = entryIndex * ColorLutChannelCount;
						data[byteIndex] = FloatToSrgbByte(luminanceSrgb * tint.r);
						data[byteIndex + 1] = FloatToSrgbByte(luminanceSrgb * tint.g);
						data[byteIndex + 2] = FloatToSrgbByte(luminanceSrgb * tint.b);
					}
				}
			}

			return data;
		}

		private static byte FloatToSrgbByte(float value) =>
			(byte)Mathf.RoundToInt(Mathf.Clamp01(value) * byte.MaxValue);

		private static bool SameRgb(Color a, Color b) =>
			a.r == b.r && a.g == b.g && a.b == b.b;

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

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrCreatePassthroughColorLutDelegate(ulong passthrough,
			ref XrPassthroughColorLutCreateInfo createInfo, out ulong colorLut);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrUpdatePassthroughColorLutDelegate(ulong colorLut,
			ref XrPassthroughColorLutUpdateInfo updateInfo);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate XrResult XrDestroyPassthroughColorLutDelegate(ulong colorLut);

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughStyle
		{
			public int type;
			public IntPtr next;
			public float textureOpacityFactor;
			public XrColor edgeColor;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughLayerCreateInfo
		{
			public int type;
			public IntPtr next;
			public ulong passthrough;
			public ulong flags;
			public uint purpose;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughColorLutData
		{
			public uint bufferSize;
			public IntPtr buffer;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughColorLutCreateInfo
		{
			public int type;
			public IntPtr next;
			public int channels;
			public uint resolution;
			public XrPassthroughColorLutData data;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughColorLutUpdateInfo
		{
			public int type;
			public IntPtr next;
			public XrPassthroughColorLutData data;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XrPassthroughColorMapLut
		{
			public int type;
			public IntPtr next;
			public ulong colorLut;
			public float weight;
		}

		private sealed class LayerState
		{
			public readonly ulong passthrough;
			public ulong colorLut;
			public IntPtr colorLutMapPointer;
			public Color uploadedTint;
			public bool hasUploadedTint;

			public LayerState(ulong passthrough)
			{
				this.passthrough = passthrough;
			}
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
