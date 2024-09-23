using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class InGameConsole : MonoBehaviour
{
	public static bool longMessagesEnabled = true;

	private const int MaxCharacters = 5000;
	private const string logStart = "--- start ---";
	private const string colorClosing = "</color>";

	private static string lastLogEntry = "";
	private static string log = logStart;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	static void OnBeforeSceneLoadRuntimeMethod()
	{
		Application.logMessageReceived += OnLog;
	}

	private void OnApplicationQuit()
	{
		Application.logMessageReceived -= OnLog;
		onLogUpdated = delegate { };
	}

	private static void OnLog(string condition, string trace, LogType type)
	{
		Color entryColor = Color.white;

		bool isError = type == LogType.Error || type == LogType.Exception;

		if (type == LogType.Warning)
		{
			entryColor = new Color(1, 1, 0.5f);
		}
		else if (isError)
		{
			entryColor = new Color(1, 0.5f, 0.5f);
		}

		string entryBody = isError && longMessagesEnabled ? $"\n{trace}" : "";

		string logEntry = $"\n\n<color=#{ColorUtility.ToHtmlStringRGB(entryColor)}><b>{condition}</b>{entryBody}</color>";

		if(logEntry.Equals(lastLogEntry))
			return;

		lastLogEntry = logEntry;

		log += logEntry;

		bool wasTooLong = log.Length > MaxCharacters;

		if (log.Length > MaxCharacters)
		{
			int firstColorClosingIndex = log.IndexOf(colorClosing, log.Length - MaxCharacters);
			log = log.Substring(firstColorClosingIndex + colorClosing.Length);
		}

		onLogUpdated.Invoke();
	}

	private static Action onLogUpdated = delegate { };

	[SerializeField] private Text consoleText;
	[SerializeField] private ScrollRect rect;

	private void OnEnable()
	{
		UpdateText();
		onLogUpdated += UpdateText;

		StartCoroutine(ApplyScrollPosition());
	}

	private void Start()
	{
		onLogUpdated.Invoke();
	}

	private void OnDisable()
	{
		onLogUpdated -= UpdateText;
	}

	public void SetLongMessagesOn(bool enabled)
	{
		longMessagesEnabled = enabled;
	}

	public void Clear()
	{
		log = logStart;
		UpdateText();
	}

	private void UpdateText()
	{
		bool atBottom = rect.verticalNormalizedPosition < 0.0001f;

		consoleText.text = log;

		if (atBottom)
		{
			Canvas.ForceUpdateCanvases();
			StartCoroutine(ApplyScrollPosition());
		}
	}

	IEnumerator ApplyScrollPosition()
	{
		yield return new WaitForEndOfFrame();
		rect.verticalNormalizedPosition = 0;
		LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rect.transform);
	}
}