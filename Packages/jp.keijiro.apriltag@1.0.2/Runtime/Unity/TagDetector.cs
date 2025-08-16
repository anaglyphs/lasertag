using Unity.Collections;
using Unity.Jobs;
using System;
using System.Collections.Generic;
using Color32 = UnityEngine.Color32;
using System.Threading.Tasks;
using UnityEngine;

namespace AprilTag
{

	//
	// Multithreaded tag detector and pose estimator
	//
	public sealed class TagDetector : System.IDisposable
	{
		public Action OnDetect = delegate { };

		#region Public properties

		public IEnumerable<TagPose> DetectedTags
		  => _detectedTags;

		public IEnumerable<(string name, long time)> ProfileData
		  => _profileData ?? (_profileData = GenerateProfileData());

		#endregion

		#region Constructor

		public TagDetector(int width, int height, int decimation)
		{
			// Object creation
			_detector = Interop.Detector.Create();
			_family = Interop.Family.CreateTagStandard41h12();
			_image = Interop.ImageU8.CreateStride(width, height, width);

			// Detector configuration
			_detector.ThreadCount = SystemConfig.PreferredThreadCount;
			_detector.QuadDecimate = decimation;
			_detector.AddFamily(_family);
		}

		#endregion

		#region Public methods

		public void Dispose()
		{
			_detector?.RemoveFamily(_family);
			_detector?.Dispose();
			_family?.Dispose();
			_image?.Dispose();

			_detector = null;
			_family = null;
			_image = null;
		}

		//public void ProcessImage
		//  (ReadOnlySpan<Color32> image, float fov, float tagSize)
		//{
		//	ImageConverter.Convert(image, _image);
		//	RunDetectorAndEstimator(fov, tagSize);
		//}

		public async Task Detect(NativeArray<byte> imgBytes, float fov, float tagSize)
		{
			imgBytes.AsReadOnlySpan().CopyTo(_image.Buffer);

			_profileData = null;

			// Run the AprilTag detector.

			Interop.DetectionArray tags = await Task.Run(() =>
			{
				// Thread-safety check: this assumes _detector is not accessed anywhere else concurrently
				return _detector.Detect(_image);
			});

			var tagCount = tags.Length;

			// Convert the detector output into a NativeArray to make them
			// accessible from the pose estimation job.
			using var jobInput = new NativeArray<PoseEstimationJob.Input>
			  (tagCount, Allocator.TempJob);

			var slice = new NativeSlice<PoseEstimationJob.Input>(jobInput);

			for (var i = 0; i < tagCount; i++)
				slice[i] = new PoseEstimationJob.Input(ref tags[i]);

			// Pose estimation output buffer
			using var jobOutput
			  = new NativeArray<TagPose>(tagCount, Allocator.TempJob);

			// Pose estimation job
			var job = new PoseEstimationJob
			  (jobInput, jobOutput, _image.Width, _image.Height, fov, tagSize);

			var handle = job.Schedule(tagCount, 1, default);

			while (!handle.IsCompleted) await Awaitable.NextFrameAsync();

			handle.Complete();

			jobOutput.CopyTo(_detectedTags);
		}

		#endregion

		#region Private objects

		Interop.Detector _detector;
		Interop.Family _family;
		Interop.ImageU8 _image;

		List<TagPose> _detectedTags = new List<TagPose>();
		List<(string, long)> _profileData;

		#endregion

		#region Detection/estimation procedure

		//
		// We can simply use the multithreaded AprilTag detector for tag detection.
		//
		// In contrast, AprilTag only provides single-threaded pose estimator, so
		// we have to manage threading ourselves.
		//
		// We don't want to spawn extra threads just for it, so we run them on
		// Unity's job system. It's a bit complicated due to "impedance mismatch"
		// things (unmanaged vs managed vs Unity DOTS).

		//void RunDetectorAndEstimator(float fov, float tagSize)
		//{
		//	_profileData = null;

		//	// Run the AprilTag detector.
		//	using var tags = _detector.Detect(_image);
		//	var tagCount = tags.Length;

		//	// Convert the detector output into a NativeArray to make them
		//	// accessible from the pose estimation job.
		//	using var jobInput = new NativeArray<PoseEstimationJob.Input>
		//	  (tagCount, Allocator.TempJob);

		//	var slice = new NativeSlice<PoseEstimationJob.Input>(jobInput);

		//	for (var i = 0; i < tagCount; i++)
		//		slice[i] = new PoseEstimationJob.Input(ref tags[i]);

		//	// Pose estimation output buffer
		//	using var jobOutput
		//	  = new NativeArray<TagPose>(tagCount, Allocator.TempJob);

		//	// Pose estimation job
		//	var job = new PoseEstimationJob
		//	  (jobInput, jobOutput, _image.Width, _image.Height, fov, tagSize);

		//	// Run and wait the jobs.
		//	job.Schedule(tagCount, 1, default(JobHandle)).Complete();

		//	// Job output -> managed list
		//	jobOutput.CopyTo(_detectedTags);

		//}

		#endregion

		#region Profile data aggregation

		List<(string, long)> GenerateProfileData()
		{
			var list = new List<(string, long)>();
			var stamps = _detector.TimeProfile.Stamps;
			var time = _detector.TimeProfile.UTime;
			for (var i = 0; i < stamps.Length; i++)
			{
				var stamp = stamps[i];
				list.Add((stamp.Name, stamp.UTime - time));
				time = stamp.UTime;
			}
			return list;
		}

		#endregion
	}

} // namespace AprilTag
