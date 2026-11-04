using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Development builds only, for measuring the keyframe graph on a headset. Every 10 s appends the graph's
	/// state, frame times and the process's CPU use to graphprobe.log beside the app's files, so an
	/// untethered walk leaves a record. Creating a file named graphbench there runs the pair and
	/// solve jobs on workers against synthetic data, stalling frames while it does.
	/// </summary>
	public class GraphDeviceProbe : MonoBehaviour
	{
		private const float Interval = 10f;
		private const string LogName = "graphprobe.log", BenchName = "graphbench", SnapshotBenchName = "snapbench";

		private string logPath, benchPath;
		private float nextLog;
		private int frames, slowFrames;
		private float frameSeconds, worstFrame;
		private double lastCpu;
		private float lastCpuAt;
		private bool benching;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Create()
		{
			if (Application.isEditor || !Debug.isDebugBuild) return;
			GameObject probe = new(nameof(GraphDeviceProbe));
			probe.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(probe);
			probe.AddComponent<GraphDeviceProbe>();
		}

		private void Awake()
		{
			logPath = Path.Combine(Application.persistentDataPath, LogName);
			benchPath = Path.Combine(Application.persistentDataPath, BenchName);
			Write($"--- started {DateTime.Now:u}, build {Application.version}, {SystemInfo.deviceModel}, refresh {Screen.currentResolution.refreshRateRatio.value:F0}, workers {JobsUtility()}");
			nextLog = Time.unscaledTime + Interval;
			lastCpu = ProcessCpuSeconds();
			lastCpuAt = Time.realtimeSinceStartup;
		}

		private static int JobsUtility() => Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;

		private void Update()
		{
			if (benching) return;

			float dt = Time.unscaledDeltaTime;
			frames++;
			frameSeconds += dt;
			worstFrame = Mathf.Max(worstFrame, dt);
			if (dt > 0.021f) slowFrames++;

			if (Time.unscaledTime < nextLog) return;
			nextLog = Time.unscaledTime + Interval;

			double cpu = ProcessCpuSeconds();
			float now = Time.realtimeSinceStartup;
			double cores = (cpu - lastCpu) / Mathf.Max(now - lastCpuAt, 0.001f);
			lastCpu = cpu;
			lastCpuAt = now;

			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Active;
			string alignment = provider != null ? $"{provider.Alignment.CurrentPhase} gen {provider.Alignment.Generation}" : "no provider";
			string graph = provider?.Graph != null ? provider.Graph.Summary : "no graph";
			string mapping = "";
			if (VisualLocalizer.Instance)
			{
				VisualMapper.Counts counts = VisualLocalizer.Instance.Mapping;
				mapping = $"{counts.added} keyframes, extract {counts.extractSeconds:F1} s, busy {counts.busySeconds:F1} s";
			}

			Write($"{DateTime.Now:HH:mm:ss} t={now:F0} fps={frames / Mathf.Max(frameSeconds, 0.001f):F1} worst={worstFrame * 1000:F0}ms slow={slowFrames}/{frames} cores={cores:F2} battery={SystemInfo.batteryLevel:F2} | {Colocator.Instance?.AlignmentState} {alignment} | {mapping} | {graph}");
			frames = slowFrames = 0;
			frameSeconds = worstFrame = 0;

			if (File.Exists(benchPath))
			{
				File.Delete(benchPath);
				StartCoroutine(Bench());
			}

			string snapshotBenchPath = Path.Combine(Application.persistentDataPath, SnapshotBenchName);
			if (File.Exists(snapshotBenchPath))
			{
				File.Delete(snapshotBenchPath);
				StartCoroutine(SnapshotBench());
			}
		}

		// utime + stime of this process
		private static double ProcessCpuSeconds()
		{
			try
			{
				string stat = File.ReadAllText("/proc/self/stat");
				string[] fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
				return (long.Parse(fields[11]) + long.Parse(fields[12])) / 100.0;
			}
			catch (Exception)
			{
				return 0;
			}
		}

		private void Write(string line)
		{
			try
			{
				File.AppendAllText(logPath, line + "\n");
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(GraphDeviceProbe)}] {e.Message}");
			}

			Debug.Log($"[{nameof(GraphDeviceProbe)}] {line}");
		}

		private IEnumerator Bench()
		{
			benching = true;
			Write($"--- bench {DateTime.Now:u}");
			Unity.Mathematics.Random rng = new(12345);

			foreach ((int countA, int countB, int shared) in new[] { (300, 512, 150), (300, 512, 0), (150, 512, 80) })
			{
				const int D = FeatureSet.DescriptorSize;
				NativeArray<half> descriptorsA = new(1024 * D, Allocator.Persistent), descriptorsB = new(1024 * D, Allocator.Persistent);
				NativeArray<float3> pointsA = new(1024, Allocator.Persistent), raysB = new(1024, Allocator.Persistent);
				NativeArray<KeyframeEdge> result = new(1, Allocator.Persistent);
				float3 center = new(0.2f, 0, -0.1f);
				for (int j = 0; j < countB; j++)
				{
					RandomUnit(ref rng, descriptorsB, j, D);
					float3 p = new(rng.NextFloat(-3, 3), rng.NextFloat(-1, 1), rng.NextFloat(2, 7));
					raysB[j] = math.normalize(p - center);
					if (j < shared)
					{
						pointsA[j] = p;
						NativeArray<half>.Copy(descriptorsB, j * D, descriptorsA, j * D, D);
					}
				}

				for (int i = shared; i < countA; i++)
				{
					RandomUnit(ref rng, descriptorsA, i, D);
					pointsA[i] = new float3(rng.NextFloat(-3, 3), rng.NextFloat(-1, 1), rng.NextFloat(2, 7));
				}

				double[] times = new double[30];
				for (int r = 0; r < times.Length; r++)
				{
					times[r] = Timed(new KeyframeEdgeJob
					{
						descriptorsA = descriptorsA, descriptorsB = descriptorsB, pointsA = pointsA, raysB = raysB,
						countA = countA, countB = countB, centerB = float3.zero, angularSigma = 0.004f,
						minInliers = 20, maxYawSigma = math.radians(1f), seed = (uint)(r + 1), result = result,
					}.Schedule());
					yield return null;
				}

				Write($"pair A={countA} B={countB} shared={shared}: {Stats(times)}, outcome {result[0].inliers}");
				descriptorsA.Dispose();
				descriptorsB.Dispose();
				pointsA.Dispose();
				raysB.Dispose();
				result.Dispose();
			}

			foreach (int knotCount in new[] { 100, 250, 500 })
			{
				const int Lap = 40;
				int edgeCount = knotCount * 3;
				NativeArray<KeyframeGraphKnot> knots = new(knotCount, Allocator.Persistent);
				NativeArray<KeyframeEdge> edges = new(edgeCount, Allocator.Persistent);
				NativeArray<double4> state = new(knotCount, Allocator.Persistent);
				NativeArray<double> step = new(1, Allocator.Persistent);
				for (int i = 0; i < knotCount; i++)
				{
					float a = 2 * math.PI * i / Lap;
					knots[i] = new KeyframeGraphKnot { origin = new float3(13 * math.cos(a), 0, 13 * math.sin(a)), path = 2 * i, previous = i - 1, next = i + 1 < knotCount ? i + 1 : -1, isFixed = i == 0 };
				}

				for (int i = 0; i < edgeCount; i++)
				{
					int b = rng.NextInt(Lap, knotCount);
					int a = (b % Lap + rng.NextInt(-1, 2) + Lap) % Lap + Lap * rng.NextInt(0, b / Lap);
					float3 at = knots[b].origin;
					edges[i] = new KeyframeEdge { knotA = a, knotB = b, pointA = at, pointB = at, positionInformation = 625, yawInformation = 1459, inliers = 50 };
				}

				NativeArray<KeyframeEdgeWeight> weights = new(edgeCount, Allocator.Persistent);
				for (int i = 0; i < edgeCount; i++) weights[i] = new KeyframeEdgeWeight { sight = new float3(1, 0, 0), along = 625, across = 625, height = 625, yaw = 1459 };

				double[] times = new double[12];
				for (int r = 0; r < times.Length; r++)
				{
					times[r] = Timed(new KeyframeGraphSolveJob
					{
						knots = knots, edges = edges, weights = weights, state = state, largestStep = step, positionRateSquared = 0.028 * 0.028,
						yawRateSquared = 7.6e-7, positionFloorSquared = 2.5e-5, yawFloorSquared = 1.2e-7, huber = 2,
					}.Schedule());
					yield return null;
				}

				Write($"solve step {knotCount} knots, {edgeCount} edges: {Stats(times)}");
				knots.Dispose();
				edges.Dispose();
				weights.Dispose();
				state.Dispose();
				step.Dispose();
			}

			Write($"--- bench done, burst {Unity.Burst.BurstCompiler.IsEnabled}");
			nextLog = Time.unscaledTime + Interval;
			benching = false;
		}

		private IEnumerator SnapshotBench()
		{
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Active;
			VisualMap map = VisualLocalizer.Instance ? VisualLocalizer.Instance.Map : null;
			if (provider == null || map == null)
			{
				Write("snapshot bench: no provider or map");
				yield break;
			}

			Write($"--- snapshot bench, {map.Count} keyframes, {map.LandmarkCount} landmarks, incremental GC {UnityEngine.Scripting.GarbageCollector.isIncremental}");
			for (int run = 0; run < 3; run++)
			{
				yield return new WaitForSeconds(2f);
				Stopwatch watch = Stopwatch.StartNew();
				int lookups = 0;
				foreach (VisualKeyframe k in map.Keyframes)
					for (int j = 0; j < k.features.Count; j++)
						if (map.TryGetLandmark(k.landmarks[j], out Landmark l) && l.IsUsable) lookups++;
				double lookupMs = watch.Elapsed.TotalMilliseconds;

				long before = GC.GetTotalMemory(false);
				watch.Restart();
				VisualMapData data = provider.SnapshotMap();
				double snapshotMs = watch.Elapsed.TotalMilliseconds;
				long after = GC.GetTotalMemory(false);

				watch.Restart();
				byte[] graph = provider.Graph.Encode();
				double encodeMs = watch.Elapsed.TotalMilliseconds;

				Write($"snapshot run {run}: {snapshotMs:F1} ms whole, of which one lookup pass {lookupMs:F1} ms ({lookups} usable), graph encode {encodeMs:F2} ms ({graph.Length} bytes), managed heap grew {(after - before) / 1048576f:F1} MB, keyframes {data.keyframes.Count}, landmarks {data.landmarks.Count}");
				yield return null;

				watch.Restart();
				data = null;
				GC.Collect();
				Write($"full GC after: {watch.Elapsed.TotalMilliseconds:F1} ms");
			}

			Write("--- snapshot bench done");
		}

		// polling leaves the job to a worker, where it runs for real, rather than this thread taking it
		private static double Timed(JobHandle handle)
		{
			Stopwatch watch = Stopwatch.StartNew();
			JobHandle.ScheduleBatchedJobs();
			while (!handle.IsCompleted) { }
			double ms = watch.Elapsed.TotalMilliseconds;
			handle.Complete();
			return ms;
		}

		private static void RandomUnit(ref Unity.Mathematics.Random rng, NativeArray<half> into, int row, int size)
		{
			float[] v = new float[size];
			float norm = 0;
			for (int c = 0; c < size; c++)
			{
				v[c] = rng.NextFloat(-1, 1);
				norm += v[c] * v[c];
			}

			norm = math.sqrt(norm);
			for (int c = 0; c < size; c++) into[row * size + c] = (half)(v[c] / norm);
		}

		private static string Stats(double[] times)
		{
			double[] sorted = (double[])times.Clone();
			Array.Sort(sorted);
			return $"best {sorted[0]:F2} median {sorted[sorted.Length / 2]:F2} worst {sorted[^1]:F2} ms";
		}
	}
}
