using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public enum DocumentReadStatus { Found, Missing, Invalid, Unavailable }

	/// <summary>Exact snapshot files with backup recovery. I/O uncertainty never becomes a negative catalog fact.</summary>
	internal sealed class JsonDocumentStore<T> where T : class
	{
		private readonly string directory;
		private readonly Func<T, string> getId;
		private readonly Func<T, T> clone;
		private readonly Func<T, bool> validate;
		private readonly Dictionary<string, T> documents = new();
		private readonly Dictionary<string, DocumentReadStatus> failures = new();
		private bool loaded;
		private bool catalogAvailable;
		public event Action Changed = delegate { };
		public string DirectoryPath => directory;
		public bool IsAvailable { get { EnsureLoaded(); return catalogAvailable && failures.Count == 0; } }
		public JsonDocumentStore(string directory, Func<T, string> getId, Func<T, T> clone, Func<T, bool> validate)
		{ this.directory = directory; this.getId = getId; this.clone = clone; this.validate = validate; }
		public List<T> All
		{
			get { EnsureLoaded(); List<T> result = new(); foreach (T value in documents.Values) result.Add(clone(value)); return result; }
		}
		public DocumentReadStatus Read(string id, out T document)
		{
			EnsureLoaded(); document = null;
			if (!MapSpace.ValidId(id)) return DocumentReadStatus.Invalid;
			if (documents.TryGetValue(id, out T value)) { document = clone(value); return DocumentReadStatus.Found; }
			return failures.TryGetValue(id, out var failure) ? failure : catalogAvailable ? DocumentReadStatus.Missing : DocumentReadStatus.Unavailable;
		}
		public void Refresh() { loaded = false; EnsureLoaded(); Changed.Invoke(); }
		public bool Save(T document)
		{
			EnsureLoaded();
			if (document == null || !validate(document)) return false;
			T snapshot = clone(document);
			string id = getId(snapshot);
			if (!AtomicJsonFile.Write(Path.Combine(directory, id + ".json"), JsonUtility.ToJson(snapshot, true))) return false;
			documents[id] = snapshot; failures.Remove(id); Changed.Invoke(); return true;
		}
		public bool Delete(string id)
		{
			EnsureLoaded(); if (!MapSpace.ValidId(id)) return false;
			string path = Path.Combine(directory, id + ".json");
			try
			{
				File.Delete(path + ".tmp"); File.Delete(path + ".bak"); File.Delete(path);
				documents.Remove(id); failures.Remove(id); Changed.Invoke(); return true;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.LogException(e); return false; }
		}
		private void EnsureLoaded()
		{
			if (loaded) return;
			loaded = true; catalogAvailable = false; documents.Clear(); failures.Clear();
			try
			{
				HashSet<string> ids = new();
				foreach (string path in Directory.GetFiles(directory, "*.json*"))
				{
					string name = Path.GetFileName(path);
					int dot = name.IndexOf('.');
					if (dot > 0 && MapSpace.ValidId(name.Substring(0, dot))) ids.Add(name.Substring(0, dot));
				}
				foreach (string id in ids)
				{
					string path = Path.Combine(directory, id + ".json");
					DocumentReadStatus outcome = DocumentReadStatus.Missing;
					foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
					{
						var status = TryRead(candidate, id, out T document);
						if (status == DocumentReadStatus.Found) { documents[id] = document; outcome = status; break; }
						// A locked primary must not be replaced by a potentially stale backup.
						if (status == DocumentReadStatus.Unavailable) { outcome = status; break; }
						if (status == DocumentReadStatus.Invalid) outcome = status;
					}
					if (outcome != DocumentReadStatus.Found) failures[id] = outcome;
				}
				catalogAvailable = true;
			}
			catch (DirectoryNotFoundException) { catalogAvailable = true; }
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.LogException(e); }
		}
		private DocumentReadStatus TryRead(string path, string id, out T document)
		{
			document = null;
			try
			{
				string json = File.ReadAllText(path);
				if (!json.Contains("\"schemaVersion\"")) return DocumentReadStatus.Invalid;
				document = JsonUtility.FromJson<T>(json);
				if (document != null && getId(document) == id && validate(document)) return DocumentReadStatus.Found;
				document = null; return DocumentReadStatus.Invalid;
			}
			catch (FileNotFoundException) { return DocumentReadStatus.Missing; }
			catch (DirectoryNotFoundException) { return DocumentReadStatus.Missing; }
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DocumentReadStatus.Unavailable; }
			catch (Exception e) when (e is ArgumentException or NullReferenceException) { return DocumentReadStatus.Invalid; }
		}
	}

	internal static class AtomicJsonFile
	{
		public static bool Write(string path, string json)
		{
			string temp = path + ".tmp", backup = path + ".bak";
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
				using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
				{ writer.Write(json); writer.Flush(); stream.Flush(true); }
				if (!File.Exists(path)) { File.Move(temp, path); return true; }
				File.Delete(backup);
				try { File.Replace(temp, path, backup); return true; }
				catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException ||
					e is IOException && File.Exists(temp) && File.Exists(path)) { }
				File.Move(path, backup);
				try { File.Move(temp, path); }
				catch { if (!File.Exists(path) && File.Exists(backup)) File.Move(backup, path); throw; }
				return true;
			}
			catch (Exception e) { Debug.LogException(e); return false; }
		}
	}
}
