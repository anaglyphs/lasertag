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

#if UNITY_ANDROID && !UNITY_EDITOR
#define JNI_AVAILABLE
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public class OVRMetricsToolSDK : MonoBehaviour
{

    [Serializable]
    public struct MetricsSnapshot
    {
        public long time;

        public int available_memory_MB;
        public int app_pss_MB;
        public int battery_level_percentage;
        public int battery_temperature_celcius;
        public int battery_current_now_milliamps;
        public int sensor_temperature_celcius;
        public int power_current;
        public int power_level_state;
        public int power_voltage;
        public int power_wattage;
        public int cpu_level;
        public int gpu_level;
        public int cpu_frequency_MHz;
        public int gpu_frequency_MHz;
        public int mem_frequency_MHz;
        public int minimum_vsyncs;
        public int extra_latency_mode;
        public int phase_sync_mode;
        public int average_frame_rate;
        public int display_refresh_rate;
        public int average_prediction_milliseconds;
        public int screen_tear_count;
        public int early_frame_count;
        public int stale_frame_count;
        public int maximum_rotational_speed_degrees_per_second;
        public int foveation_level;
        public int eye_buffer_width;
        public int eye_buffer_height;
        public int app_gpu_time_microseconds;
        public int timewarp_gpu_time_microseconds;
        public int guardian_gpu_time_microseconds;
        public int cpu_utilization_percentage;
        public int cpu_utilization_percentage_core0;
        public int cpu_utilization_percentage_core1;
        public int cpu_utilization_percentage_core2;
        public int cpu_utilization_percentage_core3;
        public int cpu_utilization_percentage_core4;
        public int cpu_utilization_percentage_core5;
        public int cpu_utilization_percentage_core6;
        public int cpu_utilization_percentage_core7;
        public int gpu_utilization_percentage;
        public int spacewarp_motion_vector_type;
        public int spacewarped_frames_per_second;
        public int app_vss_MB;
        public int app_rss_MB;
        public int app_dalvik_pss_MB;
        public int app_private_dirty_MB;
        public int app_private_clean_MB;
        public int app_uss_MB;
        public int stale_frames_consecutive;
        public int max_repeated_frames;
        public int avg_vertices_per_frame;
        public int avg_fill_percentage;
        public int avg_inst_per_frag;
        public int avg_inst_per_vert;
        public int avg_frag_inst_per_pixel;
        public int avg_vert_inst_per_pixel;
        public int avg_textures_per_frag;
        public int percent_time_shading_frags;
        public int percent_time_shading_verts;
        public int percent_time_compute;
        public int percent_vertex_fetch_stall;
        public int percent_texture_fetch_stall;
        public int percent_texture_l1_miss;
        public int percent_texture_l2_miss;
        public int percent_texture_nearest_filtered;
        public int percent_texture_linear_filtered;
        public int percent_texture_anisotropic_filtered;
        public int vrshell_average_frame_rate;
        public int vrshell_gpu_time_microseconds;
        public int vrshell_and_guardian_gpu_time_microseconds;
        public int render_scale;
        public int dynres_recommendation_percentage;
        public int dynres_recommendation_width;
        public int dynres_recommendation_height;
    }

    [Serializable]
    public class AppMetricDefinition
    {
        public string Name;
        public string DisplayName;
        public string Group;
        public int RangeMin;
        public int RangeMax;
        public int GraphMin;
        public int GraphMax;
        public float RedPercent;
        public float GreenPercent;
        public bool ShowGraph;
        public bool ShowStat;
    }

    [Serializable]
    public class AppMetricDefinitions
    {
        public List<AppMetricDefinition> Metrics;
    }

    public class AppMetricValue
    {
        // json encoded name, including quotes, for easy serialization
        public string jsonName;
        public int value;
    }

    [Serializable]
    private struct JsonName
    {
        public string name;
    }


    private static AndroidJavaClass _MetricsService = null;
    private static AndroidJavaObject _Context = null;

    private static bool _NativeInitialized = false;
    private static bool _IsBound = false;
    private static OVRMetricsToolSDK _Instance;

    private bool _appMetricDefinitionsChanged = false;
    private Dictionary<string, int> _appMetricIds = new Dictionary<string, int>();
    private readonly AppMetricDefinitions _appMetricDefinitions = new AppMetricDefinitions() { Metrics = new List<AppMetricDefinition>() };
    private bool _appMetricValuesChanged = false;
    private readonly List<AppMetricValue> _appMetricValues = new List<AppMetricValue>();

    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_Initialize(IntPtr jvm, IntPtr jni, IntPtr context);
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_EnterVrMode();
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_AppendCsvDebugString(string debugString);
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_SetOverlayDebugString(string debugString);
    [DllImport("OVRMetricsTool")]
    private static extern IntPtr ovrMetricsTool_GetLatestEventJson();
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_DefineMetrics(string json);
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_SubmitMetrics(string json);
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_LeaveVrMode();
    [DllImport("OVRMetricsTool")]
    private static extern bool ovrMetricsTool_Shutdown();
    [DllImport("OVRMetricsTool")]
    private static extern IntPtr ovrMetricsTool_GetError();
    [DllImport("OVRMetricsTool")]
    private static extern void ovrMetricsTool_FreeString(IntPtr str);

    public static OVRMetricsToolSDK Instance
    {
        get
        {
            if (_Instance == null)
            {
                var go = new GameObject("OVRMetricsToolSDK") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);

                _Instance = go.AddComponent<OVRMetricsToolSDK>();
            }
            return _Instance;
        }
    }

    [System.Diagnostics.Conditional("JNI_AVAILABLE")]
    private static void Initialize()
    {
        if (_Context == null)
        {
            AndroidJavaClass unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _Context = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");
        }

        try
        {
            _NativeInitialized = ovrMetricsTool_Initialize(IntPtr.Zero, IntPtr.Zero, _Context.GetRawObject());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to Initialize Native ovrMetricsTool plugin. Falling back to Jni.\n" + ex);
            _NativeInitialized = false;
        }

        if (!_NativeInitialized)
        {
            if (_MetricsService == null)
            {
                try
                {
                    _MetricsService = new AndroidJavaClass("com.oculus.metrics.OVRMetricsToolClient");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Java OVRMetricsToolClient API is not available. Is OVRPlugin.aar enabled?\n" + ex);
                }
            }
        }
    }

    private void Awake()
    {
        Initialize();
        EnterVrMode();
    }

    private void OnDestroy()
    {
        LeaveVrMode();
        Shutdown();
    }

    private void OnApplicationPause(bool pause)
    {
        // We need to shutdown on pause to force OVR Metrics Tool into an unbound state.
        if (pause)
        {
            LeaveVrMode();
        }
        else
        {
            EnterVrMode();
        }
    }

    private void Update()
    {
        if (_IsBound && _appMetricDefinitionsChanged)
        {
            string json = JsonUtility.ToJson(_appMetricDefinitions);
            bool result = _NativeInitialized
                ? ovrMetricsTool_DefineMetrics(json)
                : _MetricsService.CallStatic<bool>("defineAppMetrics", _Context, json);
            _appMetricDefinitionsChanged = false;
            if (!result)
            {
                Debug.LogError($"[OVR Metrics Tool] DefineMetrics resulted in Error: {GetError()}");
                _IsBound = false;
            }
        }

        if (_IsBound && _appMetricValuesChanged)
        {
            string json = ToJson(_appMetricValues);
            bool result = _NativeInitialized
                ? ovrMetricsTool_SubmitMetrics(json)
                : _MetricsService.CallStatic<bool>("updateAppMetrics", _Context, json);
            _appMetricValuesChanged = false;
            if (!result)
            {
                Debug.LogError($"[OVR Metrics Tool] UpdateMetrics resulted in Error: {GetError()}");
                _IsBound = false;
            }
        }
    }

    [System.Diagnostics.Conditional("JNI_AVAILABLE")]
    private void EnterVrMode()
    {
        if (_IsBound)
        {
            return;
        }

        if (_NativeInitialized)
        {
            _IsBound = ovrMetricsTool_EnterVrMode();
        }
        else if (_MetricsService != null)
        {
            _MetricsService.CallStatic("bind", _Context);
            _IsBound = true;
        }

        if (!_IsBound)
        {
            Debug.LogError($"[OVR Metrics Tool] Failed to Bind MetricsService: {GetError()}");
        }
        else
        {
            // Refresh App Metrics upon entering VR Mode
            _appMetricDefinitionsChanged = true;
            _appMetricValuesChanged = true;
        }
    }

    [System.Diagnostics.Conditional("JNI_AVAILABLE")]
    private void LeaveVrMode()
    {
        if (!_IsBound)
        {
            return;
        }

        if (_NativeInitialized)
        {
            ovrMetricsTool_LeaveVrMode();
        }
        else
        {
            _MetricsService.CallStatic("shutdown", _Context);
        }
        _IsBound = false;
    }

    [System.Diagnostics.Conditional("JNI_AVAILABLE")]
    private void Shutdown()
    {
        if (_NativeInitialized)
        {
            ovrMetricsTool_Shutdown();
        }

        _NativeInitialized = false;
        _MetricsService = null;
    }


    public bool AppendCsvDebugString(string debugString)
    {
        if (!_IsBound)
        {
            return false;
        }

        bool result = _NativeInitialized
            ? ovrMetricsTool_AppendCsvDebugString(debugString)
            : _MetricsService.CallStatic<bool>("appendCsvDebugString", _Context, debugString);

        if (!result)
        {
            Debug.LogError($"[OVR Metrics Tool] AppendCsvDebugString resulted in Error: {GetError()}");
            _IsBound = false;
        }

        return result;
    }

    public bool SetOverlayDebugString(string debugString)
    {
        if (!_IsBound)
        {
            return false;
        }

        bool result = _NativeInitialized
            ? ovrMetricsTool_SetOverlayDebugString(debugString)
            : _MetricsService.CallStatic<bool>("setOverlayDebugString", _Context, debugString);

        if (!result)
        {
            Debug.LogError($"[OVR Metrics Tool] SetOverlayDebugString resulted in Error: {GetError()}");
            _IsBound = false;
        }

        return result;
    }

    public MetricsSnapshot? GetLatestMetricsSnapshot()
    {
        if (!_IsBound)
        {
            return null;
        }

        string result = null;
        if (_NativeInitialized)
        {
            IntPtr ptr = ovrMetricsTool_GetLatestEventJson();
            if (ptr != IntPtr.Zero)
            {
                result = Marshal.PtrToStringAnsi(ptr);
                ovrMetricsTool_FreeString(ptr);
            }
        }
        else
        {
            result = _MetricsService.CallStatic<string>("getLatestEventJson", _Context);
        }

        if (result == null)
        {
            Debug.LogError($"[OVR Metrics Tool] GetLatestMetricsSnapshot resulted in Error: {GetError()}");
            _IsBound = false;
            return null;
        }

        return JsonUtility.FromJson<MetricsSnapshot>(result);
    }

    public string GetError()
    {
        string result = null;
        if (_NativeInitialized)
        {
            IntPtr ptr = ovrMetricsTool_GetError();
            if (ptr != IntPtr.Zero)
            {
                result = Marshal.PtrToStringAnsi(ptr);
                ovrMetricsTool_FreeString(ptr);
            }
        }
        else
        {
            result = _MetricsService != null ? _MetricsService.CallStatic<string>("getError") :
                "Jni Unavailble";
        }
        return result;
    }

    public void DefineAppMetric(string name, string displayName, string group, int rangeMin, int rangeMax, int graphMin, int graphMax, float redPercent, float greenPercent, bool showGraph, bool showStat)
    {
        // if metric already exists, just update the definition for it.
        if (_appMetricIds.TryGetValue(name, out var metricIndex))
        {
            var metric = _appMetricDefinitions.Metrics[metricIndex];
            if (metric.DisplayName != displayName)
            {
                metric.DisplayName = displayName;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.Group != group)
            {
                metric.Group = group;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.RangeMin != rangeMin)
            {
                metric.RangeMin = rangeMin;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.RangeMax != rangeMax)
            {
                metric.RangeMax = rangeMax;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.GraphMin != graphMin)
            {
                metric.GraphMin = graphMin;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.GraphMax != graphMax)
            {
                metric.GraphMax = graphMax;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.RedPercent != redPercent)
            {
                metric.RedPercent = redPercent;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.GreenPercent != greenPercent)
            {
                metric.GreenPercent = greenPercent;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.ShowGraph != showGraph)
            {
                metric.ShowGraph = showGraph;
                _appMetricDefinitionsChanged = true;
            }
            if (metric.ShowStat != showStat)
            {
                metric.ShowStat = showStat;
                _appMetricDefinitionsChanged = true;
            }
        }
        else
        {
            _appMetricIds[name] = _appMetricDefinitions.Metrics.Count;
            _appMetricDefinitions.Metrics.Add(new AppMetricDefinition
            {
                Name = name,
                DisplayName = displayName,
                Group = group,
                RangeMin = rangeMin,
                RangeMax = rangeMax,
                GraphMin = graphMin,
                GraphMax = graphMax,
                RedPercent = redPercent,
                GreenPercent = greenPercent,
                ShowGraph = showGraph,
                ShowStat = showStat
            });
            _appMetricValues.Add(new AppMetricValue
            {
                jsonName = GetJsonName(name),
                value = 0
            });
            _appMetricDefinitionsChanged = true;
        }

        // if Metric Definitions changed, also update Metric values
        _appMetricValuesChanged |= _appMetricDefinitionsChanged;
    }

    public void DeleteAppMetric(string name)
    {
        if (_appMetricIds.TryGetValue(name, out var metricIndex))
        {
            // Swap last metric
            _appMetricIds.Remove(name);
            _appMetricDefinitions.Metrics.RemoveAt(metricIndex);
            _appMetricValues.RemoveAt(metricIndex);

            // update metric ids after removal (maintaining original order)
            for (int i = metricIndex; i < _appMetricDefinitions.Metrics.Count; i++)
            {
                _appMetricIds[_appMetricDefinitions.Metrics[i].Name] = i;
            }

            _appMetricValuesChanged = true;
            _appMetricDefinitionsChanged = true;
        }
    }

    public bool UpdateAppMetric(string name, int value)
    {
        if (_appMetricIds.TryGetValue(name, out var metricIndex))
        {
            var metric = _appMetricValues[metricIndex];
            if (metric.value != value)
            {
                metric.value = value;
                _appMetricValuesChanged = true;
            }
            return true;
        }
        return false;
    }

    private static string GetJsonName(string name)
    {
        string jsonName = JsonUtility.ToJson(new JsonName() { name = name });
        // remove `{"name":` from start, and `}` from end
        return jsonName.Substring(8, jsonName.Length - 9);
    }

    private static string ToJson(List<AppMetricValue> metricValues)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{");
        for (int i = 0; i < metricValues.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }
            sb.Append(metricValues[i].jsonName);
            sb.Append(":");
            sb.Append(metricValues[i].value);
        }
        sb.Append("}");
        return sb.ToString();
    }
}
