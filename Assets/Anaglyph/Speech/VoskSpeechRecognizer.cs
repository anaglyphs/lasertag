using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Vosk;

namespace Anaglyph.Speech
{
	public class VoskSpeechRecognizer : MonoBehaviour
	{
		private const int SampleRate = 16000;
		private const int FrameSamples = 800;
		[SerializeField] private string modelArchive = "vosk-model-small-en-us-0.15.zip";
		public string[] Phrases { get; set; } = Array.Empty<string>();
		public event Action<string, bool> TranscriptionUpdated;
		public bool IsRecording => microphoneClip != null;

		private sealed class Session
		{
			public readonly CancellationTokenSource Cancellation = new();
			public readonly BlockingCollection<short[]> Audio = new(32);
			public readonly ConcurrentQueue<(string json, bool isFinal)> Results = new();
			public volatile bool Ready;
		}

		[Serializable]
		private class Transcript
		{
			public string partial;
			public string text;
		}

		private Session session;
		private Task previousSession = Task.CompletedTask;
		private AudioClip microphoneClip;
		private float[] samples;
		private int readPosition;
		private bool permissionDenied;

		private void OnEnable()
		{
			session = new Session();
			previousSession = RunSessionAsync(session, previousSession);
		}

		private void OnDisable()
		{
			StopMicrophone();
			session?.Cancellation.Cancel();
			session = null;
		}

		private async Task RunSessionAsync(Session current, Task previous)
		{
			CancellationToken token = current.Cancellation.Token;
			try
			{
				await previous;
				token.ThrowIfCancellationRequested();
				if (!await RequestMicrophonePermissionAsync(token))
					throw new InvalidOperationException("Microphone permission was denied.");
				token.ThrowIfCancellationRequested();

				string grammar = Phrases.Length == 0 ? null : "[" +
					string.Join(",", Array.ConvertAll(Phrases, phrase => "\"" +
						phrase.ToLowerInvariant().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + ",\"[unk]\"]";
				string cacheRoot = Path.Combine(Application.persistentDataPath, "Speech");
				string modelName = Path.GetFileNameWithoutExtension(modelArchive);
				string modelPath = Path.Combine(cacheRoot, modelName);
				string archivePath = Path.Combine(Application.streamingAssetsPath, modelArchive);
				Directory.CreateDirectory(cacheRoot);

				if (!File.Exists(Path.Combine(modelPath, ".ready")) && archivePath.Contains("://"))
				{
					string localArchive = Path.Combine(cacheRoot, modelArchive);
					using (UnityWebRequest request = UnityWebRequest.Get(archivePath))
					{
						request.downloadHandler = new DownloadHandlerFile(localArchive) { removeFileOnAbort = true };
						UnityWebRequestAsyncOperation download = request.SendWebRequest();
						while (!download.isDone)
							await Awaitable.NextFrameAsync(token);
						if (request.result != UnityWebRequest.Result.Success)
							throw new IOException(request.error);
					}
					archivePath = localArchive;
				}

				await Task.Run(() => Recognize(current, archivePath, modelPath, grammar, token), token);
			}
			catch (OperationCanceledException) { }
			catch (Exception exception)
			{
				if (!token.IsCancellationRequested)
					Debug.LogWarning($"Speech recognition unavailable: {exception.Message}", this);
			}
			finally
			{
				if (session == current)
				{
					StopMicrophone();
					session = null;
				}
				current.Audio.Dispose();
				current.Cancellation.Dispose();
			}
		}

		private static void Recognize(Session current, string archivePath, string modelPath,
			string grammar, CancellationToken token)
		{
			PrepareModel(archivePath, modelPath, token);
			using Model model = new(modelPath);
			token.ThrowIfCancellationRequested();
			using VoskRecognizer recognizer = string.IsNullOrEmpty(grammar)
				? new VoskRecognizer(model, SampleRate)
				: new VoskRecognizer(model, SampleRate, grammar);
			recognizer.SetMaxAlternatives(0);
			recognizer.SetWords(false);
			current.Ready = true;
			string previousPartial = null;
			foreach (short[] frame in current.Audio.GetConsumingEnumerable(token))
			{
				bool isFinal = recognizer.AcceptWaveform(frame, frame.Length);
				string json = isFinal ? recognizer.Result() : recognizer.PartialResult();
				if (isFinal || json != previousPartial)
					current.Results.Enqueue((json, isFinal));
				previousPartial = isFinal ? null : json;
			}
		}

		private static void PrepareModel(string archivePath, string modelPath, CancellationToken token)
		{
			if (File.Exists(Path.Combine(modelPath, ".ready")))
				return;
			string temporaryPath = modelPath + ".extracting";
			try
			{
				if (Directory.Exists(temporaryPath))
					Directory.Delete(temporaryPath, true);
				ZipFile.ExtractToDirectory(archivePath, temporaryPath);
				token.ThrowIfCancellationRequested();
				string extractedModel = Path.Combine(temporaryPath, Path.GetFileName(modelPath));
				if (!File.Exists(Path.Combine(extractedModel, "am", "final.mdl")))
					throw new InvalidDataException("The speech model archive is incomplete.");
				if (Directory.Exists(modelPath))
					Directory.Delete(modelPath, true);
				Directory.Move(extractedModel, modelPath);
				File.WriteAllText(Path.Combine(modelPath, ".ready"), string.Empty);
			}
			finally
			{
				if (Directory.Exists(temporaryPath))
					Directory.Delete(temporaryPath, true);
			}
		}

		private async Task<bool> RequestMicrophonePermissionAsync(CancellationToken token)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
				return true;
			if (permissionDenied)
				return false;
			TaskCompletionSource<bool> response = new();
			var callbacks = new UnityEngine.Android.PermissionCallbacks();
			callbacks.PermissionGranted += _ => response.TrySetResult(true);
			callbacks.PermissionDenied += _ => response.TrySetResult(false);
			using (token.Register(() => response.TrySetCanceled()))
			{
				UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
				bool granted = await response.Task;
				permissionDenied = !granted;
				return granted;
			}
#else
			if (Application.HasUserAuthorization(UserAuthorization.Microphone))
				return true;
			if (permissionDenied)
				return false;
			AsyncOperation request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
			while (!request.isDone)
				await Awaitable.NextFrameAsync(token);
			permissionDenied = !Application.HasUserAuthorization(UserAuthorization.Microphone);
			return !permissionDenied;
#endif
		}

		private void Update()
		{
			Session current = session;
			if (current == null || !current.Ready)
				return;
			try
			{
				if (microphoneClip == null)
				{
					if (Microphone.devices.Length == 0)
						throw new InvalidOperationException("No microphone is available.");
					microphoneClip = Microphone.Start(null, true, 1, SampleRate);
					if (microphoneClip == null || microphoneClip.frequency != SampleRate)
						throw new InvalidOperationException("The microphone could not start at 16000 Hz.");
					samples = new float[FrameSamples * microphoneClip.channels];
					readPosition = 0;
				}

				int writePosition = Microphone.GetPosition(null);
				if (writePosition < 0)
					throw new InvalidOperationException("The microphone disconnected.");
				int available = (writePosition - readPosition + microphoneClip.samples) % microphoneClip.samples;
				while (available >= FrameSamples)
				{
					microphoneClip.GetData(samples, readPosition);
					short[] frame = new short[FrameSamples];
					int channels = microphoneClip.channels;
					for (int i = 0; i < frame.Length; i++)
					{
						float sample = 0;
						for (int channel = 0; channel < channels; channel++)
							sample += samples[i * channels + channel];
						frame[i] = (short)(Mathf.Clamp(sample / channels, -1, 1) * short.MaxValue);
					}
					if (!current.Audio.TryAdd(frame))
						throw new InvalidOperationException("Speech processing could not keep up with microphone input.");
					readPosition = (readPosition + FrameSamples) % microphoneClip.samples;
					available -= FrameSamples;
				}

				while (session == current && current.Results.TryDequeue(out var result))
				{
					Transcript transcript = JsonUtility.FromJson<Transcript>(result.json);
					TranscriptionUpdated?.Invoke(result.isFinal ? transcript.text : transcript.partial, result.isFinal);
				}
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"Speech recognition stopped: {exception.Message}", this);
				StopMicrophone();
				current.Cancellation.Cancel();
				session = null;
			}
		}

		private void StopMicrophone()
		{
			if (microphoneClip == null)
				return;
			Microphone.End(null);
			Destroy(microphoneClip);
			microphoneClip = null;
		}
	}
}
