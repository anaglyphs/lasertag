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
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.Tags
{
    internal class TagBehavior
    {
        public static readonly Dictionary<Tag, TagBehavior> Registry = new();

        private Tag _tag;

        public int Order { get; set; }
        public bool Automated { get; set; }
        public Color Color { get; set; } = Utils.HexToColor("#DDDDDD");
        public TextureContent Icon { get; set; }

        /// <summary>
        /// Show : Whether or not the tag is visually represented in the UI
        /// Some tags are purely for internal system use and not meant to be shown in the UI
        /// </summary>
        public bool Show { get; set; } = true;
        public bool ShowOverlay { get; set; }
        public bool CanFilterBy { get; set; }

        /// <summary>
        /// Visibility : Wether or not the block is actually visible
        /// These tags let us hide blocks, if they're for instance : Hidden, Obsolete, Internal
        /// </summary>
        public bool ToggleableVisibility { get; set; }
        public bool DefaultVisibility { get; set; } = true;
        public bool Visibility => VisibilitySetting.Value;
        private OVRProjectSetupSettingBool _visibilitySetting;
        public OVRProjectSetupSettingBool VisibilitySetting
            => _visibilitySetting ??= new OVRProjectSetupUserSettingBool($"Tag_{_tag.Name}_Visibility", DefaultVisibility, $"Show {_tag.Name} blocks");

        private GUIStyle _style;
        private GUIContent _content;
        private float? _styleWidth;
        private GUIStyle Style => _style ??= Icon != null ? Styles.GUIStyles.TagStyleWithIcon : Styles.GUIStyles.TagStyle;
        private GUIContent Content => _content ??= new GUIContent(_tag.Name);
        public float StyleWidth => _styleWidth ??= Style.CalcSize(Content).x + 1;

        public static TagBehavior GetBehavior(Tag tag)
        {
            if (tag == null)
            {
                throw new ArgumentNullException(nameof(tag));
            }

            if (!Registry.TryGetValue(tag, out var tagBehavior))
            {
                tagBehavior = new TagBehavior(tag);
            }
            return tagBehavior;
        }

        private TagBehavior(Tag tag)
        {
            _tag = tag;
            Registry[tag] = this;
        }

        private void DrawIcon(Rect rect)
        {
            if (Icon == null) return;
            GUI.Label(rect, Icon, Styles.GUIStyles.TagIcon);
        }

        private bool DrawButton(string id, Rect rect, out bool hover)
        {
            return OVREditorUtils.HoverHelper.Button(id, rect, Content, Style, out hover);
        }

        public bool ShouldDraw(Tag.TagListType listType)
        {
            if (!Show)
            {
                return false;
            }

            if (!Visibility)
            {
                return false;
            }

            switch (listType)
            {
                case Tag.TagListType.Filters when !CanFilterBy:
                case Tag.TagListType.Overlays when !ShowOverlay:
                    return false;
            }

            return true;
        }

        public bool Draw(string controlId, Tag.TagListType listType, bool active, out bool hover, out bool clicked)
        {
            hover = false;
            clicked = false;
            if (!ShouldDraw(listType)) return false;

            var id = controlId + _tag.Name;
            var backgroundColors = listType == Tag.TagListType.Overlays ? Styles.GUIStyles.TagOverlayBackgroundColors : Styles.GUIStyles.TagBackgroundColors;
            var color = backgroundColors.GetColor(active, OVREditorUtils.HoverHelper.IsHover(id));
            var rect = GUILayoutUtility.GetRect(Content, Style, GUILayout.Width(StyleWidth));
            using var backgroundColorScope = new Utils.ColorScope(Utils.ColorScope.Scope.Background, color);
            using var contentColorScope = new Utils.ColorScope(Utils.ColorScope.Scope.Content, Color);
            clicked = DrawButton(id, rect, out hover);
            DrawIcon(rect);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return true;
        }
    }
}
