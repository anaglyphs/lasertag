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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && UNITY_NEW_INPUT_SYSTEM_INSTALLED
using UnityEngine.InputSystem;
#endif

public class OVRHeadsetEmulator : MonoBehaviour
{
    public enum OpMode
    {
        Off,
        EditorOnly,
        AlwaysOn
    }

    public OpMode opMode = OpMode.EditorOnly;
    public bool resetHmdPoseOnRelease = true;
    public bool resetHmdPoseByMiddleMouseButton = true;

    public KeyCode[] activateKeys = new KeyCode[] { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.F1 };

    public KeyCode[] pitchKeys = new KeyCode[] { KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.F2 };

#if ENABLE_INPUT_SYSTEM && UNITY_NEW_INPUT_SYSTEM_INSTALLED
    private InputAction[] activeKeyActions;
    private InputAction[] pitchKeyActions;
    private InputAction middleMouseButtonAction;
    private InputAction mouseScrollAction;
    private InputAction mouseMoveAction;
#endif


    OVRManager manager;

    const float MOUSE_SCALE_X = -2.0f;
    const float MOUSE_SCALE_X_PITCH = -2.0f;
    const float MOUSE_SCALE_Y = 2.0f;
    const float MOUSE_SCALE_HEIGHT = 1.0f;
    const float MAX_ROLL = 85.0f;

    private bool lastFrameEmulationActivated = false;

    private Vector3 recordedHeadPoseRelativeOffsetTranslation;
    private Vector3 recordedHeadPoseRelativeOffsetRotation;

    private bool hasSentEvent = false;
    private bool emulatorHasInitialized = false;

    private CursorLockMode previousCursorLockMode = CursorLockMode.None;

    // Use this for initialization
    void Start()
    {
#if ENABLE_INPUT_SYSTEM && UNITY_NEW_INPUT_SYSTEM_INSTALLED
        activeKeyActions = new InputAction[activateKeys.Length];
        for (int i = 0; i < activateKeys.Length; i++) {
            activeKeyActions[i] = new InputAction(binding: "<Keyboard>/" + activateKeys[i].ToString());
            activeKeyActions[i].Enable();
        }

        pitchKeyActions = new InputAction[pitchKeys.Length];
        for (int i = 0; i < pitchKeys.Length; i++) {
            pitchKeyActions[i] = new InputAction(binding: "<Keyboard>/" + pitchKeys[i].ToString());
            pitchKeyActions[i].Enable();
        }

        middleMouseButtonAction = new InputAction(type: InputActionType.Button, binding: "<Mouse>/middleButton");
        mouseScrollAction = new InputAction(type: InputActionType.Value, binding: "<Mouse>/scroll/y");
        mouseMoveAction = new InputAction(type: InputActionType.Value, binding: "<Mouse>/delta");
        middleMouseButtonAction.Enable();
        mouseScrollAction.Enable();
        mouseMoveAction.Enable();
#endif
    }

    // Update is called once per frame
    void Update()
    {
        //todo: enable for Unity Input System
        if (!emulatorHasInitialized)
        {
            if (OVRManager.OVRManagerinitialized)
            {
                previousCursorLockMode = Cursor.lockState;
                manager = OVRManager.instance;
                recordedHeadPoseRelativeOffsetTranslation = manager.headPoseRelativeOffsetTranslation;
                recordedHeadPoseRelativeOffsetRotation = manager.headPoseRelativeOffsetRotation;
                emulatorHasInitialized = true;
                lastFrameEmulationActivated = false;
            }
            else
                return;
        }

        bool emulationActivated = IsEmulationActivated();
        if (emulationActivated)
        {
            if (!lastFrameEmulationActivated)
            {
                previousCursorLockMode = Cursor.lockState;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (!lastFrameEmulationActivated && resetHmdPoseOnRelease)
            {
                manager.headPoseRelativeOffsetTranslation = recordedHeadPoseRelativeOffsetTranslation;
                manager.headPoseRelativeOffsetRotation = recordedHeadPoseRelativeOffsetRotation;
            }

            bool middleMousePressed = false;
            float deltaMouseScrollWheel = 0f;
            float deltaX = 0f;
            float deltaY = 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
            middleMousePressed = Input.GetMouseButton(2);
            deltaMouseScrollWheel = Input.GetAxis("Mouse ScrollWheel");
            Vector2 mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            deltaX = mouseDelta.x;
            deltaY = mouseDelta.y;
#else
#if UNITY_NEW_INPUT_SYSTEM_INSTALLED
            middleMousePressed = middleMouseButtonAction.phase == InputActionPhase.Performed;
            deltaMouseScrollWheel = mouseScrollAction.ReadValue<float>();
            Vector2 mouseDelta = mouseMoveAction.ReadValue<Vector2>();
            deltaX = mouseDelta.x * 0.05f;
            deltaY = mouseDelta.y * 0.05f;
#endif
#endif

            if (resetHmdPoseByMiddleMouseButton && middleMousePressed)
            {
                manager.headPoseRelativeOffsetTranslation = Vector3.zero;
                manager.headPoseRelativeOffsetRotation = Vector3.zero;
            }
            else
            {
                Vector3 emulatedTranslation = manager.headPoseRelativeOffsetTranslation;
                float emulatedHeight = deltaMouseScrollWheel * MOUSE_SCALE_HEIGHT;
                emulatedTranslation.y += emulatedHeight;
                manager.headPoseRelativeOffsetTranslation = emulatedTranslation;

                Vector3 emulatedAngles = manager.headPoseRelativeOffsetRotation;
                float emulatedRoll = emulatedAngles.x;
                float emulatedYaw = emulatedAngles.y;
                float emulatedPitch = emulatedAngles.z;
                if (IsTweakingPitch())
                {
                    emulatedPitch += deltaX * MOUSE_SCALE_X_PITCH;
                }
                else
                {
                    emulatedRoll += deltaY * MOUSE_SCALE_Y;
                    emulatedYaw += deltaX * MOUSE_SCALE_X;
                }

                manager.headPoseRelativeOffsetRotation = new Vector3(emulatedRoll, emulatedYaw, emulatedPitch);
            }

            if (!hasSentEvent)
            {
                OVRPlugin.SendEvent("headset_emulator", "activated");
                hasSentEvent = true;
            }
        }
        else
        {
            if (lastFrameEmulationActivated)
            {
                Cursor.lockState = previousCursorLockMode;

                recordedHeadPoseRelativeOffsetTranslation = manager.headPoseRelativeOffsetTranslation;
                recordedHeadPoseRelativeOffsetRotation = manager.headPoseRelativeOffsetRotation;

                if (resetHmdPoseOnRelease)
                {
                    manager.headPoseRelativeOffsetTranslation = Vector3.zero;
                    manager.headPoseRelativeOffsetRotation = Vector3.zero;
                }
            }
        }

        lastFrameEmulationActivated = emulationActivated;
    }

    bool IsEmulationActivated()
    {
        if (opMode == OpMode.Off)
        {
            return false;
        }
        else if (opMode == OpMode.EditorOnly && !Application.isEditor)
        {
            return false;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        foreach (KeyCode key in activateKeys)
        {
            if (Input.GetKey(key))
                return true;
        }
#else
#if UNITY_NEW_INPUT_SYSTEM_INSTALLED
        foreach (var action in activeKeyActions)
        {
            if (action.phase == InputActionPhase.Started)
            {
                return true;
            }
        }
#endif
#endif
        return false;
    }

    bool IsTweakingPitch()
    {
        if (!IsEmulationActivated())
            return false;

#if ENABLE_LEGACY_INPUT_MANAGER
        foreach (KeyCode key in pitchKeys)
        {
            if (Input.GetKey(key))
                return true;
        }
#else
#if UNITY_NEW_INPUT_SYSTEM_INSTALLED
        foreach (var action in pitchKeyActions)
        {
            if (action.phase == InputActionPhase.Started)
            {
                return true;
            }
        }
#endif
#endif

        return false;
    }

    void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM && UNITY_NEW_INPUT_SYSTEM_INSTALLED
        if (activeKeyActions != null)
        {
            foreach (var action in activeKeyActions)
            {
                action.Disable();
            }
        }

        if (pitchKeyActions != null)
        {
            foreach (var action in pitchKeyActions)
            {
                action.Disable();
            }
        }

        middleMouseButtonAction?.Disable();
        mouseScrollAction?.Disable();
        mouseMoveAction?.Disable();
#endif
    }
}
