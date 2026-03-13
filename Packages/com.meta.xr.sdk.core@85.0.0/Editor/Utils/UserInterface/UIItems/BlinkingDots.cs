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

using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class BlinkingDots : IUserInterfaceItem
    {
        private const int DotCount = 3;
        private const float AnimationSpeed = 0.25f;
        private const float ActiveAlpha = 0.85f;
        private const float InactiveAlpha = 0.3f;
        private const float DotSize = 8f;
        private const float DotSpacing = 4f;

        private int _currentIndex = 0;
        private float _lastTimer = 0.0f;
        private Color _baseColor = Color.white;
        private GUIStyle _dotStyle;
        private Texture2D _circleTexture;

        public BlinkingDots() : this(Color.white)
        {
        }

        public BlinkingDots(Color baseColor)
        {
            _baseColor = baseColor;
            InitializeDotStyle();
            CreateCircleTexture();
        }

        private void InitializeDotStyle()
        {
            _dotStyle = new GUIStyle(GUI.skin.label)
            {
                fixedWidth = DotSize,
                fixedHeight = DotSize,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        private void CreateCircleTexture()
        {
            int size = (int)DotSize * 2;
            _circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _circleTexture.hideFlags = HideFlags.DontSave;

            float center = size / 2f;
            float radius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    if (distance <= radius)
                    {
                        float alpha = 1f - Mathf.Clamp01((distance - (radius - 1f)) / 1f);
                        _circleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        _circleTexture.SetPixel(x, y, Color.clear);
                    }
                }
            }

            _circleTexture.Apply();
        }

        public void Draw()
        {
            if (Hide)
            {
                return;
            }

            Update();

            GUILayout.BeginHorizontal();
            for (int i = 0; i < DotCount; i++)
            {
                DrawDot(i);
                if (i < DotCount - 1)
                {
                    GUILayout.Space(DotSpacing);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void Update()
        {
            var newTimer = Time.realtimeSinceStartup;
            var delta = newTimer - _lastTimer;
            if (delta > AnimationSpeed)
            {
                var numberOfDeltas = Mathf.Floor(delta / AnimationSpeed);
                _lastTimer += numberOfDeltas * AnimationSpeed;
                _currentIndex = (_currentIndex + 1) % DotCount;
            }
        }

        private void DrawDot(int index)
        {
            bool isActive = index == _currentIndex;
            float alpha = isActive ? ActiveAlpha : InactiveAlpha;
            Color dotColor = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);

            var rect = GUILayoutUtility.GetRect(DotSize, DotSize, _dotStyle);

            if (_circleTexture != null)
            {
                var previousColor = GUI.color;
                GUI.color = dotColor;
                GUI.DrawTexture(rect, _circleTexture);
                GUI.color = previousColor;
            }
        }

        public bool Hide { get; set; }
    }
}
