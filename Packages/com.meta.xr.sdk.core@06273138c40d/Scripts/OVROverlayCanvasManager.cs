/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[DefaultExecutionOrder(-99)]
public class OVROverlayCanvasManager : MonoBehaviour
{
    private static OVROverlayCanvasManager _instance;
    public static OVROverlayCanvasManager Instance =>
        _instance != null ? _instance :
        Application.isPlaying ? _instance = new GameObject(nameof(OVROverlayCanvasManager)).AddComponent<OVROverlayCanvasManager>() :
        null;

    private List<OVROverlayCanvas> _canvases = new();

    public static void AddCanvas(OVROverlayCanvas canvas) => Instance?._canvases.Add(canvas);
    public static void RemoveCanvas(OVROverlayCanvas canvas) => _instance?._canvases.Remove(canvas);

    public bool IsCanvasPriority(OVROverlayCanvas canvas) =>
        canvas.GetViewPriorityScore() is not null &&
        _canvases.IndexOf(canvas) < OVROverlayCanvasSettings.Instance.MaxSimultaneousCanvases;

    public IEnumerable<OVROverlayCanvas> Canvases => _canvases;

    protected void Awake()
    {
        Debug.Assert(_instance == null, "Duplicate instance of OVROverlayCanvasManager", this);
        _instance = this;
        DontDestroyOnLoad(this);
    }

    protected void Update()
    {
        _canvases.Sort((a, b) =>
        {
            var scoreA = a.GetViewPriorityScore() ?? 0;
            var scoreB = b.GetViewPriorityScore() ?? 0;
            if (!Mathf.Approximately(scoreA, scoreB))
                return (int)((scoreB - scoreA) * 10000);

            // fallback to hashcode to keep it stable
            return b.GetHashCode() - a.GetHashCode();
        });
    }

    protected void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
