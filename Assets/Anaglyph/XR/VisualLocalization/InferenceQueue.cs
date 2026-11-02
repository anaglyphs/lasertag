using System;
using Unity.InferenceEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// The camera and the networks, which mapping, queries and localization all take turns on.
	/// One capture-and-extract at a time (they share the image buffer), and one LighterGlue run
	/// at a time: separately, so mapping goes on while a loop query matches.
	/// </summary>
	public sealed class InferenceQueue : IDisposable
	{
		[Serializable]
		public sealed class Settings
		{
			[Tooltip("Where XFeat runs: GPUCompute, or CPU (Burst jobs on worker threads) to leave the GPU to rendering")]
			public BackendType extractorBackend = BackendType.GPUCompute;
			[Tooltip("Where LighterGlue runs. CPU measured 16 ms at 512 points a side on an M-series Mac (GPU 9 ms); expect several times that on Quest, on worker threads")]
			public BackendType matcherBackend = BackendType.GPUCompute;
			[Tooltip("Frames each network run is spread over, so no one frame carries a whole network; 1 runs it at once")]
			public int framesPerRun = 6;
			[Tooltip("Frames each XFeat extraction is spread over while the headset is waiting on an alignment (relocalizing, joining): latency matters more then")]
			public int urgentFramesPerRun = 2;
			[Tooltip("Frames each LighterGlue match is spread over outside urgent alignments (loop closure). It is ~700 layers and the heaviest network; spreading it thin keeps background queries out of the frame time")]
			public int matcherFramesPerRun = 30;
			[Tooltip("Frames each LighterGlue match is spread over while something waits on it (a relocalization, a joiner's query): its ~700 layers in a couple of frames stall them, both in dispatching on this thread and on the GPU")]
			public int urgentMatcherFramesPerRun = 10;
			[Tooltip("Keyframes scored per frame when retrieving the views a query might share, each a few million multiply-adds on a worker thread; without a camera to narrow it, a query is scored against the whole map")]
			public int retrievalCandidatesPerFrame = VisualMap.DefaultCandidatesPerFrame;
			[Tooltip("Keyframes scored per frame while something waits on the result (a relocalization, a joiner's query): the whole map in a second or so, each slice holding every worker for a few milliseconds")]
			public int urgentRetrievalCandidatesPerFrame = 8;
		}

		private readonly UnityEngine.Object owner;
		private readonly Settings settings;
		private ARCameraManager cameraManager;
		private PassthroughCapture capture;
		private XFeatExtractor extractor;
		private LighterGlueMatcher matcher;
		private long? lastFrameTimestampNs;
		private bool urgent, matcherBusy;

		public InferenceQueue(UnityEngine.Object owner, Settings settings)
		{
			this.owner = owner;
			this.settings = settings;
		}

		public bool IsAvailable => capture != null;
		public bool ExtractorBusy { get; private set; }

		/// <summary>The frame last captured. Whoever holds the extractor may read it; the buffer is reused.</summary>
		public CapturedImage Image { get; private set; }

		/// <summary>
		/// Something is waiting on an alignment (relocalizing, a joiner searching): extraction
		/// runs over <see cref="Settings.urgentFramesPerRun"/> frames instead of
		/// <see cref="Settings.framesPerRun"/>, and matching and retrieval at their urgent paces.
		/// </summary>
		public bool Urgent
		{
			get => urgent;
			set
			{
				urgent = value;
				if (extractor != null) extractor.FramesPerRun = value ? settings.urgentFramesPerRun : settings.framesPerRun;
			}
		}

		/// <summary>Opens the camera and loads the networks. False when they couldn't be.</summary>
		public bool TryStart(ARCameraManager cameraManager)
		{
			this.cameraManager = cameraManager;
			cameraManager.frameReceived += OnFrameReceived;
			capture = new PassthroughCapture(cameraManager);
			Image = new CapturedImage();
			try
			{
				extractor = new XFeatExtractor(settings.extractorBackend, settings.framesPerRun);
				matcher = new LighterGlueMatcher(settings.matcherBackend, settings.matcherFramesPerRun);
				Debug.Log($"[{nameof(VisualLocalizer)}] XFeat on {settings.extractorBackend} over {settings.framesPerRun} frames ({settings.urgentFramesPerRun} when urgent), LighterGlue on {settings.matcherBackend} over {settings.matcherFramesPerRun} ({settings.urgentMatcherFramesPerRun} when urgent)");
				return true;
			}
			catch (Exception e)
			{
				Debug.LogException(e, owner);
				capture = null;
				return false;
			}
		}

		private void OnFrameReceived(ARCameraFrameEventArgs args) => lastFrameTimestampNs = args.timestampNs;

		public void Dispose()
		{
			if (cameraManager) cameraManager.frameReceived -= OnFrameReceived;
			capture?.Dispose();
			Image?.Dispose();
			extractor?.Dispose();
			matcher?.Dispose();
		}

		/// <summary>The current frame into <see cref="Image"/>; hold the extractor first. False when there is none, or the head turns faster than <paramref name="maxAngularSpeed"/>.</summary>
		public bool TryCapture(float maxAngularSpeed) => capture.TryCapture(Image, lastFrameTimestampNs, maxAngularSpeed);

		/// <summary>Waits a turn on the extractor. False when the owner was destroyed meanwhile.</summary>
		public async Awaitable<bool> AcquireExtractor()
		{
			while (ExtractorBusy)
			{
				await Awaitable.NextFrameAsync();
				if (!owner) return false;
			}

			ExtractorBusy = true;
			return true;
		}

		public void ReleaseExtractor() => ExtractorBusy = false;

		/// <summary>Waits a turn on the matcher. False when the owner was destroyed meanwhile.</summary>
		public async Awaitable<bool> AcquireMatcher()
		{
			while (matcherBusy)
			{
				await Awaitable.NextFrameAsync();
				if (!owner) return false;
			}

			matcherBusy = true;
			return true;
		}

		public void ReleaseMatcher() => matcherBusy = false;

		public Awaitable<FeatureSet> ExtractAsync(NativeArray<float> image, float4 content) => extractor.ExtractAsync(image, content);

		// matching and retrieval take their pace per call: a query something waits on is urgent
		// whatever this headset's state
		public Awaitable<(int[] matches, float[] confidence)> MatchAsync(FeatureSet a, FeatureSet b, bool urgent) =>
			matcher.MatchAsync(a, b, urgent ? settings.urgentMatcherFramesPerRun : settings.matcherFramesPerRun);

		public int RetrievalCandidatesPerFrame(bool urgent) => urgent ? settings.urgentRetrievalCandidatesPerFrame : settings.retrievalCandidatesPerFrame;
	}
}
