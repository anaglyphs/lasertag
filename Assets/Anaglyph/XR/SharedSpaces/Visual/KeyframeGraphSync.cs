using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>A knot as it goes between headsets.</summary>
	public struct KnotMessage
	{
		public ulong key, previous;
		public float3 origin;
		public float path;
		/// <summary>Raw tracking to the map where its headset had it: (translation, yaw).</summary>
		public float4 transform;
	}

	/// <summary>An edge as it goes between headsets, its knots by key. <see cref="KeyframeEdge.knotA"/> and B are ignored.</summary>
	public struct EdgeMessage
	{
		public ulong keyA, keyB;
		public KeyframeEdge edge;
	}

	/// <summary>A knot's place in the latest solve.</summary>
	public struct PoseMessage
	{
		public ulong key;
		public float4 transform;
	}

	/// <summary>
	/// The <see cref="KeyframeGraph"/> between headsets. Everyone tells the authority about the
	/// knots they walk and the edges they measure; a few hundred bytes a second each. The
	/// authority alone solves, and tells everyone where the knots went: two solvers would be two
	/// worlds, and the distance between them a head beside its body.
	/// </summary>
	public sealed class KeyframeGraphSync : IDisposable
	{
		private const uint UploadMagic = 0x55524756, PosesMagic = 0x50524756; // VGRU, VGRP
		// via the authority a message goes whole, and the bus caps those at about 60 KB
		private const int MaxUploadBytes = 48 * 1024;
		private const float UploadInterval = 1f;

		private readonly KeyframeGraph graph;
		private readonly SyncEventBytes uploadEvent = new("vgraph.up", EventRoute.ToAuthority);
		private readonly SyncEventBytes posesEvent = new("vgraph.poses", EventRoute.Direct);
		private readonly List<KnotMessage> knots = new();
		private readonly List<EdgeMessage> edges = new();
		private readonly List<PoseMessage> poses = new();
		private float nextUpload;

		public KeyframeGraphSync(KeyframeGraph graph)
		{
			this.graph = graph;
			uploadEvent.Register();
			uploadEvent.Received += OnUpload;
			posesEvent.Register();
			posesEvent.Received += OnPoses;
		}

		public void Dispose()
		{
			uploadEvent.Received -= OnUpload;
			uploadEvent.Unregister();
			posesEvent.Received -= OnPoses;
			posesEvent.Unregister();
		}

		/// <summary>Call every frame.</summary>
		public void Update()
		{
			if (!SyncBus.Active) return;

			if (SyncBus.IsAuthority)
			{
				poses.Clear();
				graph.TakeSolvedPoses(poses);
				if (poses.Count > 0) posesEvent.Raise(EncodePoses(poses));
				return;
			}

			if (Time.unscaledTime < nextUpload) return;
			nextUpload = Time.unscaledTime + UploadInterval;
			knots.Clear();
			edges.Clear();
			graph.TakeUploads(knots, edges, MaxUploadBytes / SyncBytes.Size<EdgeMessage>());
			if (knots.Count > 0 || edges.Count > 0) uploadEvent.Raise(EncodeUpload(knots, edges));
		}

		// ---- the authority ---------------------------------------------------------

		private void OnUpload(ulong sender, byte[] message)
		{
			if (!SyncBus.IsAuthority || sender == SyncBus.LocalClientId) return;
			if (message.Length < sizeof(uint) + 2 * sizeof(ushort) || SyncBytes.Read<uint>(message, 0) != UploadMagic) return;
			int knotSize = SyncBytes.Size<KnotMessage>(), edgeSize = SyncBytes.Size<EdgeMessage>();
			int knotCount = SyncBytes.Read<ushort>(message, sizeof(uint));
			int edgeCount = SyncBytes.Read<ushort>(message, sizeof(uint) + sizeof(ushort));
			int offset = sizeof(uint) + 2 * sizeof(ushort);
			if (message.Length != offset + knotCount * knotSize + edgeCount * edgeSize) return;

			for (int i = 0; i < knotCount; i++, offset += knotSize)
				graph.AddOtherKnot(SyncBytes.Read<KnotMessage>(message, offset));
			for (int i = 0; i < edgeCount; i++, offset += edgeSize)
			{
				EdgeMessage e = SyncBytes.Read<EdgeMessage>(message, offset);
				graph.AddEdge(e.keyA, e.keyB, e.edge);
			}
		}

		private static byte[] EncodePoses(List<PoseMessage> poses)
		{
			int size = SyncBytes.Size<PoseMessage>();
			byte[] message = new byte[sizeof(uint) + poses.Count * size];
			SyncBytes.Write(message, 0, PosesMagic);
			for (int i = 0; i < poses.Count; i++) SyncBytes.Write(message, sizeof(uint) + i * size, poses[i]);
			return message;
		}

		// ---- everyone else ---------------------------------------------------------

		private static byte[] EncodeUpload(List<KnotMessage> knots, List<EdgeMessage> edges)
		{
			int knotSize = SyncBytes.Size<KnotMessage>(), edgeSize = SyncBytes.Size<EdgeMessage>();
			int offset = sizeof(uint) + 2 * sizeof(ushort);
			byte[] message = new byte[offset + knots.Count * knotSize + edges.Count * edgeSize];
			SyncBytes.Write(message, 0, UploadMagic);
			SyncBytes.Write(message, sizeof(uint), (ushort)knots.Count);
			SyncBytes.Write(message, sizeof(uint) + sizeof(ushort), (ushort)edges.Count);
			foreach (KnotMessage k in knots)
			{
				SyncBytes.Write(message, offset, k);
				offset += knotSize;
			}

			foreach (EdgeMessage e in edges)
			{
				SyncBytes.Write(message, offset, e);
				offset += edgeSize;
			}

			return message;
		}

		private void OnPoses(ulong sender, byte[] message)
		{
			if (SyncBus.IsAuthority || sender == SyncBus.LocalClientId) return;
			if (message.Length < sizeof(uint) || SyncBytes.Read<uint>(message, 0) != PosesMagic) return;
			int size = SyncBytes.Size<PoseMessage>();
			int count = (message.Length - sizeof(uint)) / size;
			poses.Clear();
			for (int i = 0; i < count; i++) poses.Add(SyncBytes.Read<PoseMessage>(message, sizeof(uint) + i * size));
			graph.ApplyPoses(poses);
		}
	}
}
