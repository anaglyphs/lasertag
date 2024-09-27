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


using Meta.XR.ImmersiveDebugger.Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class Slider : Button, IDragHandler, IInitializePotentialDragHandler
    {
        private Background _emptyBackground;
        private Background _fillBackground;
        private Icon _pill;

        public Tweak Tweak { get; set; }

        private ImageStyle _emptyBackgroundStyle;
        public ImageStyle EmptyBackgroundStyle
        {
            set
            {
                _emptyBackgroundStyle = value;
                _emptyBackground.Sprite = value.sprite;
                _emptyBackground.Color = value.color;
                _emptyBackground.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        private ImageStyle _fillBackgroundStyle;
        public ImageStyle FillBackgroundStyle
        {
            set
            {
                _fillBackgroundStyle = value;
                _fillBackground.Sprite = value.sprite;
                _fillBackground.Color = value.color;
                _fillBackground.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            var raycastBackground = Append<Background>("raycast_background");
            raycastBackground.LayoutStyle = Style.Load<LayoutStyle>("Fill");
            raycastBackground.Color = new Color(0, 0, 0, 0);
            raycastBackground.Sprite = null;
            _emptyBackground = Append<Background>("empty_background");
            _emptyBackground.LayoutStyle = Style.Load<LayoutStyle>("SliderBackground");
            _fillBackground = Append<Background>("fill_background");
            _fillBackground.LayoutStyle = Style.Load<LayoutStyle>("SliderFill");
            _pill = Append<Icon>("pill");
            _pill.LayoutStyle = Style.Load<LayoutStyle>("SliderPill");
            _pill.Texture = Resources.Load<Texture2D>("Textures/icon_background_02");
            _pill.Color = Color.white;
        }

        private void UpdatePillPosition()
        {
            if (Tweak == null) return;

            var width = RectTransform.rect.width;
            var idealPosition = Tweak.Tween * width;
            _pill.RectTransform.anchoredPosition = new Vector2(idealPosition, 0.0f);
            _fillBackground.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, idealPosition);
        }

        public void Update()
        {
            UpdatePillPosition();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!MayDrag(eventData)) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(RectTransform, eventData.position, eventData.enterEventCamera, out var position)) return;

            var rect = RectTransform.rect;
            Tweak.Tween = Mathf.InverseLerp(rect.min.x, rect.max.x, position.x);
        }

        private bool MayDrag(PointerEventData eventData)
        {
            return Tweak != null && eventData.button == PointerEventData.InputButton.Left;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }
    }
}

