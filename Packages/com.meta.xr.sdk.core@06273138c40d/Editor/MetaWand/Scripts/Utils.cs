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

using System.ComponentModel;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Utils;
using Meta.XR.MetaWand.Editor.API;
using UnityEditor;
using UnityEngine;
using static Meta.XR.MetaWand.Editor.Styles.Contents;
using static Meta.XR.Editor.UserInterface.RLDS.Styles;

namespace Meta.XR.MetaWand.Editor
{
    internal static class Utils
    {

        public enum GeneratorType
        {
            Mesh,
        }

        private static int? _coreSdkVersion;
        public static int? CoreSdkVersion => _coreSdkVersion ??= PackageList.ComputePackageVersion(Constants.CoreSDKPackageName);

        public static readonly ToolDescriptor ToolDescriptorAssetLibrary = new()
        {
            Icon = AssetLibraryIcon,
            Name = Constants.AssetLibraryPublicName,
            MenuDescription = RemoteContent.GetText(Constants.AssetLibraryMenuDescriptionKey, Constants.AssetLibraryMenuDescription),
            AddToStatusMenu = true,
            AddToMenu = true,
            OnClickDelegate = ShowAssetLibrary,
            IsStatusMenuItemDarker = true,
            Experimental = true,
            DrawExperimentalInStatusMenu = true,
            EnableRampUp = true
        };
        public static void ShowAssetLibrary(Origins origin) => AssetLibraryWindow.ShowWindow(origin);

        private static string _cacheFilePath;
        public static string CacheFilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(_cacheFilePath))
                {
                    return _cacheFilePath;
                }

                var directory = Path.Combine(Path.GetTempPath(), "Meta", Constants.CacheDir);
                Directory.CreateDirectory(directory);
                _cacheFilePath = Path.Combine(directory, "cache");
                return _cacheFilePath;
            }
        }

        public static void OnFeedbackIconClicked()
        {
            var submitFeedbackEvent = OVRTelemetry.Start(OVRTelemetryConstants.Feedback.MarkerId.SubmitFeedback);
            try
            {
                using Process process = new Process();
                string mqdhCategoryId = "1046393670222453";
                process.StartInfo.FileName = XR.Editor.ToolingSupport.Utils.GetMqdhDeeplink(mqdhCategoryId);
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
            catch (Win32Exception)
            {
                submitFeedbackEvent.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                if (EditorUtility.DisplayDialog("Install Meta Quest Developer Hub",
                        "Meta Quest Developer Hub is not installed on this machine.", "Get Meta Quest Developer Hub",
                        "Cancel"))
                {
                    Application.OpenURL(
                        "https://developers.meta.com/horizon/documentation/unity/ts-odh-getting-started/");
                }
            }

            submitFeedbackEvent.AddAnnotation(OVRTelemetryConstants.Feedback.AnnotationType.ToolName, Constants.AssetLibraryPublicName).Send();
        }

        public static T ReadFromCache<T>() =>
            !File.Exists(CacheFilePath) ? default : FromByteArray<T>(File.ReadAllBytes(CacheFilePath));

        public static async Task WriteToCache<T>(T content) =>
            await File.WriteAllBytesAsync(CacheFilePath, ToByteArray(content));

        private static byte[] ToByteArray<T>(T content)
        {
            if (content == null)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            var formatter = new BinaryFormatter();
            formatter.Serialize(memoryStream, content);
            return memoryStream.ToArray();
        }

        private static T FromByteArray<T>(byte[] byteArray)
        {
            if (byteArray == null)
            {
                return default;
            }

            using var memoryStream = new MemoryStream(byteArray);
            var formatter = new BinaryFormatter();
            return (T)formatter.Deserialize(memoryStream);
        }

        public static void ClearCache()
        {
            if (File.Exists(CacheFilePath))
            {
                File.Delete(CacheFilePath);
            }
        }

        public static int CalculateGridSizeForWidth(Rect rect, int currentGridSize, int numOfGridItem, int gap)
        {
            var containerPadding = Styles.GUIStyles.ContentContainer.padding.left;
            var containerWidth = (int)rect.width;
            var expectedMinWidth = currentGridSize * numOfGridItem + gap * (numOfGridItem - 1) + containerPadding * 2;

            // Not enough space to accomodate grid items
            if (containerWidth < expectedMinWidth)
            {
                currentGridSize = Mathf.RoundToInt((containerWidth - gap * (numOfGridItem - 1) - containerPadding * 2) /
                                                   (float)numOfGridItem);
            }

            return currentGridSize;
        }

        public static string TruncateText(string text, int maxLength = 42)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }

        public static Dictionary<string, string> SplitUrlParameters(string url)
        {
            var queryStringIndex = url.IndexOf('?');
            if (queryStringIndex == -1)
            {
                // No query string found, return an empty dictionary
                return new Dictionary<string, string>();
            }

            var queryString = url.Substring(queryStringIndex + 1);
            var parameters = queryString.Split('&');
            Dictionary<string, string> parameterDictionary = new Dictionary<string, string>();
            foreach (string parameter in parameters)
            {
                var keyValue = parameter.Split(new[] { '=' }, 2); // Split only on the first '='
                var key = keyValue[0];
                var value = keyValue.Length > 1 ? keyValue[1] : "";
                parameterDictionary[key] = value;
            }

            return parameterDictionary;
        }

        public static float Remap(float current, float currentMin, float currentMax, float newMin, float newMax)
        {
            var proportion = (current - currentMin) / (currentMax - currentMin);
            var newValue = newMin + (proportion * (newMax - newMin));
            return newValue;
        }

        #region Reusable and static UI items

        private static Spinner _spinner;
        private static Spinner _spinnerMini;
        public const int IconSize = 36;
        public const int SpinnerSize = 20;
        public const int SpinnerMiniSize = 12;

        public static Spinner Spinner => _spinner ??= new Spinner();

        public static Spinner SpinnerMini => _spinnerMini ??= new Spinner(SpinnerMiniSize);

        public static void DrawSpinner() => Spinner.Draw();

        public static void DrawCenterAligned(IUserInterfaceItem item)
        {
            EditorGUILayout.BeginHorizontal();
            DrawFlexibleSpace();
            item.Draw();
            DrawFlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        public static void DrawFlexibleSpace() => new AddSpace(true).Draw();

        public static void DrawBorderedRectangle(Rect rect)
        {
            GUI.DrawTexture(rect, Styles.Colors.DarkBackground.ToTexture(), ScaleMode.ScaleAndCrop, false, 1, GUI.color,
                0, Radius.RadiusSM);
            GUI.DrawTexture(rect, Styles.Colors.BorderColor.ToTexture(), ScaleMode.ScaleAndCrop, false, 1, GUI.color,
                1.5f, Radius.RadiusSM);
        }

        public static void DrawURLLabel(string label, string url, GUIStyle style, Action onClick = null)
        {
            new Label(label, style).Draw();
            var rect = GUILayoutUtility.GetLastRect();
            var hover = rect.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseUp)
            {
                Application.OpenURL(url);
                onClick?.Invoke();
            }
        }

        public static void DrawActionLabel(string label, GUIStyle style, int width, Action onClick = null)
        {
            new Label(label, style, GUILayout.Width(width)).Draw();
            var rect = GUILayoutUtility.GetLastRect();
            var hover = rect.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseUp)
            {
                onClick?.Invoke();
            }
        }

        public static void DrawRoundedBackground(Rect rect, int cornerRadius, Texture background, float aspect = 1.0f)
        {
            GUI.DrawTexture(rect, background, ScaleMode.ScaleAndCrop, false, aspect, GUI.color, 0,
                cornerRadius);
        }

        public static void DrawIconLabel(TextureContent content, string label, GUIStyle style = null)
        {
            style ??= new GUIStyle(Styles.GUIStyles.Body2SupportingText)
            {
                padding = new RectOffset(0, 0, 0, Spacing.Space4XS),
                wordWrap = false
            };
            new Icon(content, Colors.IconSecondaryOnMedia, label)
            {
                LabelStyle = style
            }.Draw();
        }

        #endregion
    }
}
