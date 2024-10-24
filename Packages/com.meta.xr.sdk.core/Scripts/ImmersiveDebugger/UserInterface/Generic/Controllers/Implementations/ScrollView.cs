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
using UnityEngine.UI;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class ScrollView : InteractableController
    {
        private ScrollRect _scrollRect;
        private ScrollViewport _viewport;
        private Mask _mask;

        public ScrollRect ScrollRect => _scrollRect;
        public Flex Flex => _viewport.Flex;

        private float _previousProgress;

        public float Progress
        {
            get => _scrollRect.verticalNormalizedPosition;
            set => _scrollRect.verticalNormalizedPosition = value;
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            _scrollRect = GameObject.AddComponent<ScrollRect>();

            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.inertia = true;

            _viewport = Append<ScrollViewport>("viewport");
            _viewport.LayoutStyle = Style.Load<LayoutStyle>("Fill");

            _scrollRect.content = _viewport.Flex.RectTransform;
        }

        protected override void RefreshLayoutPreChildren()
        {
            _previousProgress = Progress;

            base.RefreshLayoutPreChildren();
        }

        protected override void RefreshLayoutPostChildren()
        {
            Progress = _previousProgress;

            base.RefreshLayoutPostChildren();
        }
    }
}

