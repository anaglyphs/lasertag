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
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// A block for mapping controller buttons easily.
    /// </summary>
    public class ControllerButtonsMapper : MonoBehaviour
    {
        [Serializable]
        public struct ButtonClickAction
        {
            public enum ButtonClickMode
            {
                OnButtonUp,
                OnButtonDown,
                OnButton
            }

            public string Title;
            public OVRInput.Button Button;
            public ButtonClickMode ButtonMode;
            public UnityEvent Callback;
        }

        [SerializeField]
        private List<ButtonClickAction> _buttonClickActions;

        public List<ButtonClickAction> ButtonClickActions
        {
            get => _buttonClickActions;
            set => _buttonClickActions = value;
        }

        private void Update()
        {
            foreach (var buttonClickAction in ButtonClickActions)
            {
                ButtonClickAction.ButtonClickMode buttonMode = buttonClickAction.ButtonMode;
                OVRInput.Button button = buttonClickAction.Button;

                if ((buttonMode == ButtonClickAction.ButtonClickMode.OnButtonUp && OVRInput.GetUp(button)) ||
                    (buttonMode == ButtonClickAction.ButtonClickMode.OnButtonDown && OVRInput.GetDown(button)) ||
                    (buttonMode == ButtonClickAction.ButtonClickMode.OnButton && OVRInput.Get(button)))
                {
                    buttonClickAction.Callback?.Invoke();
                }
            }
        }
    }
}
