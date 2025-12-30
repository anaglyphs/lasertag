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
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    /// <summary>
    /// This is a <see cref="MonoBehaviour"/> used for managing the layout for all the UI elements of Immersive Debugger.
    /// It contains helper functions to calculate and update layout.
    /// For more info about Immersive Debugger, check out the [official doc](https://developer.oculus.com/documentation/unity/immersivedebugger-overview)
    /// </summary>
    public class Flex : Controller
    {
        private Vector2 _sizeDelta;

        internal Vector2 SizeDelta => _sizeDelta;
        internal Vector2 SizeDeltaWithMargin => _sizeDelta + LayoutStyle.TopLeftMargin + LayoutStyle.BottomRightMargin;

        internal ScrollViewport ScrollViewport { get; set; }
        private Vector2? _previousAnchoredPosition;

        private void UpdateAnchoredPosition(Controller controller, ref Vector2 offset, Vector2 direction)
        {
            var margin = controller.LayoutStyle.margin;
            var bottomMargin = controller.LayoutStyle.useBottomRightMargin ?
                margin :
                controller.LayoutStyle.bottomRightMargin;
            var anchoredPosition = new Vector2(margin.x, -margin.y);
            var size = controller.RectTransform.sizeDelta;
            controller.RectTransform.anchoredPosition = anchoredPosition + offset;

            offset += direction * (size + margin + bottomMargin);
            offset += direction * _layoutStyle.spacing;
        }

        private void UpdateChildrenWidth()
        {
            if (_children == null) return;
            if (!_layoutStyle.autoFitChildren || _layoutStyle.size.x <= 0) return;

            var flexibleSpaceRemaining = RectTransform.sizeDelta.x;
            var fixedSizedChildCount = 0;
            var ignoredChildCount = 0;

            foreach (var child in _children)
            {
                // Skip children that should be ignored in flex layout
                if (child.LayoutStyle.ignoreFlexLayout)
                {
                    ignoredChildCount++;
                    continue;
                }

                if (child.LayoutStyle.layout is LayoutStyle.Layout.Fixed)
                {
                    flexibleSpaceRemaining -= child.LayoutStyle.size.x + child.LayoutStyle.margin.x * 2 + _layoutStyle.spacing;
                    fixedSizedChildCount++;
                }
            }

            var remainingChild = _children.Count - fixedSizedChildCount - ignoredChildCount;
            if (remainingChild == 0) return;

            foreach (var child in _children)
            {
                // Skip children that should be ignored in flex layout
                if (child.LayoutStyle.ignoreFlexLayout)
                {
                    continue;
                }

                var estimatedCellWidth = Mathf.RoundToInt((flexibleSpaceRemaining / remainingChild)) - _layoutStyle.spacing;
                if (child.LayoutStyle.layout is not LayoutStyle.Layout.Fixed)
                {
                    child.SetWidth(estimatedCellWidth);
                }
            }
        }

        private void RefreshVisibilities(bool force = false)
        {
            if (ScrollViewport == null) return;
            if (_children == null) return;

            var newPosition = RectTransform.anchoredPosition;
            if (!force && newPosition == _previousAnchoredPosition) return;
            _previousAnchoredPosition = newPosition;

            var scroll = RectTransform.anchoredPosition;
            var viewportRect = new Rect(ScrollViewport.RectTransform.anchoredPosition, ScrollViewport.RectTransform.rect.size);

            var firstWasShown = false;
            var lastWasShown = false;

            foreach (var child in _children)
            {
                if (child.LayoutStyle.ignoreFlexLayout)
                {
                    // Items with ignoreFlexLayout are always shown and don't affect culling logic
                    child.Show();
                    continue;
                }

                if (!lastWasShown && IsVerticallyInViewport(child, viewportRect, scroll))
                {
                    child.Show();
                    firstWasShown = true;
                }
                else
                {
                    child.Hide();
                    if (firstWasShown)
                    {
                        lastWasShown = true;
                    }
                }
            }
        }

        private static bool IsVerticallyInViewport(Controller controller, Rect viewportRect, Vector2 scroll)
        {
            var position = -controller.RectTransform.anchoredPosition - scroll;
            var itemHeight = controller.RectTransform.sizeDelta.y;
            var itemBottom = position.y;
            var itemTop = position.y + itemHeight;

            var isVisible = false;

            // Check if item overlaps viewport vertically
            if (itemTop >= viewportRect.yMin && itemBottom < viewportRect.yMax)
            {
                // Item is fully within viewport
                isVisible = true;
            }
            else if (itemBottom < viewportRect.yMin && itemTop >= viewportRect.yMin)
            {
                // Item extends below viewport but starts within it
                isVisible = true;
            }
            else if (itemBottom < viewportRect.yMax && itemTop >= viewportRect.yMax)
            {
                // Item extends above viewport but ends within it
                isVisible = true;
            }
            else if (itemBottom >= viewportRect.yMin && itemTop < viewportRect.yMax)
            {
                // Item spans entire viewport
                isVisible = true;
            }

            return isVisible;
        }

        protected override void RefreshLayoutPreChildren()
        {
            base.RefreshLayoutPreChildren();

            UpdateChildrenWidth();
        }

        protected override void RefreshLayoutPostChildren()
        {
            if (!_hasRectTransform) return;

            // Not calling base purposefully

            if (_children != null)
            {
                var direction = _layoutStyle.flexDirection switch
                {
                    LayoutStyle.Direction.Left => Vector2.left,
                    LayoutStyle.Direction.Right => Vector2.right,
                    LayoutStyle.Direction.Down => Vector2.down,
                    LayoutStyle.Direction.Up => Vector2.up,
                    _ => throw new ArgumentOutOfRangeException()
                };

                // Apply top padding offset for adaptive height containers
                var topPaddingOffset = Vector2.zero;
                if (LayoutStyle.adaptHeight && LayoutStyle.adaptiveHeightPadding.x > 0)
                {
                    topPaddingOffset = direction switch
                    {
                        var d when d == Vector2.down => new Vector2(0, -LayoutStyle.adaptiveHeightPadding.x),
                        var d when d == Vector2.up => new Vector2(0, LayoutStyle.adaptiveHeightPadding.x),
                        _ => Vector2.zero
                    };
                }

                var offset = topPaddingOffset;
                foreach (var child in _children)
                {
                    // Skip children that should be ignored in flex layout
                    if (child.LayoutStyle.ignoreFlexLayout)
                    {
                        continue;
                    }

                    UpdateAnchoredPosition(child, ref offset, direction);
                }

                // Reset Visibility previous position to enforce refreshing visibilities
                _previousAnchoredPosition = null;

                _sizeDelta = new Vector2(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
            }

            if (LayoutStyle.adaptHeight)
            {
                var totalAdaptivePadding = LayoutStyle.adaptiveHeightPadding.x + LayoutStyle.adaptiveHeightPadding.y;
                RectTransform.sizeDelta = new Vector2(RectTransform.sizeDelta.x, Mathf.Abs(_sizeDelta.y) + totalAdaptivePadding);
            }
        }

        private void LateUpdate()
        {
            RefreshVisibilities();
        }

        internal void Forget(Controller controller)
        {
            Remove(controller, false);
            controller.Hide();
        }

        internal void Remember(Controller controller)
        {
            Append(controller);
            controller.Show();
        }

        internal void ForgetAll()
        {
            if (_children == null) return;

            foreach (var child in _children)
            {
                child.Hide();
            }
            Clear(false);
        }
    }
}

