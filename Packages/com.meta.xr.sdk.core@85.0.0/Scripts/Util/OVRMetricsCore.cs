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
using UnityEngine;
#if UNITY_PROFILING && UNITY_ANDROID
using Unity.Profiling;
#endif

public class OVRMetricsCore
{
    [Flags]
    public enum AppMemoryMetric
    {
        AppCommittedMemory = (1 << 0),
        AppResidentMemory = (1 << 1),
        AudioReservedMemory = (1 << 2),
        AudioUsedMemory = (1 << 3),
        GCReservedMemory = (1 << 4),
        GCUsedMemory = (1 << 5),
        ProfilerReservedMemory = (1 << 6),
        ProfilerUsedMemory = (1 << 7),
        SystemTotalUsedMemory = (1 << 8),
        SystemUsedMemory = (1 << 9),
        TotalReservedMemory = (1 << 10),
        TotalUsedMemory = (1 << 11),
        VideoReservedMemory = (1 << 12),
        VideoUsedMemory = (1 << 13),
    }

    [Flags]
    public enum AppRenderMetric
    {
        BatchesCount = (1 << 0),
        CPUMainThreadFrameTime = (1 << 1),
        CPURenderThreadFrameTime = (1 << 2),
        CPUTotalFrameTime = (1 << 3),
        DrawCallsCount = (1 << 4),
        GPUFrameTime = (1 << 5),
        IndexBufferUploadInFrameBytes = (1 << 6),
        IndexBufferUploadInFrameCount = (1 << 7),
        RenderTexturesBytes = (1 << 8),
        RenderTexturesChangesCount = (1 << 9),
        RenderTexturesCount = (1 << 10),
        SetPassCallsCount = (1 << 11),
        ShadowCastersCount = (1 << 12),
        TrianglesCount = (1 << 13),
        UsedBuffersBytes = (1 << 14),
        UsedBuffersCount = (1 << 15),
        VertexBufferUploadInFrameBytes = (1 << 16),
        VertexBufferUploadInFrameCount = (1 << 17),
        VerticesCount = (1 << 18),
        VideoMemoryBytes = (1 << 19),
        VisibleSkinnedMeshesCount = (1 << 20)
    }

    [Serializable]
    public struct AppMetricsVisibilityConfiguration
    {
        public AppMemoryMetric visibleMemoryMetricStats;
        public AppMemoryMetric visibleMemoryMetricGraphs;
        public AppRenderMetric visibleRenderMetricStats;
        public AppRenderMetric visibleRenderMetricGraphs;
    }


#if UNITY_PROFILING && UNITY_ANDROID
    private struct AppMetricType
    {
        public AppMemoryMetric memoryMetric;
        public AppRenderMetric renderMetric;
        public ProfilerCategory category;

        public static implicit operator AppMetricType(AppMemoryMetric metric)
        {
            return new AppMetricType
            {
                memoryMetric = metric,
                renderMetric = 0,
                category = ProfilerCategory.Memory
            };
        }

        public static implicit operator AppMetricType(AppRenderMetric metric)
        {
            return new AppMetricType
            {
                memoryMetric = 0,
                renderMetric = metric,
                category = ProfilerCategory.Render
            };
        }
    }

    private class NamedStatRecorder
    {
        public double multiplier;
        public string name;
        public ProfilerRecorder recorder;
    }

    private struct AvailableMetric
    {
        public AppMetricType metric;
        public string name;
        public string displayName;
        public ProfilerMarkerDataUnit unit;
        public double multiplier;
        public int maxValue;
        public int graphMin;
        public int graphMax;
        public float redPercent;
        public float greenPercent;
        public bool developmentOnly;
    }

    private const double kBytesToMegabytes = 1.0 / (1024 * 1024);
    private const double kBytesToKilobytes = 1.0 / 1024;
    private const double kNanoSecondsToMicroseconds = 1.0 / 1000;
    // Default max for frame time graphs is 2x the length of a frame.
    // Assume default FPS of 72.
    private const int kDefaultFrameTimeGraphMax = (int)(2 * 1000.0 * 1000.0 / 72.0);

    private static readonly AvailableMetric[] kAvailableMetrics =
    {
        new AvailableMetric
        {
            metric = AppMemoryMetric.AppCommittedMemory, name = "App Committed Memory", displayName = "COM MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.AppResidentMemory, name = "App Resident Memory", displayName = "RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.AudioReservedMemory,
            name = "Audio Reserved Memory", displayName = "AUD RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.AudioUsedMemory, name = "Audio Used Memory", displayName = "AUD U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.GCReservedMemory, name = "GC Reserved Memory", displayName = "GC RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.GCUsedMemory, name = "GC Used Memory", displayName = "GC U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.ProfilerReservedMemory,
            name = "Profiler Reserved Memory", displayName = "PROF RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.ProfilerUsedMemory, name = "Profiler Used Memory", displayName = "PROF U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.SystemTotalUsedMemory,
            name = "System Total Used Memory", displayName = "SYS TOT MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.SystemUsedMemory, name = "System Used Memory", displayName = "SYS U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.TotalReservedMemory,
            name = "Total Reserved Memory", displayName = "TOT RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.TotalUsedMemory, name = "Total Used Memory", displayName = "TOT U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999, graphMin = 0, graphMax = 8192, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.VideoReservedMemory,
            name = "Video Reserved Memory", displayName = "VID RES MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppMemoryMetric.VideoUsedMemory, name = "Video Used Memory", displayName = "VID U MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.BatchesCount, name = "Batches Count", displayName = "BATCH",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 9999, graphMin = 0, graphMax = 2000, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.CPUMainThreadFrameTime,
            name = "CPU Main Thread Frame Time", displayName = "CPU MAIN T",
            unit = ProfilerMarkerDataUnit.TimeNanoseconds,
            multiplier = kNanoSecondsToMicroseconds, maxValue = 99999, developmentOnly = true, graphMin = 0, graphMax = kDefaultFrameTimeGraphMax, greenPercent = 50, redPercent = 60
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.CPURenderThreadFrameTime,
            name = "CPU Render Thread Frame Time", displayName = "CPU REND T",
            unit = ProfilerMarkerDataUnit.TimeNanoseconds,
            multiplier = kNanoSecondsToMicroseconds, maxValue = 99999, developmentOnly = true, graphMin = 0, graphMax = kDefaultFrameTimeGraphMax, greenPercent = 50, redPercent = 60
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.CPUTotalFrameTime, name = "CPU Total Frame Time", displayName = "CPU T",
            unit = ProfilerMarkerDataUnit.TimeNanoseconds,
            multiplier = kNanoSecondsToMicroseconds, maxValue = 99999, developmentOnly = true, graphMin = 0, graphMax = kDefaultFrameTimeGraphMax, greenPercent = 50, redPercent = 60
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.DrawCallsCount, name = "Draw Calls Count", displayName = "DRAW",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 9999, graphMin = 0, graphMax = 2000, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.GPUFrameTime, name = "GPU Frame Time", displayName = "GPU T",
            unit = ProfilerMarkerDataUnit.TimeNanoseconds,
            multiplier = kNanoSecondsToMicroseconds, maxValue = 99999, developmentOnly = true, graphMin = 0, graphMax = kDefaultFrameTimeGraphMax, greenPercent = 50, redPercent = 60
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.IndexBufferUploadInFrameBytes,
            name = "Index Buffer Upload In Frame Bytes", displayName = "IX UP KB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToKilobytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.IndexBufferUploadInFrameCount,
            name = "Index Buffer Upload In Frame Count", displayName = "IX UP",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 999, graphMin = 0, graphMax = 100
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.RenderTexturesBytes, name = "Render Textures Bytes", displayName = "RT MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.RenderTexturesChangesCount,
            name = "Render Textures Changes Count", displayName = "RT CHG",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 999, graphMin = 0, graphMax = 50, greenPercent = 20, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.RenderTexturesCount, name = "Render Textures Count", displayName = "RT",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 999, graphMin = 0, graphMax = 50, greenPercent = 20, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.SetPassCallsCount, name = "SetPass Calls Count", displayName = "SETPASS",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 9999, graphMin = 0, graphMax = 2000, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.ShadowCastersCount, name = "Shadow Casters Count", displayName = "SHADOW",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 9999, graphMin = 0, graphMax = 2000, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.TrianglesCount, name = "Triangles Count", displayName = "TRIS K",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1 / 1000.0, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.UsedBuffersBytes, name = "Used Buffers Bytes", displayName = "BUF MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.UsedBuffersCount, name = "Used Buffers Count", displayName = "BUF CNT",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 9999, graphMin = 0, graphMax = 2000
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.VertexBufferUploadInFrameBytes,
            name = "Vertex Buffer Upload In Frame Bytes", displayName = "VX UP KB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToKilobytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.VertexBufferUploadInFrameCount,
            name = "Vertex Buffer Upload In Frame Count", displayName = "VX UP",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 999, graphMin = 0, graphMax = 100
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.VerticesCount, name = "Vertices Count", displayName = "VERTS K",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1 / 1000.0, maxValue = 9999, graphMin = 0, graphMax = 2000, greenPercent = 30, redPercent = 50
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.VideoMemoryBytes, name = "Video Memory Bytes", displayName = "VID MB",
            unit = ProfilerMarkerDataUnit.Bytes, multiplier = kBytesToMegabytes, maxValue = 9999
        },
        new AvailableMetric
        {
            metric = AppRenderMetric.VisibleSkinnedMeshesCount,
            name = "Visible Skinned Meshes Count", displayName = "SKIN",
            unit = ProfilerMarkerDataUnit.Count, multiplier = 1, maxValue = 999, graphMin = 0, graphMax = 100, greenPercent = 40, redPercent = 60
        }
    };

    private readonly List<NamedStatRecorder> recorders = new();


    private static bool IsMetricAvailable(bool developmentOnly)
    {
#if DEVELOPMENT_BUILD
        return true;
#else
        return !developmentOnly;
#endif
    }

    private bool ShowGraph(in AppMetricType metric, in AppMetricsVisibilityConfiguration config)
    {
        return (metric.memoryMetric & config.visibleMemoryMetricGraphs) != 0 || (metric.renderMetric & config.visibleRenderMetricGraphs) != 0;
    }

    private bool ShowStat(in AppMetricType metric, in AppMetricsVisibilityConfiguration config)
    {
        return (metric.memoryMetric & config.visibleMemoryMetricStats) != 0 || (metric.renderMetric & config.visibleRenderMetricStats) != 0;
    }
#endif


    public void Update()
    {
#if UNITY_PROFILING && UNITY_ANDROID
        foreach (var recorder in recorders)
        {
            OVRMetricsToolSDK.Instance.UpdateAppMetric(recorder.name,
                (int)(recorder.recorder.LastValueAsDouble * recorder.multiplier));
        }
#endif
    }

    public void EnableMetrics(in AppMetricsVisibilityConfiguration config)
    {
#if UNITY_PROFILING && UNITY_ANDROID
        bool recording = recorders.Count > 0;
        foreach (var metric in kAvailableMetrics)
        {
            if (!IsMetricAvailable(metric.developmentOnly))
            {
                continue;
            }

            if (!recording) {
                // If we are already recording, we don't need to recreate the recorder
                recorders.Add(new NamedStatRecorder
                {
                    name = metric.name,
                    recorder = ProfilerRecorder.StartNew(metric.metric.category, metric.name),
                    multiplier = metric.multiplier
                });
            }

            OVRMetricsToolSDK.Instance.DefineAppMetric(metric.name, metric.displayName,
                metric.metric.category.Name, 0, metric.maxValue, metric.graphMin, metric.graphMax, metric.redPercent, metric.greenPercent, ShowGraph(metric.metric, in config),
                ShowStat(metric.metric, in config));
        }
#endif
    }

    public void DisableMetrics()
    {
#if UNITY_PROFILING && UNITY_ANDROID
        foreach (var recorder in recorders)
        {
            OVRMetricsToolSDK.Instance.DeleteAppMetric(recorder.name);
            recorder.recorder.Dispose();
        }
        recorders.Clear();
#endif
    }
}
