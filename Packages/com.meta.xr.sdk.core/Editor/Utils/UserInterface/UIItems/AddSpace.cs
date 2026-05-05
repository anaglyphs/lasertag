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

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Add spaces between <see cref="IUserInterfaceItem"/>(s)
    /// </summary>
    internal class AddSpace : IUserInterfaceItem
    {
        public enum SpaceDirection
        {
            Vertical,
            Horizontal
        }

        private readonly SpaceDirection _direction;

        public bool Hide { get; set; }
        private readonly float _space;
        private readonly bool _flexibleSpace;

        /// <summary>
        /// Insert a flexible space between <see cref="IUserInterfaceItem"/>s.
        /// </summary>
        public AddSpace(bool flexibleSpace) => _flexibleSpace = flexibleSpace;

        /// <summary>
        /// Add fixed space(s) between <see cref="IUserInterfaceItem"/>s.
        /// </summary>
        public AddSpace(float space = 6f, SpaceDirection direction = SpaceDirection.Vertical) : this(false)
        {
            _space = space;
            _direction = direction;
        }

        public void Draw()
        {
            if (_flexibleSpace)
            {
                GUILayout.FlexibleSpace();
            }
            else
            {
                EditorGUILayout.Space(_space);
            }
        }

        public VisualElement Get()
        {
            return new VisualElement
            {
                style =
                {
                    height = _direction is SpaceDirection.Vertical ? _space : 0,
                    width = _direction is SpaceDirection.Horizontal ? _space : 0,
                    flexGrow = _flexibleSpace ? 1 : 0
                }
            };
        }
    }
}
