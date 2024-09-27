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
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class TextureContent
    {
        public class Category
        {
            private readonly string _relativePath;
            private string _fullPath;

            public bool TryGetFullPath(out string fullPath)
            {
                if (_fullPath == null)
                {
                    if (TryGetRootPath(out var rootPath))
                    {
                        _fullPath = Path.Combine(rootPath, _relativePath);
                    }
                }

                fullPath = _fullPath;
                return fullPath != null;
            }

            public Category(string path, bool isFullPath = false)
            {
                if (isFullPath)
                {
                    _fullPath = path;
                    return;
                }
                _relativePath = path;
            }
        }

        public static class Categories
        {
            public static readonly Category BuiltIn = new Category(null);
            public static readonly Category Generic = new Category("Icons");
        }

        public static TextureContent CreateContent(string name, TextureContent.Category category, string tooltip = null)
        {
            return new TextureContent(name, category, tooltip);
        }

        private static string _rootPath;

        private static bool TryGetRootPath(out string rootPath)
        {
            if (_rootPath == null)
            {
                var g = AssetDatabase.FindAssets($"t:Script {nameof(TextureContent)}");
                if (g.Length > 0)
                {
                    _rootPath = AssetDatabase.GUIDToAssetPath(g[0]);
                    _rootPath = Path.GetDirectoryName(_rootPath);
                    _rootPath = Path.GetDirectoryName(_rootPath);
                    _rootPath = Path.GetDirectoryName(_rootPath);
                }
            }

            rootPath = _rootPath;
            return rootPath != null;
        }

        public static bool BuildPath(string path, Category category, out string contentPath)
        {
            contentPath = null;
            if (category.TryGetFullPath(out var fullPath))
            {
                contentPath = Path.Combine(fullPath, path);
            }

            return contentPath != null;
        }

        public static implicit operator GUIContent(TextureContent ovrTextureContent)
        {
            return ovrTextureContent.GUIContent;
        }

        private GUIContent _content;
        private readonly string _name;
        private readonly Category _category;
        private readonly bool _builtIn;
        private string _tooltip;

        public string Name => _name;

        public string Tooltip
        {
            set
            {
                _tooltip = value;
                _content.tooltip = value;
            }
            get => _tooltip;
        }

        private TextureContent(string name, Category category, string tooltip = null)
        {
            _name = name;
            _tooltip = tooltip;
            _category = category;
            _content = new GUIContent
            {
                image = null,
                tooltip = _tooltip
            };
        }

        public GUIContent GUIContent
        {
            get
            {
                if (_content.image == null)
                {
                    LoadContent();
                }

                return _content;
            }
        }

        public bool Valid => GUIContent.image != null;
        public Texture Image => GUIContent.image;

        private void LoadContent()
        {
            if (!Utils.IsMainEditor())
            {
                return;
            }

            if (_category == Categories.BuiltIn)
            {
                _content = EditorGUIUtility.TrIconContent(_name, _tooltip);
            }
            else if (BuildPath(_name, _category, out var fullPath))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
                if (texture)
                {
                    _content.image = texture;
                }
            }
        }
    }
}
