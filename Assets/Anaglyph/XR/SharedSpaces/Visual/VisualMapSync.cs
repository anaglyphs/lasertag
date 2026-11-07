using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// The views every headset has made, shared: a team on one side of a space may be all that
	/// ever sees it. A joiner asks the authority once and receives every shared view and landmark
	/// the authority holds (<see cref="VisualMapCodec"/>), and from then on every headset sends the
	/// views it adds that none of them has seen a place and heading from yet, and the new depths
	/// of its landmarks that have gone out and been refined since. Received views go
	/// into the map in their owner's raw tracking, under that owner's knot, so they move with the
	/// <see cref="KeyframeGraph"/>; this headset's tracking only localizes against them.
	/// A view is named by its owner and capture time on every headset, a landmark by its owner
	/// and the owner's id for it, and a knot by <see cref="KnotKeys"/>.
	///
	/// Views go once their landmarks have had <see cref="SettleSeconds"/> to triangulate from
	/// the views after them. The bus splits and paces the messages
	/// (<see cref="SyncBus.ReliableBytesPerSecond"/>), and keeps each sender's in order. A map
	/// the authority clears or replaces starts a new epoch, sent whole to everyone again.
	/// </summary>
	public sealed class VisualMapSync : IDisposable
	{
		/// <summary>Seconds a new view waits before it is sent: the views after it range its landmarks.</summary>
		public float SettleSeconds = 10f;
		/// <summary>A view sighting fewer usable landmarks than this can't be localized against, whatever else it covers: it isn't sent.</summary>
		public int MinLandmarks = 30;
		/// <summary>Seconds a joiner waits on the map it asked for before asking again: a few MB take a few seconds.</summary>
		public float RequestRetry = 30f;
		/// <summary>A view adds nothing while one already shared is this near, meters...</summary>
		public float CoverDistance = 1f;
		/// <summary>...and turned less than this, degrees: a headset new to a place has only these to match its views against, and matches few across more.</summary>
		public float CoverAngle = 20f;
		/// <summary>Seconds between messages of refined landmark depths.</summary>
		public float DepthSendSeconds = 2f;
		/// <summary>A landmark's depth goes out again once it has moved this share from what the others hold.</summary>
		public float DepthResendShare = 0.05f;

		private const uint Magic = 0x50534D57; // WMSP
		// [magic][epoch][kind][part][parts][owner][knots] then each knot, then the codec's bytes
		private const int HeaderBytes = sizeof(uint) + sizeof(ushort) + 1 + 2 * sizeof(ushort) + sizeof(uint) + sizeof(ushort);
		private const int KindOffset = sizeof(uint) + sizeof(ushort);
		private const int PartOffset = KindOffset + 1;
		private const int PartsOffset = PartOffset + sizeof(ushort);
		private const int OwnerOffset = PartsOffset + sizeof(ushort);
		private const int KnotsOffset = OwnerOffset + sizeof(uint);
		private const byte KindSnapshot = 0, KindViews = 1, KindGraph = 2, KindForget = 3, KindDepths = 4;
		// a landmark in a depths message: the owner's id for it, its inverse depth and sigma as halves
		private const int DepthEntryBytes = sizeof(int) + 2 * sizeof(ushort);
		private const byte RequestSnapshot = 0, RequestKeepAlive = 1, RequestForget = 2;
		// a joiner says it is still there this often, and is forgotten after this long without
		private const float KeepAliveSeconds = 10f, ForgetSeconds = 30f;
		private const int MaxEarlyMessages = 256;

		private readonly SyncEventBytes requestEvent = new("vmap.request", EventRoute.ToAuthority);
		private readonly SyncEventBytes mapEvent = new("vmap.map", EventRoute.Direct);
		private readonly VisualLocalizer localizer;
		private readonly KeyframeGraph graph;
		private readonly TrackAnchoring track;
		private readonly string logTag;
		private readonly Action<string> outcome;
		private readonly Func<bool> holdViews;

		/// <param name="holdViews">Whether the views this headset adds should wait for now.</param>
		public VisualMapSync(VisualLocalizer localizer, KeyframeGraph graph, TrackAnchoring track, string logTag, Action<string> outcome, Func<bool> holdViews)
		{
			this.holdViews = holdViews;
			this.localizer = localizer;
			this.graph = graph;
			this.track = track;
			this.logTag = logTag;
			this.outcome = outcome;
			VisualMapCodec.Prepare();
			requestEvent.Register();
			requestEvent.Received += OnRequest;
			mapEvent.Register();
			mapEvent.Received += OnMap;
			localizer.KeyframeAdded += OnKeyframeAdded;
			localizer.Map.Cleared += OnMapCleared;
			localizer.Map.LandmarkDepthChanged += OnLandmarkDepthChanged;
			SyncBus.Deactivated += Reset;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		public void Dispose()
		{
			requestEvent.Received -= OnRequest;
			requestEvent.Unregister();
			mapEvent.Received -= OnMap;
			mapEvent.Unregister();
			if (localizer)
			{
				localizer.KeyframeAdded -= OnKeyframeAdded;
				localizer.Map.Cleared -= OnMapCleared;
				localizer.Map.LandmarkDepthChanged -= OnLandmarkDepthChanged;
			}
			SyncBus.Deactivated -= Reset;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
		}

		private static bool IsJoiner => SyncBus.Active && !SyncBus.IsAuthority;
		private static bool IsHost => SyncBus.Active && SyncBus.IsAuthority;

		/// <summary>Call every frame. <paramref name="wanted"/>: whether this headset is aligning visually at all.</summary>
		public void Update(bool wanted)
		{
			if (IsHost) UpdateHost();
			else if (IsJoiner && wanted) UpdateJoiner();
		}

		private void Reset()
		{
			subscribers.Clear();
			settling.Clear();
			HasHostMap = false;
			arrived.Clear();
			early.Clear();
			earlyDepths.Clear();
			refined.Clear();
			idsByOwner.Clear();
			views.Clear();
			nextRequest = 0;
		}

		private void OnAuthorityChanged(bool _) => Reset();

		/// <summary>
		/// This joiner's alignment turned out wrong: asks the authority to take back everything
		/// shared under <paramref name="owner"/>, from every headset. Call before the map is cleared:
		/// what this headset asks for next comes after it, without those views to mislead it again.
		/// </summary>
		public void TakeBackShared(uint owner)
		{
			if (!IsJoiner) return;
			byte[] request = new byte[1 + sizeof(uint)];
			request[0] = RequestForget;
			SyncBytes.Write(request, 1, owner);
			requestEvent.Raise(request);
		}

		private void Forget(uint owner)
		{
			int views = localizer.Map.RemoveOwner(owner);
			this.views.RemoveWhere(view => view.owner == owner);
			idsByOwner.Remove(owner);
			graph.Forget(owner);
			localizer.Map.RefreshPlacement();
			outcome($"MapSync: took back {views} views and the edges of a headset whose alignment was wrong");
		}

		// ---- what everyone sends ---------------------------------------------------

		private readonly List<(VisualKeyframe keyframe, float addedAt)> settling = new();

		private void OnKeyframeAdded(VisualKeyframe keyframe)
		{
			// an authority no one has asked yet sends it all when someone does
			if (IsHost && subscribers.Count > 0 || IsJoiner && HasHostMap) settling.Add((keyframe, Time.unscaledTime));
		}

		private ushort CurrentEpoch => IsHost ? epoch : hostEpoch;

		// a view any headset already holds this place and heading from adds nothing to send
		private bool MarkIfAdding(VisualKeyframe view)
		{
			if (view.shared || !SightsEnough(view)) return false;
			float cosine = math.cos(math.radians(CoverAngle));
			Vector3 at = view.camera.position, forward = view.camera.forward;
			foreach (VisualKeyframe k in localizer.Map.Keyframes)
				if (k.shared && Vector3.Distance(k.camera.position, at) < CoverDistance && Vector3.Dot(k.camera.forward, forward) > cosine)
					return false;

			view.shared = true;
			return true;
		}

		private bool SightsEnough(VisualKeyframe view)
		{
			int sighted = 0;
			foreach (int id in view.landmarks)
				if (localizer.Map.TryGetLandmark(id, out Landmark l) && l.IsUsable && ++sighted >= MinLandmarks)
					return true;
			return false;
		}

		private async void SendSettledViews()
		{
			if (settling.Count == 0 || holdViews()) return;
			List<VisualKeyframe> settled = null;
			for (int i = 0; i < settling.Count; i++)
			{
				if (Time.unscaledTime - settling[i].addedAt < SettleSeconds) continue;
				(settled ??= new()).Add(settling[i].keyframe);
				settling.RemoveAt(i--);
			}

			if (settled == null) return;
			// views evicted while settling are gone from the map too
			HashSet<VisualKeyframe> held = new(localizer.Map.Keyframes);
			settled.RemoveAll(k => !held.Contains(k) || !MarkIfAdding(k));
			if (settled.Count == 0) return;

			ushort sentEpoch = CurrentEpoch;
			byte[] message = await BuildAsync(KindViews, 0, 1, graph.Owner, settled);
			if (sentEpoch == CurrentEpoch) mapEvent.Raise(message);
		}

		// the views' poses as their node has them, not as the graph does: the knots' place goes with them
		private async Awaitable<byte[]> BuildAsync(byte kind, int part, int parts, uint owner, List<VisualKeyframe> sending)
		{
			ushort sentEpoch = CurrentEpoch;
			VisualMapData data = VisualMapFile.Snapshot(localizer.Map, pose => pose, Quaternion.identity, sending, Raw, graph.WireNode);

			HashSet<ulong> named = new();
			List<PoseMessage> knots = new();
			void Name(ulong node)
			{
				if (node != 0 && named.Add(node))
					knots.Add(new PoseMessage { key = KnotKeys.Key(node), transform = graph.TransformOfWire(node) });
			}

			foreach (VisualMapData.Keyframe k in data.keyframes) Name(k.node);
			foreach (VisualMapData.Landmark l in data.landmarks) Name(l.node);

			byte[] bytes;
			await Awaitable.BackgroundThreadAsync();
			try
			{
				bytes = VisualMapCodec.Encode(data);
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			int knotSize = SyncBytes.Size<PoseMessage>();
			byte[] message = new byte[HeaderBytes + knots.Count * knotSize + bytes.Length];
			SyncBytes.Write(message, 0, Magic);
			SyncBytes.Write(message, sizeof(uint), sentEpoch);
			message[KindOffset] = kind;
			SyncBytes.Write(message, PartOffset, (ushort)part);
			SyncBytes.Write(message, PartsOffset, (ushort)parts);
			SyncBytes.Write(message, OwnerOffset, owner);
			SyncBytes.Write(message, KnotsOffset, (ushort)knots.Count);
			for (int i = 0; i < knots.Count; i++) SyncBytes.Write(message, HeaderBytes + i * knotSize, knots[i]);
			Buffer.BlockCopy(bytes, 0, message, HeaderBytes + knots.Count * knotSize, bytes.Length);
			return message;
		}

		// other headsets' and a saved map's are held as they came
		private Pose Raw(ulong node, Pose inNode) => track.Raw(node, inNode);

		// ---- refined depths --------------------------------------------------------

		// this headset's landmarks whose depth has moved since the last message
		private readonly HashSet<Landmark> refined = new();
		private float nextDepthSend;
		private int depthsSent, depthsReceived;
		private float nextDepthReport;

		private void OnLandmarkDepthChanged(Landmark landmark)
		{
			if (landmark.owner == graph.Owner) refined.Add(landmark);
		}

		private void SendRefinedDepths()
		{
			if (Time.unscaledTime >= nextDepthReport)
			{
				nextDepthReport = Time.unscaledTime + 60f;
				if (depthsSent + depthsReceived > 0) outcome($"MapSync: {depthsSent} refined depths sent, {depthsReceived} received");
			}

			if (refined.Count == 0 || Time.unscaledTime < nextDepthSend || holdViews()) return;
			nextDepthSend = Time.unscaledTime + DepthSendSeconds;

			List<Landmark> sending = new();
			foreach (Landmark l in refined)
				// one that hasn't gone out yet goes as it is then, with its views
				if (l.sentInverseDepth > 0 && math.abs(l.inverseDepth - l.sentInverseDepth) >= DepthResendShare * l.sentInverseDepth)
					sending.Add(l);
			refined.Clear();
			if (sending.Count == 0) return;

			byte[] message = new byte[HeaderBytes + sending.Count * DepthEntryBytes];
			SyncBytes.Write(message, 0, Magic);
			SyncBytes.Write(message, sizeof(uint), CurrentEpoch);
			message[KindOffset] = KindDepths;
			SyncBytes.Write(message, OwnerOffset, graph.Owner);
			for (int i = 0; i < sending.Count; i++)
			{
				Landmark l = sending[i];
				int at = HeaderBytes + i * DepthEntryBytes;
				SyncBytes.Write(message, at, l.ownerId);
				SyncBytes.Write(message, at + sizeof(int), ((half)l.inverseDepth).value);
				SyncBytes.Write(message, at + sizeof(int) + sizeof(ushort), ((half)math.min(l.inverseDepthSigma, 65000f)).value);
				l.sentInverseDepth = l.inverseDepth;
			}

			depthsSent += sending.Count;
			mapEvent.Raise(message);
		}

		private void ApplyDepths(byte[] message)
		{
			if (!localizer || !SyncBus.Active) return;
			// they may name landmarks of the map still on its way: applied after it
			if (IsJoiner && !HasHostMap)
			{
				if (earlyDepths.Count < MaxEarlyMessages) earlyDepths.Add(message);
				return;
			}

			if (SyncBytes.Read<ushort>(message, sizeof(uint)) != CurrentEpoch) return;
			uint owner = SyncBytes.Read<uint>(message, OwnerOffset);
			if (graph.IsForgotten(owner) || !idsByOwner.TryGetValue(owner, out Dictionary<int, int> ids)) return;
			for (int at = HeaderBytes; at + DepthEntryBytes <= message.Length; at += DepthEntryBytes)
			{
				if (!ids.TryGetValue(SyncBytes.Read<int>(message, at), out int id)) continue;
				float inverseDepth = new half { value = SyncBytes.Read<ushort>(message, at + sizeof(int)) };
				float inverseDepthSigma = new half { value = SyncBytes.Read<ushort>(message, at + sizeof(int) + sizeof(ushort)) };
				localizer.Map.SetDepth(id, inverseDepth, inverseDepthSigma);
				depthsReceived++;
			}
		}

		// ---- the authority ---------------------------------------------------------

		private sealed class Subscriber
		{
			public float heardAt;
			public float snapshotAt = float.NegativeInfinity;
			// the snapshot being encoded for it: only the latest asked for is sent
			public int snapshot;
		}

		private readonly Dictionary<ulong, Subscriber> subscribers = new();
		private ushort epoch;
		private bool resync;

		private void OnRequest(ulong sender, byte[] payload)
		{
			if (!IsHost || sender == SyncBus.LocalClientId) return;
			if (payload.Length == 1 + sizeof(uint) && payload[0] == RequestForget)
			{
				uint owner = SyncBytes.Read<uint>(payload, 1);
				Forget(owner);
				byte[] message = new byte[HeaderBytes];
				SyncBytes.Write(message, 0, Magic);
				SyncBytes.Write(message, sizeof(uint), epoch);
				message[KindOffset] = KindForget;
				SyncBytes.Write(message, OwnerOffset, owner);
				mapEvent.Raise(message);
				return;
			}

			bool known = subscribers.TryGetValue(sender, out Subscriber s);
			if (!known) subscribers[sender] = s = new Subscriber();
			s.heardAt = Time.unscaledTime;
			if (payload.Length > 0 && payload[0] == RequestKeepAlive && known) return;
			// one already on its way is still coming: a retry while it drains would send it twice
			if (Time.unscaledTime - s.snapshotAt < RequestRetry) return;
			SendSnapshot(sender, s);
		}

		private void OnMapCleared()
		{
			if (IsHost)
			{
				// restoring a saved map clears first and fills after: sent next frame, whole
				epoch++;
				settling.Clear();
				refined.Clear();
				views.Clear();
				idsByOwner.Clear();
				resync = true;
			}
			else if (!applying)
			{
				// a joiner's map is gone (searching again, its scan cleared): ask for the authority's anew
				HasHostMap = false;
				settling.Clear();
				refined.Clear();
				early.Clear();
				earlyDepths.Clear();
				idsByOwner.Clear();
				views.Clear();
				nextRequest = 0;
			}
		}

		private void UpdateHost()
		{
			// joiners not heard from in a while are gone
			List<ulong> gone = null;
			foreach ((ulong client, Subscriber s) in subscribers)
				if (Time.unscaledTime - s.heardAt > ForgetSeconds) (gone ??= new()).Add(client);
			if (gone != null) foreach (ulong client in gone) subscribers.Remove(client);

			if (resync)
			{
				resync = false;
				foreach ((ulong client, Subscriber s) in subscribers) SendSnapshot(client, s);
			}

			SendSettledViews();
			SendRefinedDepths();
		}

		// one message per owner: a landmark's id means something only within its owner's
		private async void SendSnapshot(ulong client, Subscriber s)
		{
			s.snapshotAt = Time.unscaledTime;
			int version = ++s.snapshot;
			ushort sentEpoch = epoch;

			Dictionary<uint, List<VisualKeyframe>> byOwner = new() { [graph.Owner] = new() };
			List<VisualKeyframe> held = new(localizer.Map.Keyframes);
			held.Sort((a, b) => a.timestampNs.CompareTo(b.timestampNs));
			foreach (VisualKeyframe k in held)
			{
				if (!k.shared && !MarkIfAdding(k)) continue;
				if (!byOwner.TryGetValue(k.owner, out List<VisualKeyframe> list)) byOwner[k.owner] = list = new();
				list.Add(k);
			}

			int part = 0, bytes = 0, viewCount = 0;
			foreach ((uint owner, List<VisualKeyframe> list) in byOwner)
			{
				byte[] message = await BuildAsync(KindSnapshot, part++, byOwner.Count, owner, list);
				if (!subscribers.TryGetValue(client, out Subscriber now) || now != s || version != s.snapshot || sentEpoch != epoch) return;
				mapEvent.RaiseTo(client, message);
				bytes += message.Length;
				viewCount += list.Count;
			}

			// last, so nothing it names is missing when it lands: the views' knots came with them until then
			byte[] whole = graph.Encode();
			byte[] graphMessage = new byte[HeaderBytes + whole.Length];
			SyncBytes.Write(graphMessage, 0, Magic);
			SyncBytes.Write(graphMessage, sizeof(uint), sentEpoch);
			graphMessage[KindOffset] = KindGraph;
			SyncBytes.Write(graphMessage, PartsOffset, (ushort)part);
			SyncBytes.Write(graphMessage, OwnerOffset, graph.Owner);
			Buffer.BlockCopy(whole, 0, graphMessage, HeaderBytes, whole.Length);
			mapEvent.RaiseTo(client, graphMessage);
			bytes += graphMessage.Length;

			outcome($"MapSync: sending client {client} the map, {viewCount} views from {byOwner.Count} headsets and {graph.KnotCount} knots, {graph.EdgeCount} edges in {bytes / 1024} KB");
		}

		// ---- a joiner --------------------------------------------------------------

		/// <summary>Whether this joiner holds the authority's map, to align against.</summary>
		public bool HasHostMap { get; private set; }

		/// <summary>Raised when the authority's whole map has replaced this joiner's.</summary>
		public event Action HostMapReceived = delegate { };

		private ushort hostEpoch;
		private float nextRequest;
		private float nextKeepAlive;
		private bool applying;

		private void UpdateJoiner()
		{
			if (!HasHostMap && !decoding && Time.unscaledTime >= nextRequest)
			{
				nextRequest = Time.unscaledTime + RequestRetry;
				nextKeepAlive = Time.unscaledTime + KeepAliveSeconds;
				requestEvent.Raise(new[] { RequestSnapshot });
			}
			else if (Time.unscaledTime >= nextKeepAlive)
			{
				nextKeepAlive = Time.unscaledTime + KeepAliveSeconds;
				requestEvent.Raise(new[] { RequestKeepAlive });
			}

			SendSettledViews();
			SendRefinedDepths();
		}

		// ---- what everyone receives --------------------------------------------------

		// each owner's landmark ids as this map's, and the views already merged (by owner and capture time)
		private readonly Dictionary<uint, Dictionary<int, int>> idsByOwner = new();
		private readonly HashSet<(uint owner, long timestamp)> views = new();

		private void OnMap(ulong sender, byte[] message)
		{
			if (!SyncBus.Active || message.Length < HeaderBytes || SyncBytes.Read<uint>(message, 0) != Magic) return;
			// the bus delivers a headset's own back to it
			if (sender == SyncBus.LocalClientId) return;
			arrived.Enqueue(message);
			if (!decoding) DecodeArrived();
		}

		// messages waiting to decode: one at a time, in the order they came, so a few views
		// never land before the snapshot they follow
		private readonly Queue<byte[]> arrived = new();
		private bool decoding;
		// other headsets' views that came before the authority's map: merged after it
		private readonly List<(ushort epoch, uint owner, VisualMapData data)> early = new();
		private readonly List<byte[]> earlyDepths = new();

		private async void DecodeArrived()
		{
			decoding = true;
			try
			{
				while (arrived.Count > 0)
				{
					byte[] message = arrived.Dequeue();
					if (message[KindOffset] == KindGraph)
					{
						ApplyGraph(message);
						continue;
					}

					if (message[KindOffset] == KindForget)
					{
						if (localizer && SyncBus.Active) Forget(SyncBytes.Read<uint>(message, OwnerOffset));
						continue;
					}

					if (message[KindOffset] == KindDepths)
					{
						ApplyDepths(message);
						continue;
					}

					int knotSize = SyncBytes.Size<PoseMessage>();
					int knotCount = SyncBytes.Read<ushort>(message, KnotsOffset);
					int headerEnd = HeaderBytes + knotCount * knotSize;
					if (message.Length < headerEnd) continue;
					byte[] bytes = new byte[message.Length - headerEnd];
					Buffer.BlockCopy(message, headerEnd, bytes, 0, bytes.Length);

					VisualMapData data;
					await Awaitable.BackgroundThreadAsync();
					try
					{
						data = VisualMapCodec.Decode(bytes);
					}
					finally
					{
						await Awaitable.MainThreadAsync();
					}

					Apply(message, data, knotCount);
				}
			}
			finally
			{
				decoding = false;
			}
		}

		private void Apply(byte[] message, VisualMapData data, int knotCount)
		{
			if (!localizer || !SyncBus.Active) return;
			ushort messageEpoch = SyncBytes.Read<ushort>(message, sizeof(uint));
			byte kind = message[KindOffset];
			uint owner = SyncBytes.Read<uint>(message, OwnerOffset);
			if (data == null)
			{
				Debug.LogWarning($"{logTag} a map message didn't decode ({message.Length} bytes)");
				if (kind == KindSnapshot && IsJoiner)
				{
					HasHostMap = false;
					nextRequest = 0;
				}
				return;
			}

			if (kind == KindSnapshot)
			{
				if (!IsJoiner) return;
				int part = SyncBytes.Read<ushort>(message, PartOffset);
				if (part == 0)
				{
					applying = true;
					try
					{
						localizer.Map.Clear();
					}
					finally
					{
						applying = false;
					}

					idsByOwner.Clear();
					views.Clear();
					hostEpoch = messageEpoch;
				}

				ApplyKnots(message, knotCount);
				Merge(owner, data, false);
				return;
			}

			ApplyKnots(message, knotCount);

			if (IsJoiner && !HasHostMap)
			{
				if (early.Count < MaxEarlyMessages) early.Add((messageEpoch, owner, data));
				return;
			}

			// views of a map that has since been replaced: its snapshot is on the way
			if (messageEpoch != CurrentEpoch) return;
			Merge(owner, data, true);
		}

		// the authority's graph, the end of its map
		private void ApplyGraph(byte[] message)
		{
			if (!localizer || !IsJoiner || SyncBytes.Read<ushort>(message, sizeof(uint)) != hostEpoch) return;
			if (!graph.Decode(message, HeaderBytes))
			{
				Debug.LogWarning($"{logTag} the authority's graph didn't decode ({message.Length} bytes)");
				HasHostMap = false;
				nextRequest = 0;
				return;
			}

			localizer.Map.RefreshPlacement();
			HasHostMap = true;
			outcome($"MapSync: received the authority's map from {SyncBytes.Read<ushort>(message, PartsOffset)} headsets, {graph.KnotCount} knots and {graph.EdgeCount} edges ({localizer.Quality})");
			foreach ((ushort epochThen, uint ownerThen, VisualMapData dataThen) in early)
				if (epochThen == hostEpoch)
					Merge(ownerThen, dataThen, true);
			early.Clear();
			foreach (byte[] depths in earlyDepths) ApplyDepths(depths);
			earlyDepths.Clear();
			HostMapReceived.Invoke();
		}

		// where the sender's knots were when it sent these: until a solve says otherwise
		private void ApplyKnots(byte[] message, int knotCount)
		{
			int knotSize = SyncBytes.Size<PoseMessage>();
			for (int i = 0; i < knotCount; i++)
			{
				PoseMessage knot = SyncBytes.Read<PoseMessage>(message, HeaderBytes + i * knotSize);
				ulong node = KnotKeys.Node(knot.key);
				if (!track.HasOtherWorld(node)) track.SetOtherWorld(node, new Pose4(knot.transform.xyz, knot.transform.w));
			}
		}

		private void Merge(uint owner, VisualMapData data, bool matchAgainstOwn)
		{
			if (graph.IsForgotten(owner)) return;
			data.owner = owner;
			// a view sent whole and then again as it settled is merged once
			data.keyframes.RemoveAll(k => !views.Add((owner, k.timestampNs)));
			if (!idsByOwner.TryGetValue(owner, out Dictionary<int, int> ids)) idsByOwner[owner] = ids = new();
			List<VisualKeyframe> added = localizer.Map.Merge(data, pose => pose, Quaternion.identity, localizer.PixelSigma, ids, true);
			localizer.Map.RefreshPlacement();
			if (!matchAgainstOwn) return;
			foreach (VisualKeyframe view in added) graph.AddReceived(view);
		}
	}
}
