using System;
using System.Collections.Generic;
using System.IO;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;

namespace Anaglyph.XR.DepthKit.EnvScanning.Slam
{
	public enum EdgeKind : byte
	{
		/// <summary>Consecutive nodes of one headset's trajectory, measured by its tracking.</summary>
		Odometry = 0,

		/// <summary>A registration found a revisited place: the current node against an old one.</summary>
		Loop = 1,

		/// <summary>A new trajectory segment (colocation, relocalization) tied to the map it was aligned to.</summary>
		Attach = 2
	}

	/// <summary>
	/// A pose in one headset's trajectory. <see cref="odometry"/> is where that headset's
	/// own tracking put it (before any drift correction) and never changes; <see cref="world"/>
	/// is where the shared graph currently says it is.
	/// </summary>
	public sealed class PoseNode
	{
		public ulong key;
		/// <summary>Insertion order in this replica; the solver orders its envelope by it.</summary>
		public int order;
		public ulong previous;
		public bool segmentStart;
		public Pose4 odometry;
		public Pose4 world;
		/// <summary>A gauge prior holds this node in place: the session's first node.</summary>
		public bool hasPrior;

		/// <summary>world ∘ odometry⁻¹: what the headset's tracking frame needs to land in the shared frame here.</summary>
		public Pose4 Correction => world * odometry.Inverse;
	}

	public struct PoseEdge
	{
		public ulong id;
		public ulong from;
		public ulong to;
		/// <summary>`to` expressed in `from`'s frame.</summary>
		public Pose4 measurement;
		public float sigmaTranslation; // meters
		public float sigmaYaw; // radians
		public EdgeKind kind;
		public bool rejected;
	}

	/// <summary>
	/// The shared pose graph as one headset holds it. Every headset keeps a replica:
	/// nodes and edges are broadcast by whoever creates them, and the session authority
	/// solves and broadcasts the result. Chunks of the environment scan are anchored to
	/// nodes, which is how a solve moves the map, and how far a node is from a chunk's
	/// anchor (in expected drift, along the graph) decides whether a frame from that node
	/// may integrate into it.
	/// </summary>
	public sealed class PoseGraph
	{
		/// <summary>The replica everything in this process shares.</summary>
		public static PoseGraph Shared { get; } = new();

		// expected drift of the tracking between nodes, the unit trust is measured in.
		// ~1% of distance travelled plus what a yaw error swings at the lever arm
		public float DriftPerMeter = 0.01f;
		public float DriftPerRadian = 0.5f;
		public float MinOdometryDrift = 0.005f;
		public float MinYawDriftDegrees = 0.2f;
		public float YawDriftDegreesPerMeter = 0.3f;
		// a confirmed registration is good to a few centimeters
		public float LoopDrift = 0.03f;

		private readonly Dictionary<ulong, PoseNode> nodes = new();
		private readonly List<PoseNode> ordered = new();
		private readonly Dictionary<ulong, PoseEdge> edges = new();
		private readonly Dictionary<ulong, List<ulong>> adjacency = new();
		private readonly Dictionary<int, ulong> chunkAnchors = new();
		private readonly Dictionary<uint, ulong> latestByPlayer = new();
		private int nextOrder;

		/// <summary>The authority's solve counter; every solve it broadcasts bumps it.</summary>
		public int Version { get; private set; }

		/// <summary>Bumped by any change to nodes, edges or poses; caches key on it.</summary>
		public int Revision { get; private set; }

		public IReadOnlyList<PoseNode> Nodes => ordered;
		public IEnumerable<PoseEdge> Edges => edges.Values;
		public int NodeCount => ordered.Count;
		public int EdgeCount => edges.Count;
		public IReadOnlyDictionary<int, ulong> ChunkAnchors => chunkAnchors;

		public event Action Cleared = delegate { };
		public event Action<PoseNode> NodeAdded = delegate { };
		public event Action<PoseEdge> EdgeAdded = delegate { };

		/// <summary>A solve was applied; the argument holds every moved node's pose before it.</summary>
		public event Action<IReadOnlyDictionary<ulong, Pose4>> Solved = delegate { };

		// ---- keys --------------------------------------------------------------

		public static ulong MakeKey(uint player, uint sequence) => (ulong)player << 32 | sequence;
		public static uint PlayerOf(ulong key) => (uint)(key >> 32);
		public static uint SequenceOf(ulong key) => (uint)key;

		// ---- queries -----------------------------------------------------------

		public bool TryGetNode(ulong key, out PoseNode node) => nodes.TryGetValue(key, out node);
		public bool Contains(ulong key) => nodes.ContainsKey(key);
		public bool TryGetEdge(ulong id, out PoseEdge edge) => edges.TryGetValue(id, out edge);

		/// <summary>The most recent node a player has created: its active node.</summary>
		public bool TryGetLatest(uint player, out PoseNode node)
		{
			node = null;
			return latestByPlayer.TryGetValue(player, out ulong key) && nodes.TryGetValue(key, out node);
		}

		public bool TryGetAnchor(int chunkIndex, out ulong node) => chunkAnchors.TryGetValue(chunkIndex, out node);

		/// <summary>The node nearest a world point by its current pose, or null with no nodes.</summary>
		public PoseNode Nearest(float3 world)
		{
			PoseNode best = null;
			float bestDist = float.MaxValue;
			foreach (PoseNode node in ordered)
			{
				float d = math.distancesq(node.world.translation, world);
				if (d < bestDist) { bestDist = d; best = node; }
			}

			return best;
		}

		/// <summary>
		/// The node a world point moves with: the anchor of the chunk it lies in, else the
		/// nearest node. 0 when the graph is empty
		/// </summary>
		public ulong AnchorFor(float3 world, EnvScanner scanner)
		{
			if (scanner != null)
			{
				int chunk = scanner.ChunkCoordToChunkIndex(scanner.WorldPosToChunkCoord(world));
				if (chunk >= 0 && chunkAnchors.TryGetValue(chunk, out ulong anchor) && nodes.ContainsKey(anchor)) return anchor;
			}

			return Nearest(world)?.key ?? 0;
		}

		// ---- mutation ----------------------------------------------------------

		public void Clear()
		{
			nodes.Clear();
			ordered.Clear();
			edges.Clear();
			adjacency.Clear();
			chunkAnchors.Clear();
			latestByPlayer.Clear();
			nextOrder = 0;
			Version = 0;
			Revision++;
			driftCache.Clear();
			Cleared.Invoke();
		}

		/// <summary>Adds a node and, when it continues a segment, the odometry edge from its predecessor.</summary>
		public bool AddNode(ulong key, ulong previous, bool segmentStart, Pose4 odometry, Pose4 world, bool hasPrior)
		{
			if (key == 0 || nodes.ContainsKey(key)) return false;

			PoseNode node = new()
			{
				key = key, order = nextOrder++, previous = previous, segmentStart = segmentStart,
				odometry = odometry, world = world, hasPrior = hasPrior
			};
			nodes.Add(key, node);
			ordered.Add(node);
			adjacency[key] = new List<ulong>();

			uint player = PlayerOf(key);
			if (!latestByPlayer.TryGetValue(player, out ulong latest) || SequenceOf(latest) < SequenceOf(key))
				latestByPlayer[player] = key;

			Revision++;
			NodeAdded.Invoke(node);

			// the odometry edge is implied by the two nodes' own tracking poses
			if (!segmentStart && previous != 0 && nodes.TryGetValue(previous, out PoseNode prev))
				AddOdometryEdge(prev, node);

			return true;
		}

		private void AddOdometryEdge(PoseNode from, PoseNode to)
		{
			Pose4 z = Pose4.Between(from.odometry, to.odometry);
			float drift = OdometryDrift(z);
			AddEdge(new PoseEdge
			{
				id = to.key, // one odometry edge per node: its own key is unique among edges
				from = from.key, to = to.key, measurement = z,
				sigmaTranslation = drift,
				sigmaYaw = math.radians(MinYawDriftDegrees + YawDriftDegreesPerMeter * math.length(z.translation)) + DriftPerMeter * math.abs(z.yaw),
				kind = EdgeKind.Odometry
			});
		}

		public float OdometryDrift(Pose4 z) =>
			math.max(MinOdometryDrift, DriftPerMeter * math.length(z.translation) + DriftPerMeter * DriftPerRadian * math.abs(z.yaw));

		public bool AddEdge(PoseEdge edge)
		{
			if (edge.id == 0 || edges.ContainsKey(edge.id)) return false;
			if (!nodes.ContainsKey(edge.from) || !nodes.ContainsKey(edge.to)) return false;

			edges.Add(edge.id, edge);
			adjacency[edge.from].Add(edge.id);
			adjacency[edge.to].Add(edge.id);
			Revision++;
			EdgeAdded.Invoke(edge);
			return true;
		}

		public void SetRejected(ulong edgeId, bool rejected)
		{
			if (!edges.TryGetValue(edgeId, out PoseEdge edge) || edge.rejected == rejected) return;
			edge.rejected = rejected;
			edges[edgeId] = edge;
			Revision++;
		}

		public void SetAnchor(int chunkIndex, ulong node)
		{
			if (node == 0) chunkAnchors.Remove(chunkIndex);
			else chunkAnchors[chunkIndex] = node;
		}

		/// <summary>
		/// Applies a solve the authority broadcast. Poses are absolute. Raises
		/// <see cref="Solved"/> with the previous pose of every node that moved
		/// </summary>
		public void ApplySolve(int version, IReadOnlyList<(ulong key, Pose4 pose)> poses, IReadOnlyList<ulong> rejected)
		{
			Dictionary<ulong, Pose4> before = new();
			foreach ((ulong key, Pose4 pose) in poses)
			{
				if (!nodes.TryGetValue(key, out PoseNode node)) continue;
				before[key] = node.world;
				node.world = pose;
			}

			if (rejected != null)
				foreach (ulong id in rejected)
					SetRejected(id, true);

			Version = math.max(Version, version);
			Revision++;
			Solved.Invoke(before);
		}

		// ---- drift -------------------------------------------------------------

		private readonly Dictionary<ulong, (int revision, Dictionary<ulong, float> drift)> driftCache = new();
		private readonly List<(float cost, ulong key)> heap = new();

		/// <summary>
		/// Expected drift from <paramref name="source"/> to every node it reaches, the
		/// cheapest path through odometry (drift that grows with distance) and loop edges
		/// (a registration's few centimeters). Cached until the graph changes
		/// </summary>
		public IReadOnlyDictionary<ulong, float> DriftFrom(ulong source)
		{
			if (driftCache.TryGetValue(source, out var cached) && cached.revision == Revision) return cached.drift;

			Dictionary<ulong, float> drift = cached.drift ?? new Dictionary<ulong, float>();
			drift.Clear();
			if (nodes.ContainsKey(source))
			{
				heap.Clear();
				drift[source] = 0;
				Push(0, source);

				while (heap.Count > 0)
				{
					(float cost, ulong key) = Pop();
					if (cost > drift[key]) continue;

					foreach (ulong id in adjacency[key])
					{
						PoseEdge e = edges[id];
						if (e.rejected) continue;
						ulong other = e.from == key ? e.to : e.from;
						float next = cost + (e.kind == EdgeKind.Odometry ? e.sigmaTranslation : LoopDrift);
						if (drift.TryGetValue(other, out float known) && known <= next) continue;
						drift[other] = next;
						Push(next, other);
					}
				}
			}

			// a long session grows one cache entry per node asked about; keep the recent ones
			if (driftCache.Count > 64) driftCache.Clear();
			driftCache[source] = (Revision, drift);
			return drift;
		}

		/// <summary>Expected drift between two nodes; +∞ when no path joins them.</summary>
		public float Drift(ulong a, ulong b) => DriftFrom(a).TryGetValue(b, out float d) ? d : float.PositiveInfinity;

		private void Push(float cost, ulong key)
		{
			heap.Add((cost, key));
			int i = heap.Count - 1;
			while (i > 0)
			{
				int parent = (i - 1) / 2;
				if (heap[parent].cost <= heap[i].cost) break;
				(heap[parent], heap[i]) = (heap[i], heap[parent]);
				i = parent;
			}
		}

		private (float, ulong) Pop()
		{
			var top = heap[0];
			int last = heap.Count - 1;
			heap[0] = heap[last];
			heap.RemoveAt(last);
			int i = 0;
			while (true)
			{
				int l = 2 * i + 1, r = l + 1, m = i;
				if (l < heap.Count && heap[l].cost < heap[m].cost) m = l;
				if (r < heap.Count && heap[r].cost < heap[m].cost) m = r;
				if (m == i) break;
				(heap[m], heap[i]) = (heap[i], heap[m]);
				i = m;
			}

			return top;
		}

		/// <summary>
		/// Sets the bit of every anchored chunk a frame from <paramref name="node"/> must not
		/// integrate into: its anchor is further than <paramref name="limit"/> in expected
		/// drift, or not joined to it at all. <paramref name="bits"/> is cleared first
		/// </summary>
		public void BuildLockMask(ulong node, float limit, uint[] bits)
		{
			Array.Clear(bits, 0, bits.Length);
			IReadOnlyDictionary<ulong, float> drift = DriftFrom(node);
			foreach ((int chunk, ulong anchor) in chunkAnchors)
			{
				if (chunk < 0 || (chunk >> 5) >= bits.Length) continue;
				if (drift.TryGetValue(anchor, out float d) && d <= limit) continue;
				bits[chunk >> 5] |= 1u << (chunk & 31);
			}
		}

		// ---- solving -----------------------------------------------------------

		/// <summary>
		/// The optimization problem over every node and accepted edge, in insertion order.
		/// Loop and attach edges are robust (Huber); the gauge prior nodes are held
		/// </summary>
		public PoseGraphOptimizer.Problem BuildProblem(out List<ulong> nodeOrder, out List<ulong> edgeOrder)
		{
			nodeOrder = new List<ulong>(ordered.Count);
			Dictionary<ulong, int> index = new(ordered.Count);
			foreach (PoseNode node in ordered)
			{
				index[node.key] = nodeOrder.Count;
				nodeOrder.Add(node.key);
			}

			edgeOrder = new List<ulong>();
			List<int2> pairs = new();
			foreach (PoseEdge e in edges.Values)
			{
				if (e.rejected) continue;
				edgeOrder.Add(e.id);
				pairs.Add(new int2(index[e.from], index[e.to]));
			}

			PoseGraphOptimizer.Problem problem = new(nodeOrder.Count, edgeOrder.Count, pairs);
			for (int i = 0; i < ordered.Count; i++)
			{
				PoseNode node = ordered[i];
				problem.states[i] = node.world.Packed;
				problem.priors[i] = node.world.Packed;
				// a millimeter and a tenth of a degree: the gauge does not move
				problem.priorInformation[i] = node.hasPrior ? new float2(1e6f, 3e5f) : float2.zero;
			}

			for (int i = 0; i < edgeOrder.Count; i++)
			{
				PoseEdge e = edges[edgeOrder[i]];
				problem.edgeNodes[i] = pairs[i];
				problem.edgeMeasurements[i] = e.measurement.Packed;
				float st = math.max(e.sigmaTranslation, 1e-3f), sy = math.max(e.sigmaYaw, 1e-4f);
				problem.edgeInformation[i] = new float2(1f / (st * st), 1f / (sy * sy));
				problem.edgeRobust[i] = e.kind == EdgeKind.Odometry ? (byte)0 : (byte)1;
			}

			return problem;
		}

		// ---- wire format -------------------------------------------------------

		public static void WriteNode(BinaryWriter w, PoseNode node)
		{
			w.Write(node.key);
			w.Write(node.previous);
			w.Write((byte)((node.segmentStart ? 1 : 0) | (node.hasPrior ? 2 : 0)));
			WritePose(w, node.odometry);
			WritePose(w, node.world);
		}

		public static (ulong key, ulong previous, bool segmentStart, bool hasPrior, Pose4 odometry, Pose4 world) ReadNode(BinaryReader r)
		{
			ulong key = r.ReadUInt64();
			ulong previous = r.ReadUInt64();
			byte flags = r.ReadByte();
			Pose4 odometry = ReadPose(r);
			Pose4 world = ReadPose(r);
			return (key, previous, (flags & 1) != 0, (flags & 2) != 0, odometry, world);
		}

		public static void WriteEdge(BinaryWriter w, PoseEdge e)
		{
			w.Write(e.id);
			w.Write(e.from);
			w.Write(e.to);
			WritePose(w, e.measurement);
			w.Write(e.sigmaTranslation);
			w.Write(e.sigmaYaw);
			w.Write((byte)((byte)e.kind | (e.rejected ? 0x80 : 0)));
		}

		public static PoseEdge ReadEdge(BinaryReader r)
		{
			PoseEdge e = new()
			{
				id = r.ReadUInt64(), from = r.ReadUInt64(), to = r.ReadUInt64(),
				measurement = ReadPose(r), sigmaTranslation = r.ReadSingle(), sigmaYaw = r.ReadSingle()
			};
			byte kind = r.ReadByte();
			e.kind = (EdgeKind)(kind & 0x7F);
			e.rejected = (kind & 0x80) != 0;
			return e;
		}

		public static void WritePose(BinaryWriter w, Pose4 p)
		{
			w.Write(p.translation.x);
			w.Write(p.translation.y);
			w.Write(p.translation.z);
			w.Write(p.yaw);
		}

		public static Pose4 ReadPose(BinaryReader r) => new(new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle());

		/// <summary>
		/// Merges an authority's snapshot into this replica: its nodes and edges are added,
		/// its poses, rejections and chunk anchors win. Anything this replica already
		/// received that the snapshot predates is kept
		/// </summary>
		public void MergeSnapshot(int version, List<(ulong key, ulong previous, bool segmentStart, bool hasPrior, Pose4 odometry, Pose4 world)> snapshotNodes,
			List<PoseEdge> snapshotEdges, List<(int chunk, ulong anchor)> anchors)
		{
			Dictionary<ulong, Pose4> before = new();
			foreach (var n in snapshotNodes)
			{
				if (nodes.TryGetValue(n.key, out PoseNode existing))
				{
					before[n.key] = existing.world;
					existing.world = n.world;
					existing.hasPrior = n.hasPrior;
				}
				else
				{
					// odometry edges travel in the edge list; adding the node as a segment
					// start keeps AddNode from inventing one before its predecessor exists
					AddNode(n.key, n.previous, true, n.odometry, n.world, n.hasPrior);
					nodes[n.key].segmentStart = n.segmentStart;
				}
			}

			foreach (PoseEdge e in snapshotEdges)
			{
				if (edges.ContainsKey(e.id)) SetRejected(e.id, e.rejected);
				else AddEdge(e);
			}

			foreach ((int chunk, ulong anchor) in anchors) SetAnchor(chunk, anchor);

			Version = math.max(Version, version);
			Revision++;
			if (before.Count > 0) Solved.Invoke(before);
		}
	}
}
