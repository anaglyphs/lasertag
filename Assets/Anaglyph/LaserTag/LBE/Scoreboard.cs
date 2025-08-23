#if UNITY_EDITOR
using Anaglyph.Lasertag;
using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class SceneLabel
{
	private static GUIStyle style = new GUIStyle();
	private static GUIStyle styleRed;
	private static GUIStyle styleBlue;


	static SceneLabel()
	{
		style = new GUIStyle();
		style.normal.textColor = Color.white;
		style.fontSize = 50;
		style.fontStyle = FontStyle.Bold;
		style.alignment = TextAnchor.MiddleCenter;

		styleRed = new GUIStyle(style);
		styleRed.normal.textColor = Color.red;
		styleRed.fontSize = 100;
		styleRed.fontStyle = FontStyle.Bold;
		styleRed.alignment = TextAnchor.MiddleCenter;

		styleBlue = new GUIStyle(style);
		styleBlue.normal.textColor = Color.cyan;
		styleBlue.fontSize = 100;
		styleBlue.fontStyle = FontStyle.Bold;
		styleBlue.alignment = TextAnchor.MiddleCenter;

		SceneView.duringSceneGui -= OnScene;
		SceneView.duringSceneGui += OnScene;
	}

	private static void OnScene(SceneView sceneview)
	{
		if (MatchManager.Instance == null)
			return;

		Handles.BeginGUI();

		GUILayout.BeginArea(new Rect(sceneview.camera.pixelWidth / 2 - 400, 0, 200, 200));
		GUILayout.Label(MatchManager.GetTeamScore(1).ToString(), styleRed);
		GUILayout.EndArea();

		GUILayout.BeginArea(new Rect(sceneview.camera.pixelWidth / 2 + 200, 0, 200, 200));
		GUILayout.Label(MatchManager.GetTeamScore(2).ToString(), styleBlue);
		GUILayout.EndArea();

		GUILayout.BeginArea(new Rect(sceneview.camera.pixelWidth / 2 - 200, 100, 400, 200));

		float timeLeft = Mathf.Max(MatchManager.TimeMatchEnds - Time.time, 0);

		TimeSpan time = TimeSpan.FromSeconds(timeLeft);
		string str = time.ToString(@"mm\:ss");
		GUILayout.Label(str, style);
		GUILayout.EndArea();

		Handles.EndGUI();
	}
}
#endif