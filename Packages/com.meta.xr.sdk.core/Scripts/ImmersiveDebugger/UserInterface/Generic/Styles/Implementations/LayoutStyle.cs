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

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class LayoutStyle : Style
    {
        public enum Layout
        {
            Fixed,
            Fill,
            FillHorizontal,
            FillVertical
        }

        public enum Direction
        {
            Left,
            Right,
            Down,
            Up
        }

        public Direction flexDirection;
        public Layout layout;
        public TextAnchor anchor;
        public TextAnchor pivot;
        public Vector2 size;
        public Vector2 margin;
        public bool useBottomRightMargin = false;
        public Vector2 bottomRightMargin;
        public float spacing;
        public bool masks;
        public bool adaptHeight;
        public bool autoFitChildren;
        public bool isOverlayCanvas;

        public float LeftMargin => margin.x;
        public float TopMargin => margin.y;
        public float RightMargin => useBottomRightMargin ? bottomRightMargin.x : margin.x;
        public float BottomMargin => useBottomRightMargin ? bottomRightMargin.y : margin.y;
        public Vector2 TopLeftMargin => margin;
        public Vector2 BottomRightMargin => useBottomRightMargin ? bottomRightMargin : margin;

        public bool SetHeight(float height)
        {
            if (!_instantiated || size.y == height) return false;

            size.y = height;
            return true;
        }

        public bool SetWidth(float width)
        {
            if (!_instantiated || size.x == width) return false;

            size.x = width;
            return true;
        }
    }
}

