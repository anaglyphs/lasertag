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
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class OVREditorUtils
{
    internal const string MetaXRPublicName = "Meta XR";
    internal static readonly string MetaXRSettingsName = $"{MetaXRPublicName} Settings";

    internal static double LastUpdateTime;
    internal static float DeltaTime { get; private set; }

    internal static Item SettingsItem = new Item()
    {
        Name = MetaXRSettingsName,
        Color = Utils.HexToColor("#c4c4c4"),
        Icon = TextureContent.CreateContent("ovr_icon_settings.png", TextureContent.Categories.Generic),
        InfoTextDelegate = ComputeInfoText,
        OnClickDelegate = OnStatusMenuClick,
        Order = 100
    };

    static OVREditorUtils()
    {
        EditorApplication.update -= UpdateEditor;
        EditorApplication.update += UpdateEditor;

        StatusMenu.RegisterItem(SettingsItem);
    }

    internal static void UpdateEditor()
    {
        var timeSinceStartup = EditorApplication.timeSinceStartup;
        DeltaTime = (float)(timeSinceStartup - LastUpdateTime);
        LastUpdateTime = timeSinceStartup;
    }

    private static (string, Color?) ComputeInfoText() => ("Open settings menu.", null);


    private static void OnStatusMenuClick(Item.Origins origins)
    {
        OVRUserSettingsProvider.OpenSettingsWindow(origins);
    }

    public static string ChoosePlural(int number, string singular, string plural)
    {
        return number > 1 ? plural : singular;
    }

    public static bool IsUnityVersionCompatible()
    {
#if UNITY_2021_3_OR_NEWER
        return true;
#else
        return false;
#endif
    }

    public static string VersionCompatible => "2021.3";

    public static bool IsMainEditor()
    {
        // Early Return when the process service is not the Editor itself
#if UNITY_2021_1_OR_NEWER
        return (uint)UnityEditor.MPE.ProcessService.level != (uint)UnityEditor.MPE.ProcessLevel.Secondary;
#else
        return (uint)UnityEditor.MPE.ProcessService.level != (uint)UnityEditor.MPE.ProcessLevel.Slave;
#endif
    }

    public readonly struct UndoScope : System.IDisposable
    {
        private readonly int _group;
        private readonly string _name;

        public UndoScope(string name)
        {
            Undo.IncrementCurrentGroup();
            _group = Undo.GetCurrentGroup();
            _name = name;
        }

        public void Dispose()
        {
            Undo.SetCurrentGroupName(_name);
            Undo.CollapseUndoOperations(_group);
        }
    }

    public static class HoverHelper
    {
        private static readonly Dictionary<string, bool> Hovers = new();

        public static void Reset()
        {
            Hovers.Clear();
        }

        public static bool IsHover(string id, Event ev = null, Rect? area = null)
        {
            var hover = false;
            if (area.HasValue && ev?.type == EventType.Repaint)
            {
                hover = area.Value.Contains(ev.mousePosition);
                Hovers[id] = hover;
                return hover;
            }

            Hovers.TryGetValue(id, out hover);
            return hover;
        }

        public static bool Button(string id, GUIContent content, GUIStyle style, out bool hover)
        {
            var isClicked = GUILayout.Button(content, style);
            hover = IsHover(id, Event.current, GUILayoutUtility.GetLastRect());
            return isClicked;
        }

        public static bool Button(string id, Rect rect, GUIContent content, GUIStyle style, out bool hover)
        {
            var isClicked = GUI.Button(rect, content, style);
            hover = IsHover(id, Event.current, rect);
            return isClicked;
        }

        public static bool Button(string id, GUIContent label, GUIContent icon, GUIStyle buttonStyle, GUIStyle iconStyle, out bool hover)
        {
            var isClicked = GUILayout.Button(label, buttonStyle);
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.LabelField(rect, icon, iconStyle);
            hover = IsHover(id, Event.current, rect);
            return isClicked;
        }
    }

    public static class TweenHelper
    {
        private static readonly Dictionary<string, float> Tweens = new();

        public static void Reset()
        {
            Tweens.Clear();
        }

        public static float GetTweenValue(string id, float target, float? start)
        {
            if (!Tweens.TryGetValue(id, out var current))
            {
                current = start ?? target;
                Tweens[id] = current;
            }

            return current;
        }

        public static float Smooth(string id,
            float target,
            out bool completed,
            float? start = null,
            float speed = 10.0f,
            float epsilon = 5.0f)
        {
            var current = GetTweenValue(id, target, start);

            if (Math.Abs(target - current) <= epsilon)
            {
                current = target;
                Tweens[id] = current;
                completed = true;
            }
            else
            {
                current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * DeltaTime));
                Tweens[id] = current;
                completed = false;
            }

            return current;
        }

        public static float GUISmooth(string id, float target, float? start = null,
            float speed = 10.0f, float epsilon = 5.0f, Action ifNotCompletedDelegate = null)
        {
            var shouldUpdate = Event.current.type == EventType.Layout;
            var completed = true;
            var current = shouldUpdate
                ? Smooth(id, target, out completed, start, speed, epsilon)
                : GetTweenValue(id, target, start);

            if (!completed)
            {
                ifNotCompletedDelegate?.Invoke();
            }

            return current;
        }
    }
}
