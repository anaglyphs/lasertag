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
using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    internal static class Extensions
    {
        public static void SetSizeOptimized(this RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax, Vector2 fixedDimensions, bool setAnchoredPosition)
        {
            // This function is only the unwrapping and simple optimisation (as in, removing duplicated computing, and reusing proxy variables)
            // of .minOffset, followed by .maxOffset, followed by .sizeDelta from the Unity Source code
            var sizeDelta = rectTransform.sizeDelta;
            var anchoredPosition = rectTransform.anchoredPosition;
            var pivot = rectTransform.pivot;
            var vector = offsetMin - (anchoredPosition - new Vector2(sizeDelta.x * pivot.x, sizeDelta.y * pivot.y));
            sizeDelta.x = vector.x + sizeDelta.x;
            sizeDelta.y = vector.y + sizeDelta.y;
            anchoredPosition = new Vector2(offsetMin.x + sizeDelta.x * pivot.x, offsetMin.y + sizeDelta.y * pivot.y);

            vector = offsetMax - (anchoredPosition + new Vector2(sizeDelta.x * (1 - pivot.x), sizeDelta.y * (1 - pivot.y)));
            sizeDelta.x = vector.x + sizeDelta.x;
            sizeDelta.y = vector.y + sizeDelta.y;
            anchoredPosition = new Vector2(offsetMax.x - sizeDelta.x * (1 - pivot.x), offsetMax.y - sizeDelta.y * (1 - pivot.y));

            sizeDelta.x = fixedDimensions.x != 0.0f ? fixedDimensions.x : sizeDelta.x;
            sizeDelta.y = fixedDimensions.y != 0.0f ? fixedDimensions.y : sizeDelta.y;
            rectTransform.sizeDelta = sizeDelta;
            if (setAnchoredPosition)
            {
                rectTransform.anchoredPosition = anchoredPosition;
            }
        }
    }

    public class Controller : MonoBehaviour
    {
        private bool _visibility = true;
        private bool _refreshLayoutRequested;
        protected bool _hasRectTransform;
        private bool _layoutStyleHasChanged;


        [SerializeField]
        protected LayoutStyle _layoutStyle;
        protected List<Controller> _children;

        public Controller Owner { get; set; }
        public Transform Transform { get; protected set; }
        public RectTransform RectTransform { get; protected set; }
        protected GameObject GameObject { get; set; }
        public List<Controller> Children => _children;

        private RectMask2D _mask = null;

        public LayoutStyle LayoutStyle
        {
            get => _layoutStyle;
            set
            {
                if (value == null) return;
                if (_layoutStyle == value) return;

                _layoutStyle = value;
                _layoutStyleHasChanged = true;
                RefreshLayout();

                if (_layoutStyle?.isOverlayCanvas ?? false)
                    UpdateRefreshLayout(false);
            }
        }

        public event Action<Controller> OnVisibilityChangedEvent;

        protected virtual void Setup(Controller owner)
        {
            Owner = owner;
            GameObject = gameObject;
            GameObject.layer = RuntimeSettings.Instance.PanelLayer;
            RectTransform = GameObject.AddComponent<RectTransform>() ?? GameObject.GetComponent<RectTransform>();
            Transform = RectTransform ? RectTransform : GameObject.transform;
            if (Owner != this && Owner != null)
            {
                Transform.SetParent(Owner.Transform, false);
            }

            LayoutStyle = Style.Default<LayoutStyle>();

            _hasRectTransform = RectTransform != null;
        }

        public T Append<T>(string childName)
            where T : Controller, new()
        {
            var childController = SetupChildController<T>(childName);
            _children ??= new List<Controller>();
            _children.Add(childController);
            return childController;
        }

        public T Prepend<T>(string childName)
            where T : Controller, new()
        {
            var childController = SetupChildController<T>(childName);
            _children ??= new List<Controller>();
            _children.Insert(0, childController);
            return childController;
        }

        private T SetupChildController<T>(string childName)
            where T : Controller, new()
        {
            var childObject = new GameObject(childName);
            var childController = childObject.AddComponent<T>();
            childController.Setup(this);
            return childController;
        }

        public void Append(Controller controller)
        {
            _children?.Add(controller);
            controller.RefreshLayout();
        }

        public void Remove(Controller controller, bool destroy)
        {
            _children?.Remove(controller);
            if (destroy)
            {
                if (Application.isPlaying)
                {
                    Destroy(controller.gameObject);
                }
                else
                {
                    DestroyImmediate(controller.gameObject);
                }
            }
            RefreshLayout();
        }

        public void Clear(bool destroy)
        {
            while (_children.Count > 0)
            {
                Remove(_children[^1], destroy);
            }
        }

        public bool Visibility
        {
            get => _visibility;
            private set
            {
                if (_visibility != value)
                {
                    _visibility = value;
                    OnVisibilityChanged();
                }
            }
        }

        public void Hide()
        {
            Visibility = false;
        }

        public void Show()
        {
            Visibility = true;
        }

        public void ToggleVisibility()
        {
            Visibility = !GameObject.activeSelf;
        }

        public void OnVisibilityChanged()
        {
            GameObject.SetActive(Visibility);
            OnVisibilityChangedEvent?.Invoke(this);
        }

        private static Vector2 GetVec2FromLayout(TextAnchor anchor)
        {
            var index = (int)anchor;
            return new Vector2((index % 3) * 0.5f, 1.0f - (int)(index / 3) * 0.5f);
        }

        protected void UpdateRefreshLayout(bool force)
        {
            if (!force && !_refreshLayoutRequested) return;

            _refreshLayoutRequested = false;


            RefreshLayoutPreChildren();

            // Process Refresh of layout of all children
            if (_children != null)
            {
                // Force refresh if all children are needed for computing height or width
                var forceChildrenRefresh = _layoutStyle.adaptHeight || _layoutStyle.autoFitChildren;
                foreach (var child in _children)
                {
                    child.UpdateRefreshLayout(forceChildrenRefresh);
                }
            }

            RefreshLayoutPostChildren();
        }

        public void RefreshLayout()
        {
            // Request refresh flag
            _refreshLayoutRequested = true;

            // Propagate up
            Owner?.RefreshLayout();
        }

        protected virtual void RefreshLayoutPreChildren()
        {
            if (!_hasRectTransform) return;

            // Calculate Anchor and Pivot position
            if (_layoutStyleHasChanged)
            {
                _layoutStyleHasChanged = false;

                RectTransform.pivot = GetVec2FromLayout(_layoutStyle.pivot);
                RectTransform.anchorMin = GetVec2FromLayout(_layoutStyle.anchor);
                RectTransform.anchorMax = GetVec2FromLayout(_layoutStyle.anchor);

                switch (_layoutStyle.layout)
                {
                    case LayoutStyle.Layout.Fixed:
                        break;

                    case LayoutStyle.Layout.Fill:
                        RectTransform.anchorMin = new Vector2(0.0f, 0.0f);
                        RectTransform.anchorMax = new Vector2(1.0f, 1.0f);
                        break;
                    case LayoutStyle.Layout.FillHorizontal:
                        RectTransform.anchorMin = new Vector2(0.0f, RectTransform.anchorMin.y);
                        RectTransform.anchorMax = new Vector2(1.0f, RectTransform.anchorMax.y);
                        break;
                    case LayoutStyle.Layout.FillVertical:
                        RectTransform.anchorMin = new Vector2(RectTransform.anchorMin.x, 0.0f);
                        RectTransform.anchorMax = new Vector2(RectTransform.anchorMax.x, 1.0f);
                        break;
                }

                // Update Mask
                if (_layoutStyle.masks)
                {
                    _mask ??= GameObject.AddComponent<RectMask2D>();
                    _mask.enabled = true;
                }
                else if (_mask != null)
                {
                    _mask.enabled = false;
                }
            }

            var offsetMin = new Vector2(_layoutStyle.LeftMargin, _layoutStyle.BottomMargin);
            var offsetMax = new Vector2(-_layoutStyle.RightMargin, -_layoutStyle.TopMargin);
            RectTransform.SetSizeOptimized(offsetMin, offsetMax, _layoutStyle.size, !_layoutStyle.isOverlayCanvas);
        }

        protected virtual void RefreshLayoutPostChildren()
        {
            if (!_hasRectTransform) return;

            if (LayoutStyle.adaptHeight)
            {
                var maxOffset = 0.0f;

                if (_children != null)
                {
                    foreach (var child in _children)
                    {
                        if (child is Flex flex)
                        {
                            maxOffset = Mathf.Max(maxOffset, flex.SizeDeltaWithMargin.y);
                        }
                    }
                }

                RectTransform.sizeDelta = new Vector2(RectTransform.sizeDelta.x, maxOffset);
            }
        }

        public void OnDestroy()
        {
            if (Owner != null)
            {
                Owner.Remove(this, false);
            }
        }

        public void SetHeight(float height)
        {
            if (!_layoutStyle.SetHeight(height)) return;
            RefreshLayout();
        }

        public void SetWidth(float width)
        {
            if (!_layoutStyle.SetWidth(width)) return;
            RefreshLayout();
        }
    }
}

