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
using System.IO;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Meta.XR.Telemetry
{

    internal static class IssueTracker
    {

        public enum SDK
        {
            Core,
            BuildingBlocks,
            XRSim,
            ProjectSetupTool,
            MetaWand,
            MRUK
        }

        private static void SendEvent(SDK sdk, string issueCode, string eventType, string message, string memberName, int lineNumber, string fullPath)
        {
            string fileName = Path.GetFileName(fullPath);
            var unifiedEvent = new OVRPlugin.UnifiedEventData("INTEGRATION_ISSUE")
            {
                isEssential = OVRPlugin.Bool.True
            };
            unifiedEvent.SetMetadata("sdk", sdk.ToString());
            unifiedEvent.SetMetadata("issue_code", issueCode);
            unifiedEvent.SetMetadata("event_type", eventType);
            unifiedEvent.SetMetadata("message", message);
            unifiedEvent.SetMetadata("member_name", memberName);
            unifiedEvent.SetMetadata("line_number", lineNumber);
            unifiedEvent.SetMetadata("file_name", fileName);
            unifiedEvent.SetMetadata("openxr_runtime_name", OVRPlugin.runtimeName);
            unifiedEvent.Send();
        }

        /// <summary>
        /// Tracks an integration error by sending telemetry data and logging the error in the Unity editor.
        /// </summary>
        /// <param name="sdk">The SDK where the error occurred.</param>
        /// <param name="issueCode">A unique static code identifying the type of issue. Use a constant value to allow grouping of similar issues. Dynamic information should be placed in the message parameter.</param>
        /// <param name="message">A descriptive message about the error. This can contain dynamic information specific to this occurrence.</param>
        /// <param name="enableDebugLog">Whether to log the error to the Unity console.</param>
        /// <param name="memberName">The name of the calling member (automatically populated).</param>
        /// <param name="lineNumber">The line number where the error occurred (automatically populated).</param>
        /// <param name="fullPath">The file path where the error occurred (automatically populated).</param>
        public static void TrackError(SDK sdk, string issueCode, string message, bool enableDebugLog = true, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fullPath = "")
        {
            SendEvent(sdk, issueCode, "error", message, memberName, lineNumber, fullPath);
#if UNITY_EDITOR
            if (enableDebugLog)
            {
                Debug.LogError($"{message}\n[{sdk.ToString()}-{fullPath}-{memberName}:{lineNumber}] {issueCode}");
            }
#endif
        }

        /// <summary>
        /// Tracks an integration error with exception details by sending telemetry data and logging the error in the Unity editor.
        /// </summary>
        /// <param name="sdk">The SDK where the error occurred.</param>
        /// <param name="issueCode">A unique static code identifying the type of issue. Use a constant value to allow grouping of similar issues. Dynamic information should be placed in the exception parameter.</param>
        /// <param name="exception">The exception that was thrown. This contains dynamic information specific to this occurrence.</param>
        /// <param name="enableDebugLog">Whether to log the error to the Unity console.</param>
        /// <param name="memberName">The name of the calling member (automatically populated).</param>
        /// <param name="lineNumber">The line number where the error occurred (automatically populated).</param>
        /// <param name="fullPath">The file path where the error occurred (automatically populated).</param>
        public static void TrackError(SDK sdk, string issueCode, Exception exception, bool enableDebugLog = true, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fullPath = "")
        {
            SendEvent(sdk, issueCode, "error", exception.Message, memberName, lineNumber, fullPath);
#if UNITY_EDITOR
            if (enableDebugLog)
            {
                Debug.LogError($"{exception.Message}\n[{sdk.ToString()}-{fullPath}-{memberName}:{lineNumber}] {issueCode}\n{exception.StackTrace}");
            }
#endif
        }

        /// <summary>
        /// Tracks an integration warning by sending telemetry data and logging the warning in the Unity editor.
        /// </summary>
        /// <param name="sdk">The SDK where the warning occurred.</param>
        /// <param name="issueCode">A unique static code identifying the type of issue. Use a constant value to allow grouping of similar issues. Dynamic information should be placed in the message parameter.</param>
        /// <param name="message">A descriptive message about the warning. This can contain dynamic information specific to this occurrence.</param>
        /// <param name="enableDebugLog">Whether to log the warning to the Unity console.</param>
        /// <param name="memberName">The name of the calling member (automatically populated).</param>
        /// <param name="lineNumber">The line number where the warning occurred (automatically populated).</param>
        /// <param name="fullPath">The file path where the warning occurred (automatically populated).</param>
        public static void TrackWarning(SDK sdk, string issueCode, string message, bool enableDebugLog = true, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fullPath = "")
        {
            SendEvent(sdk, issueCode, "warning", message, memberName, lineNumber, fullPath);
#if UNITY_EDITOR
            if (enableDebugLog)
            {
                Debug.LogWarning($"{message}\n[{sdk.ToString()}-{fullPath}-{memberName}:{lineNumber}] {issueCode}");
            }
#endif
        }

        /// <summary>
        /// Tracks an integration warning with exception details by sending telemetry data and logging the warning in the Unity editor.
        /// </summary>
        /// <param name="sdk">The SDK where the warning occurred.</param>
        /// <param name="issueCode">A unique static code identifying the type of issue. Use a constant value to allow grouping of similar issues. Dynamic information should be placed in the exception parameter.</param>
        /// <param name="exception">The exception that was thrown. This contains dynamic information specific to this occurrence.</param>
        /// <param name="enableDebugLog">Whether to log the warning to the Unity console.</param>
        /// <param name="memberName">The name of the calling member (automatically populated).</param>
        /// <param name="lineNumber">The line number where the warning occurred (automatically populated).</param>
        /// <param name="fullPath">The file path where the warning occurred (automatically populated).</param>
        public static void TrackWarning(SDK sdk, string issueCode, Exception exception, bool enableDebugLog = true, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fullPath = "")
        {
            SendEvent(sdk, issueCode, "warning", exception.Message, memberName, lineNumber, fullPath);
#if UNITY_EDITOR
            if (enableDebugLog)
            {
                Debug.LogWarning($"{exception.Message}\n[{sdk.ToString()}-{fullPath}-{memberName}:{lineNumber}] {issueCode}\n{exception.StackTrace}");
            }
#endif
        }
    }
}
