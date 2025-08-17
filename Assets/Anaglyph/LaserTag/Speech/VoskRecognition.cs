using Ionic.Zip;
using OVRSimpleJSON;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;                // NEW
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using Vosk;

public class VoskRecognition : MonoBehaviour
{
	[Tooltip("Location of the model, relative to the Streaming Assets folder.")]
	public string ModelPath = "vosk-model-small-ru-0.22.zip";

	[Tooltip("The source of the microphone input.")]
	public VoiceProcessor VoiceProcessor;

	[Tooltip("The Max number of alternatives that will be processed.")]
	public int MaxAlternatives = 3;

	[Tooltip("How long should we record before restarting?")]
	public float MaxRecordLength = 5;

	[Tooltip("Should the recognizer start when the application is launched?")]
	public bool AutoStart = true;

	[Tooltip("The phrases that will be detected. If left empty, all words will be detected.")]
	public List<string> KeyPhrases = new List<string>();

	private Model _model;
	private VoskRecognizer _recognizer;
	private bool _recognizerReady;

	public string PartialResult { get; private set; }

	private readonly AutoResetEvent _hasAudio = new AutoResetEvent(false); // NEW

	public Action<string> OnStatusUpdated = delegate { };
	public Action<string> OnTranscriptionResult = delegate { };

	// NEW: partial transcription callback (plain text, not JSON)
	public Action<string> OnPartialTranscription = delegate { };   // NEW

	private string _decompressedModelPath;
	private string _grammar = "";
	private bool _isDecompressing;
	private bool _isInitializing;
	private bool _didInit;

	private bool _running;

	private readonly ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();
	private readonly ConcurrentQueue<string> _threadedResultQueue = new ConcurrentQueue<string>();

	static readonly ProfilerMarker voskRecognizerCreateMarker = new ProfilerMarker("VoskRecognizer.Create");
	static readonly ProfilerMarker voskRecognizerReadMarker = new ProfilerMarker("VoskRecognizer.AcceptWaveform");

	// NEW: tiny helper to extract the "partial" field from Vosk JSON
	private static readonly Regex PartialJsonRx =
		new Regex(@"""partial""\s*:\s*""(?<t>.*?)""", RegexOptions.Compiled); // NEW

	void Start()
	{
		if (AutoStart)
		{
			StartVoskStt();
		}
	}

	public void StartVoskStt(List<string> keyPhrases = null, string modelPath = default, bool startMicrophone = false, int maxAlternatives = 3)
	{
		if (_isInitializing)
		{
			Debug.LogError("Initializing in progress!");
			return;
		}
		if (_didInit)
		{
			Debug.LogError("Vosk has already been initialized!");
			return;
		}

		if (!string.IsNullOrEmpty(modelPath))
		{
			ModelPath = modelPath;
		}

		if (keyPhrases != null)
		{
			KeyPhrases = keyPhrases;
		}

		MaxAlternatives = maxAlternatives;
		StartCoroutine(DoStartVoskStt(startMicrophone));
	}

	private IEnumerator DoStartVoskStt(bool startMicrophone)
	{
		_isInitializing = true;
		yield return WaitForMicrophoneInput();

		yield return Decompress();

		OnStatusUpdated?.Invoke("Loading Model from: " + _decompressedModelPath);
		_model = new Model(_decompressedModelPath);

		yield return null;

		OnStatusUpdated?.Invoke("Initialized");
		VoiceProcessor.OnFrameCaptured += VoiceProcessorOnOnFrameCaptured;
		VoiceProcessor.OnRecordingStop += VoiceProcessorOnOnRecordingStop;

		if (startMicrophone)
			VoiceProcessor.StartRecording();

		_isInitializing = false;
		_didInit = true;

		ToggleRecording();
	}

	private void UpdateGrammar()
	{
		if (KeyPhrases.Count == 0)
		{
			_grammar = "";
			return;
		}

		JSONArray keywords = new JSONArray();
		foreach (string keyphrase in KeyPhrases)
		{
			keywords.Add(new JSONString(keyphrase.ToLower()));
		}

		keywords.Add(new JSONString("[unk]"));
		_grammar = keywords.ToString();
	}

	private IEnumerator Decompress()
	{
		if (!Path.HasExtension(ModelPath)
			|| Directory.Exists(
				Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(ModelPath))))
		{
			OnStatusUpdated?.Invoke("Using existing decompressed model.");
			_decompressedModelPath =
				Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(ModelPath));
			Debug.Log(_decompressedModelPath);
			yield break;
		}

		OnStatusUpdated?.Invoke("Decompressing model...");
		string dataPath = Path.Combine(Application.streamingAssetsPath, ModelPath);

		Stream dataStream;
		if (dataPath.Contains("://"))
		{
			UnityWebRequest www = UnityWebRequest.Get(dataPath);
			www.SendWebRequest();
			while (!www.isDone)
			{
				yield return null;
			}
			dataStream = new MemoryStream(www.downloadHandler.data);
		}
		else
		{
			dataStream = File.OpenRead(dataPath);
		}

		var zipFile = ZipFile.Read(dataStream);
		zipFile.ExtractProgress += ZipFileOnExtractProgress;
		OnStatusUpdated?.Invoke("Reading Zip file");
		zipFile.ExtractAll(Application.persistentDataPath);

		while (_isDecompressing == false)
		{
			yield return null;
		}

		_decompressedModelPath = Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(ModelPath));
		OnStatusUpdated?.Invoke("Decompressing complete!");
		yield return new WaitForSeconds(1);
		zipFile.Dispose();
	}

	private void ZipFileOnExtractProgress(object sender, ExtractProgressEventArgs e)
	{
		if (e.EventType == ZipProgressEventType.Extracting_AfterExtractAll)
		{
			_isDecompressing = true;
			_decompressedModelPath = e.ExtractLocation;
		}
	}

	private IEnumerator WaitForMicrophoneInput()
	{
		while (Microphone.devices.Length <= 0)
			yield return null;
	}

	public void ToggleRecording()
	{
		Debug.Log("Toogle Recording");
		if (!VoiceProcessor.IsRecording)
		{
			Debug.Log("Start Recording");
			_running = true;
			VoiceProcessor.StartRecording();
			Task.Run(ThreadedWork).ConfigureAwait(false);
		}
		else
		{
			Debug.Log("Stop Recording");
			_running = false;
			VoiceProcessor.StopRecording();
		}
	}

	void Update()
	{
		while (_threadedResultQueue.TryDequeue(out string finalJson))
			OnTranscriptionResult?.Invoke(finalJson);
	}

	private void VoiceProcessorOnOnFrameCaptured(short[] samples)
	{
		_threadedBufferQueue.Enqueue(samples);
		_hasAudio.Set(); // NEW
	}

	private void VoiceProcessorOnOnRecordingStop()
	{
		Debug.Log("Stopped");
	}

	private async Task ThreadedWork()
	{
		voskRecognizerCreateMarker.Begin();
		if (!_recognizerReady)
		{
			UpdateGrammar();
			_recognizer = string.IsNullOrEmpty(_grammar)
				? new VoskRecognizer(_model, 16000.0f)
				: new VoskRecognizer(_model, 16000.0f, _grammar);

			_recognizer.SetMaxAlternatives(0);   // NEW: fastest
			_recognizer.SetWords(false);         // NEW: skip word timing
			_recognizerReady = true;
		}
		voskRecognizerCreateMarker.End();

		voskRecognizerReadMarker.Begin();
		while (_running)
		{
			if (!_threadedBufferQueue.TryDequeue(out short[] voiceFrame))
			{
				_hasAudio.WaitOne(20);   // NEW: blocks until audio or 20 ms timeout
				continue;
			}

			do
			{
				var partialJson = _recognizer.PartialResult();
				PartialResult = ExtractPartialText(partialJson);

				if (_recognizer.AcceptWaveform(voiceFrame, voiceFrame.Length))
				{
					_threadedResultQueue.Enqueue(_recognizer.Result());
				}
			}
			while (_threadedBufferQueue.TryDequeue(out voiceFrame)); // NEW: flush backlog
		}
		voskRecognizerReadMarker.End();
	}

	private static string ExtractPartialText(string json)
	{
		if (string.IsNullOrEmpty(json)) return null;
		var m = PartialJsonRx.Match(json);
		return m.Success ? m.Groups["t"].Value : null;
	}
}