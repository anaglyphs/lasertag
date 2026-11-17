using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR
{
	/// <summary>Whether this session runs on a phone or tablet held in hand (ARKit or ARCore) rather than a headset.</summary>
	public static class HandheldAR
	{
		public enum Provider { None, ARKit, ARCore }

		private static readonly List<XRSessionSubsystem> sessions = new();

		private static Provider runtime;
		private static bool runtimeIsKnown;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ForgetRuntime() => runtimeIsKnown = false;

		/// <summary>The handheld AR runtime whose session subsystem exists, once XR has started; None on headsets, in the editor and before XR starts.</summary>
		public static Provider Runtime
		{
			get
			{
				if (runtimeIsKnown) return runtime;

				SubsystemManager.GetSubsystems(sessions);
				runtime = Provider.None;
				foreach (XRSessionSubsystem session in sessions)
				{
					string id = session.subsystemDescriptor.id;
					if (id.Contains("ARKit")) runtime = Provider.ARKit;
					else if (id.Contains("ARCore")) runtime = Provider.ARCore;
					else continue;
					break;
				}

				// a session subsystem exists once XR has started, and which one never changes after
				runtimeIsKnown = sessions.Count > 0;
				return runtime;
			}
		}

		/// <summary>The editor plays as a phone over XR Simulation: phone UI and input, without a phone's camera poses or depth.</summary>
		public static bool SimulatedInEditor { get; set; }

		public static bool IsActive => Runtime != Provider.None || SimulatedInEditor && Application.isEditor;
	}
}
