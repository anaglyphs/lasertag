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

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    private readonly List<OVROverlayCanvas> _canvases = new();

    public static void AddCanvas(OVROverlayCanvas canvas) => Instance?._canvases.Add(canvas);
    public static void RemoveCanvas(OVROverlayCanvas canvas) => _instance?._canvases.Remove(canvas);

    public IEnumerable<OVROverlayCanvas> Canvases => _canvases;

    protected void Awake()
    {
        Debug.Assert(_instance == null, "Duplicate instance of OVROverlayCanvasManager", this);
        _instance = this;
        DontDestroyOnLoad(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InsertSorted(int* indexList, float* scoreList, int index, float score)
    {
        int insertIndex = index;
        while (insertIndex > 0 && scoreList[insertIndex - 1] < score && !Mathf.Approximately(scoreList[insertIndex - 1], score))
        {
            scoreList[insertIndex] = scoreList[insertIndex - 1];
            indexList[insertIndex] = indexList[insertIndex - 1];
            insertIndex--;
        }

        scoreList[insertIndex] = score;
        indexList[insertIndex] = index;
    }


    private void UpdateCanvasPriorities()
    {
        unsafe
        {
            // Sort the canvases by priority, and update IsCanvasPriority
            int* indexSortList = stackalloc int[_canvases.Count];
            float* prioritySortList = stackalloc float[_canvases.Count];
            for (int i = 0; i < _canvases.Count; i++)
            {
                InsertSorted(indexSortList, prioritySortList, i, _canvases[i].GetViewPriorityScore() ?? -1000);
            }

            for (int i = 0; i < _canvases.Count; i++)
            {
                _canvases[indexSortList[i]].IsCanvasPriority =
                    i < OVROverlayCanvasSettings.Instance.MaxSimultaneousCanvases;
            }
        }
    }

    private void UpdateCanvasDepths()
    {
        unsafe
        {
            // Sort the canvases by distance, and update CanvasDepth
            int* indexSortList = stackalloc int[_canvases.Count];
            float* distanceSortList = stackalloc float[_canvases.Count];
            for (int i = 0; i < _canvases.Count; i++)
            {
                InsertSorted(indexSortList, distanceSortList, i, _canvases[i].GetViewDistance() ?? 1000);
            }

            for (int i = 0; i < _canvases.Count; i++)
            {
                _canvases[indexSortList[i]].CanvasDepth = -i;
            }
        }
    }
    protected void Update()
    {
        UpdateCanvasPriorities();
        UpdateCanvasDepths();
    }

    protected void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
