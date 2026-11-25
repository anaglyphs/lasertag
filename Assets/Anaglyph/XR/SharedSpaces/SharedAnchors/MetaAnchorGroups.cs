using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if !UNITY_IOS && !UNITY_STANDALONE_LINUX || UNITY_EDITOR
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	/// <summary>
	/// Meta's anchor groups, the only runtime here that shares anchors. Unsupported wherever Meta's
	/// OpenXR package isn't built, as on iOS, and under any other anchor runtime.
	/// </summary>
	public static class MetaAnchorGroups
	{
		public static Supported SharingSupport(XRAnchorSubsystem anchors) =>
#if !UNITY_IOS && !UNITY_STANDALONE_LINUX || UNITY_EDITOR
			anchors is MetaOpenXRAnchorSubsystem meta ? meta.isSharedAnchorsSupported : Supported.Unsupported;
#else
			Supported.Unsupported;
#endif

		/// <summary>Shares <paramref name="anchor"/> into <paramref name="group"/>.</summary>
		public static async Awaitable<XRResultStatus> ShareAsync(ARAnchorManager manager, ARAnchor anchor, SerializableGuid group)
		{
#if !UNITY_IOS && !UNITY_STANDALONE_LINUX || UNITY_EDITOR
			// the subsystem reads the group synchronously, before the share's Awaitable returns
			if (manager.subsystem is MetaOpenXRAnchorSubsystem meta)
			{
				meta.sharedAnchorsGroupId = group;
				return await manager.TryShareAnchorAsync(anchor);
			}
#endif
			await Awaitable.NextFrameAsync();
			return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);
		}

		/// <summary>Downloads every anchor shared into <paramref name="group"/>.</summary>
		public static async Awaitable<XRResultStatus> LoadAllAsync(ARAnchorManager manager, SerializableGuid group, List<XRAnchor> into)
		{
#if !UNITY_IOS && !UNITY_STANDALONE_LINUX || UNITY_EDITOR
			if (manager.subsystem is MetaOpenXRAnchorSubsystem meta)
			{
				meta.sharedAnchorsGroupId = group;
				return await manager.TryLoadAllSharedAnchorsAsync(into, null);
			}
#endif
			await Awaitable.NextFrameAsync();
			return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);
		}
	}
}
