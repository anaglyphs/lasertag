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



using System.Text.RegularExpressions;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    public static class Utils
    {
        private const int MaxLetterCountForTitle = 22;

        // Console
        internal static readonly Vector3 ConsolePanelClosePosition = new(0.8f, 0.51f, 0.01f);
        internal static readonly Vector3 ConsolePanelDefaultPosition = new(1.0f, 0.5f, 0.01f);
        internal static readonly Vector3 ConsolePanelFarPosition = new(1.2f, 0.5f, 0.01f);

        // Inspectors
        internal static readonly Vector3 InspectorsPanelClosePosition = new(0.8f, -0.51f, 0.01f);
        internal static readonly Vector3 InspectorsPanelDefaultPosition = new(1.0f, -0.5f, 0.01f);
        internal static readonly Vector3 InspectorsPanelFarPosition = new(1.20f, -0.5f, 0.01f);

        public static string ToDisplayText(this string input)
        {
            string output = Regex.Replace(input, @"([a-z])([A-Z])", "$1 $2");
            output = Regex.Replace(output, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            output = output.Replace("_", " ");
            output = char.ToUpper(output[0]) + output.Substring(1);

            if (output.Length > MaxLetterCountForTitle)
                output = output.Substring(0, MaxLetterCountForTitle);

            return output;
        }

        public static Vector3 LerpPosition(Vector3 current, Vector3 target, float lerpSpeed)
        {
            if (Vector3.Distance(current, target) < 0.01f)
            {
                return target;
            }

            current = Vector3.Lerp(current, target, Time.deltaTime * lerpSpeed);
            return current;
        }

        public static string ClampText(string text, int limit) => text.Length > limit ? text.Substring(0, limit) : text;
    }
}

