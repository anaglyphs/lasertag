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

namespace Meta.XR.ImmersiveDebugger.UsageExample
{
    /// <summary>
    /// Example demonstrating how to use the RuntimeAPIs to dynamically add inspectors
    /// for components at runtime without using DebugMember attributes.
    /// </summary>
    internal class RuntimeAPIExample : MonoBehaviour
    {
        [Header("Runtime API Configuration")]
        [SerializeField] private string targetGameObjectName = "Cube";
        [SerializeField] private string targetComponentName = "Transform";
        [SerializeField] private string inspectorCategory = "Runtime Inspectors";
        [SerializeField] private string membersToInspect = "position,rotation,localScale";

        [Header("Example Component to Inspect")]
        public float exampleFloat = 42.0f;
        public bool exampleBool = true;
        public Vector3 exampleVector = Vector3.one;

        private void Start()
        {
            // Wait some time to ensure DebugManager is initialized
            Invoke(nameof(AddRuntimeInspectors), 1.0f);
        }

        private void AddRuntimeInspectors()
        {
            // Example 1: Add inspector for Transform component with specific members
            var result1 = RuntimeAPIs.AddInspector(
                inspectorCategory,
                targetGameObjectName,
                targetComponentName,
                membersToInspect
            );

            if (result1.IsSuccess)
            {
                Debug.Log($"[RuntimeAPIExample] {result1.Message}");
                Debug.Log($"[RuntimeAPIExample] Details: {result1.Context}");
            }
            else
            {
                Debug.LogWarning($"[RuntimeAPIExample] Failed to add inspector: {result1.Message}");
                Debug.LogWarning($"[RuntimeAPIExample] Context: {result1.Context}");
            }

            // Example 2: Add inspector for this component with all public members
            var result2 = RuntimeAPIs.AddInspector(
                "Example Components",
                gameObject.name,
                "RuntimeAPIExample"
            );

            if (result2.IsSuccess)
            {
                Debug.Log($"[RuntimeAPIExample] {result2.Message}");
                Debug.Log($"[RuntimeAPIExample] Details: {result2.Context}");
            }
            else
            {
                Debug.LogWarning($"[RuntimeAPIExample] Failed to add inspector: {result2.Message}");
                Debug.LogWarning($"[RuntimeAPIExample] Context: {result2.Context}");
            }

            // Example 3: Add inspector for a specific component with selected members
            var result3 = RuntimeAPIs.AddInspector(
                "Camera Settings",
                "Main Camera",
                "Camera",
                "fieldOfView,nearClipPlane,farClipPlane"
            );

            if (result3.IsSuccess)
            {
                Debug.Log($"[RuntimeAPIExample] {result3.Message}");
                Debug.Log($"[RuntimeAPIExample] Details: {result3.Context}");
            }
            else
            {
                Debug.LogWarning($"[RuntimeAPIExample] Failed to add inspector: {result3.Message}");
                Debug.LogWarning($"[RuntimeAPIExample] Context: {result3.Context}");
                Debug.LogWarning("[RuntimeAPIExample] Note: This is expected if no Main Camera exists in the scene");
            }

            // Example 4: Demonstrate error handling with invalid parameters
            var result4 = RuntimeAPIs.AddInspector(
                "Invalid Test",
                "", // Empty GameObject name should trigger Failure_InvalidParameter
                "Transform"
            );

            Debug.Log($"[RuntimeAPIExample] Invalid parameter test - Status: {result4.Status}");
            Debug.Log($"[RuntimeAPIExample] Error message: {result4.Message}");
            Debug.Log($"[RuntimeAPIExample] Context: {result4.Context}");
        }

        /// <summary>
        /// Example method that can be called from the inspector
        /// </summary>
        public void ExampleMethod()
        {
            Debug.Log("[RuntimeAPIExample] ExampleMethod was called from the inspector!");
            exampleFloat = Random.Range(0f, 100f);
            exampleBool = !exampleBool;
            exampleVector = Random.insideUnitSphere * 10f;
        }

        /// <summary>
        /// Another example method with different behavior
        /// </summary>
        public void ResetValues()
        {
            exampleFloat = 42.0f;
            exampleBool = true;
            exampleVector = Vector3.one;
            Debug.Log("[RuntimeAPIExample] Values have been reset to defaults");
        }
    }
}
