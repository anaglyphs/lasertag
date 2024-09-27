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
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.Tags
{
    [CustomPropertyDrawer(typeof(TagArray), true)]
    internal class TagArrayDrawer : PropertyDrawer
    {
        internal const string AddNewTag = "Add New Tag";
        internal const string NewTagTextField = "NewTagTextField";

        private static bool _addingNewTag;
        private static string _newTag;

        public static string[] GetTagOptions()
        {
            var tags = Tag.Registry.SortedTags.Where(tag => !tag.Behavior.Automated).ToList();
            var options = new string[tags.Count + 1];
            var index = 0;
            foreach (var tag in tags)
            {
                options[index++] = tag;
            }

            options[index++] = AddNewTag;

            return options;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            if (_addingNewTag && !GUI.GetNameOfFocusedControl().Equals(NewTagTextField) && !string.IsNullOrEmpty(_newTag))
            {
                _addingNewTag = false;
                var tag = new Tag(_newTag);
                _newTag = null;
                AddTag(property, tag);
            }

            var tagOptions = GetTagOptions();
            var currentMask = GetCurrentMask(property, tagOptions);

            if (_addingNewTag)
            {
                var rect = EditorGUI.PrefixLabel(position, new GUIContent(label));
                GUI.SetNextControlName(NewTagTextField);
                _newTag = EditorGUI.TextField(rect, _newTag);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var newMask = EditorGUI.MaskField(position, label, currentMask, tagOptions);
                if (EditorGUI.EndChangeCheck())
                {
                    UpdateTagList(property, newMask, tagOptions);
                }
            }

            EditorGUI.EndProperty();
        }

        internal static int GetCurrentMask(SerializedProperty property, string[] tagOptions)
        {
            var arrayProperty = property.FindPropertyRelative("array");
            var mask = 0;
            for (var i = 0; i < arrayProperty.arraySize; i++)
            {
                var tagProperty = arrayProperty.GetArrayElementAtIndex(i);
                var tag = new Tag(tagProperty.FindPropertyRelative("name").stringValue);

                var index = Array.IndexOf(tagOptions, tag.Name);
                if (index >= 0)
                {
                    mask |= 1 << index;
                }
            }

            return mask;
        }

        internal static void UpdateTagList(SerializedProperty property, int mask, string[] tagOptions)
        {
            var arrayProperty = property.FindPropertyRelative("array");
            arrayProperty.ClearArray();
            for (var i = 0; i < tagOptions.Length; i++)
            {
                if ((mask & (1 << i)) == 0) continue;

                var label = tagOptions[i];

                if (label == AddNewTag)
                {
                    _addingNewTag = true;
                    _newTag = null;
                }
                else
                {
                    AddTag(property, new Tag(label), false);
                }
            }
            arrayProperty.serializedObject.ApplyModifiedProperties();
        }

        internal static void AddTag(SerializedProperty property, Tag tag, bool apply = true)
        {
            if (HasTag(property, tag))
            {
                return;
            }

            var arrayProperty = property.FindPropertyRelative("array");
            arrayProperty.InsertArrayElementAtIndex(arrayProperty.arraySize);
            var tagProperty = arrayProperty.GetArrayElementAtIndex(arrayProperty.arraySize - 1);
            tagProperty.FindPropertyRelative("name").stringValue = tag.Name;
            if (apply)
            {
                arrayProperty.serializedObject.ApplyModifiedProperties();
            }
        }

        internal static bool HasTag(SerializedProperty property, Tag tag)
        {
            var arrayProperty = property.FindPropertyRelative("array");
            for (var i = 0; i < arrayProperty.arraySize; i++)
            {
                var tagProperty = arrayProperty.GetArrayElementAtIndex(i);
                var tagName = tagProperty.FindPropertyRelative("name").stringValue;
                if (tag == tagName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
