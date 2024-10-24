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
    public class Interface : Controller
    {
        private ProxyInputModule _proxyInputModule;
        private ProxyCameraRig _proxyCameraRig;
        public Cursor Cursor { get; private set; }

        public Camera Camera => _proxyCameraRig.Camera;

        protected virtual bool FollowOverride { get; set; }
        protected virtual bool RotateOverride { get; set; }

        public virtual void Awake()
        {
            Setup(null);

            var cursorObject = new GameObject("cursor");
            cursorObject.transform.SetParent(Transform);
            Cursor = cursorObject.AddComponent<Cursor>();

            _proxyCameraRig = new ProxyCameraRig();
            _proxyInputModule = new ProxyInputModule(GameObject, Cursor);
        }

        private void UpdateTransform()
        {
            if (FollowOverride)
            {
                Transform.position = _proxyCameraRig.CameraTransform.position;
            }

            if (RotateOverride)
            {
                var euler = _proxyCameraRig.CameraTransform.eulerAngles;
                // Only rotating around up axis (means the dashboard cannot roll, and stays parallel to the ground)
                euler.x = 0.0f;
                euler.z = 0.0f;
                Transform.rotation = Quaternion.Euler(euler);
            }
        }

        private void UpdateController()
        {
            _proxyInputModule.InputModule.rayTransform = OVRInput.GetActiveController() switch
            {
                OVRInput.Controller.LTouch => _proxyCameraRig.LeftControllerTransform,
                OVRInput.Controller.RTouch => _proxyCameraRig.RightControllerTransform,
                _ => _proxyCameraRig.RightControllerTransform
            };
        }

        private void UpdateCulling()
        {
            var runtimeSettings = RuntimeSettings.Instance;
            if (!runtimeSettings.AutomaticLayerCullingUpdate) return;

            var currentCullingMask = Camera.cullingMask;
            var expectedCullingMask = SetBits(currentCullingMask, runtimeSettings.PanelLayer,
                runtimeSettings.MeshRendererLayer,
#if !UNITY_EDITOR
                false
#else
                true
#endif
            );

            if (expectedCullingMask != currentCullingMask)
            {
                Camera.cullingMask = expectedCullingMask;
            }
        }

        private static int SetBits(int cullingMask, int bitPosition1, int bitPosition2, bool state)
        {
            if (state)
            {
                // Set bits to true using OR
                cullingMask |= (1 << bitPosition1);
                cullingMask |= (1 << bitPosition2);
            }
            else
            {
                // Set bits to false using AND with NOT
                cullingMask &= ~(1 << bitPosition1);
                cullingMask &= ~(1 << bitPosition2);
            }

            return cullingMask;
        }

        public virtual void LateUpdate()
        {
            UpdateRefreshLayout(false);

            if (!_proxyCameraRig.Refresh()) return;
            UpdateTransform();
            UpdateCulling();

            if (!_proxyInputModule.Refresh()) return;
            UpdateController();
        }

        protected override void RefreshLayoutPreChildren() { }

    }
}

