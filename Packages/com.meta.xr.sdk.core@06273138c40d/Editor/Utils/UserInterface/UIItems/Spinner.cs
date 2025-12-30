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

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class Spinner : IUserInterfaceItem
    {
        private readonly GUIStyle _style;
        private AnimatedContent _content;

        private const string PackageName = "com.meta.xr.sdk.core";

        private string DirPath => Utils.IsInsidePackageDistribution()
            ? $"Packages/{PackageName}/Editor/Textures/Spinner"
            : "Assets/Oculus/VR/Editor/Textures/Spinner";

        public Spinner(int spinnerSize = 20)
        {
            _style = new GUIStyle(Styles.GUIStyles.Spinner)
            {
                fixedWidth = spinnerSize,
                fixedHeight = spinnerSize
            };
            LoadSpinner();
        }

        public void Draw()
        {
            if (_content == null)
            {
                return;
            }
            using var color =
                new Utils.ColorScope(Utils.ColorScope.Scope.All, Utils.HexToColorWithAlpha("#FFFFFF", 0.5f));
            EditorGUILayout.LabelField(new GUIContent(_content.CurrentFrame), _style);
            _content.Update();
        }

        private void LoadSpinner()
        {
            const string assetName = "SpinnerContent.asset";
            var assetPath = Path.Combine(DirPath, assetName);
            if (!AssetDatabase.IsValidFolder(DirPath))
            {
                return;
            }

            _content = AssetDatabase.LoadAssetAtPath<AnimatedContent>(assetPath);
        }

        public bool Hide { get; set; }
    }
}
