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
using System.Text;
using Meta.XR.Editor.FalcoOVRTelemetry;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace Meta.XR.Samples.Editor
{
    [InitializeOnLoad]
    public class SampleMetadataTelemetry : IPreprocessBuildWithReport
    {
        private const float FrequencySec = 180; // we don't want to check on every compilation since it takes time

        private const string LastRunTimeKey = "CODE_SAMPLES_TELEMETRY_LAST_RUN_TIME";
        private const string LastListOfSamplesKey = "CODE_SAMPLES_TELEMETRY_LAST_LIST_SAMPLES";
        private const string EventNameCodeSampleUpdated = "CODE_SAMPLES_UPDATED";

        static SampleMetadataTelemetry()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.quitting += OnEditorQuitting;
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            CheckAndSendSampleTelemetryIfChanged(EventNameCodeSampleUpdated, true);
        }

        public static string GetSamplesListJson()
        {
            var allSamplesData = UpdateManifestWithCodeSample.FindAllMetaCodeSamplesAttributes();

            var sb = new StringBuilder();
            sb.Append("{");
            foreach (var kvp in allSamplesData)
            {
                sb.Append($"'{kvp.Key}': ");
                sb.Append($"'{string.Join(", ", kvp.Value)}',");
            }
            sb.Append("}");

            // save the list for Update detection
            SaveListOfSampleString(GetListOfSampleAsString(allSamplesData));

            var returnString = sb.ToString();
            returnString = returnString.Replace(",}", "}");
            return returnString;
        }

        private static void OnCompilationFinished(object obj)
        {
            CheckAndSendSampleTelemetryIfChanged(EventNameCodeSampleUpdated);
        }

        private static void OnEditorQuitting()
        {
            CheckAndSendSampleTelemetryIfChanged(EventNameCodeSampleUpdated, true);
        }

        private static void CheckAndSendSampleTelemetryIfChanged(string eventName, bool skipTimeCheck = false, bool sendEmpty = true)
        {
            if (!skipTimeCheck)
            {
                // Check that the proper amount of time elapsed since the last run
                var lastRunTime = SessionState.GetFloat(LastRunTimeKey, 0);
                if (lastRunTime > 0 &&
                    lastRunTime + FrequencySec > Time.realtimeSinceStartup)
                {
                    return;
                }
            }

            var allSamplesData = UpdateManifestWithCodeSample.FindAllMetaCodeSamplesAttributes();

            var listOfSamplesString = GetListOfSampleAsString(allSamplesData);
            var lastListOfSamples = SessionState.GetString(LastListOfSamplesKey, null);
            var hasChanged = lastListOfSamples != listOfSamplesString;
            if (!hasChanged)
            {
                return;
            }

            if (sendEmpty || allSamplesData.Count > 0)
            {
                var falcoEvent = OVRFalcoTelemetry.NewEvent(eventName);
                foreach (var kvp in allSamplesData)
                {
                    falcoEvent.AddMetadata(kvp.Key, kvp.Value);
                }

                falcoEvent.SendEssential();
            }

            SaveListOfSampleString(listOfSamplesString);
            SessionState.SetFloat(LastRunTimeKey, Time.realtimeSinceStartup);
        }

        private static string GetListOfSampleAsString(Dictionary<string, HashSet<string>> allSamplesData)
        {
            var listOfSamples = new List<string>(allSamplesData.Keys);
            listOfSamples.Sort(); // for list determinism
            return string.Join(", ", listOfSamples);
        }

        private static void SaveListOfSampleString(string listOfSamplesString)
        {
            SessionState.SetString(LastListOfSamplesKey, listOfSamplesString);
        }
    }
}
