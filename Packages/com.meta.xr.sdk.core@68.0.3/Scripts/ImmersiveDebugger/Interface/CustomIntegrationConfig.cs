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

namespace Meta.XR.ImmersiveDebugger
{
    /// <summary>
    /// Subscribe to the delegate events to register custom config of the integration with Immersive Debugger,
    /// all should be only subscribed once for each scene.
    /// For example implementation, check out ExampleCustomIntegrationConfig.cs
    /// </summary>
    public static class CustomIntegrationConfig
    {
        public delegate Camera GetCameraDelegate();
        public delegate Transform GetLeftControllerTransformDelegate();
        public delegate Transform GetRightControllerTransformDelegate();

        // Get Camera of the current scene, could be null. Used for show panel in relation to
        // the camera's pose in runtime if you're not using OVRCameraRig.
        public static event GetCameraDelegate GetCameraHandler;
        // Get Left controller, used to calculate raycasting to the panel in runtime
        public static event GetLeftControllerTransformDelegate GetLeftControllerTransformHandler;
        // Get Right controller, used to calculate raycasting to the panel in runtime
        public static event GetRightControllerTransformDelegate GetRightControllerTransformHandler;

        public static void SetupAllConfig(ICustomIntegrationConfig customConfig)
        {
            GetCameraHandler += customConfig.GetCamera;
            GetLeftControllerTransformHandler += customConfig.GetLeftControllerTransform;
            GetRightControllerTransformHandler += customConfig.GetRightControllerTransform;
        }

        public static void ClearAllConfig(ICustomIntegrationConfig customConfig)
        {
            GetCameraHandler -= customConfig.GetCamera;
            GetLeftControllerTransformHandler -= customConfig.GetLeftControllerTransform;
            GetRightControllerTransformHandler -= customConfig.GetRightControllerTransform;
        }

        public static Camera GetCamera()
        {
            return GetCameraHandler?.Invoke();
        }

        public static Transform GetLeftControllerTransform()
        {
            return GetLeftControllerTransformHandler?.Invoke();
        }

        public static Transform GetRightControllerTransform()
        {
            return GetRightControllerTransformHandler?.Invoke();
        }
    }

    public interface ICustomIntegrationConfig
    {
        public Camera GetCamera();
        public Transform GetLeftControllerTransform();
        public Transform GetRightControllerTransform();
    }

    public class CustomIntegrationConfigBase : MonoBehaviour, ICustomIntegrationConfig
    {
        private void Awake()
        {
            CustomIntegrationConfig.SetupAllConfig(this);
        }

        private void OnDestroy()
        {
            CustomIntegrationConfig.ClearAllConfig(this);
        }

        public virtual Camera GetCamera()
        {
            return null;
        }

        public virtual Transform GetLeftControllerTransform()
        {
            return null;
        }

        public virtual Transform GetRightControllerTransform()
        {
            return null;
        }
    }
}
