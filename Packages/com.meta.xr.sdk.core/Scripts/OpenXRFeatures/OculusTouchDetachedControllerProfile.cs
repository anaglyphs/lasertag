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

using System.Collections.Generic;
using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#if USING_XR_SDK_OPENXR
using UnityEditor.XR.OpenXR.Features;
#endif
#endif

#if USING_XR_SDK_OPENXR
#if USE_INPUT_SYSTEM_POSE_CONTROL
using PoseControl = UnityEngine.InputSystem.XR.PoseControl;
#else
using PoseControl = UnityEngine.XR.OpenXR.Input.PoseControl;
#endif

using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.OpenXR.Input;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR;
using UnityEngine.InputSystem;

#if USE_STICK_CONTROL_THUMBSTICKS
using ThumbstickControl = UnityEngine.InputSystem.Controls.StickControl; // If replaced, make sure the control extends Vector2Control
#else
using ThumbstickControl = UnityEngine.InputSystem.Controls.Vector2Control;
#endif
#endif

namespace Meta.XR
{
#if USING_XR_SDK_OPENXR
#if UNITY_EDITOR
    [MetaOpenXRFeature(
        featureId: featureId,
        uiName: "Detached Oculus Touch Controller Profile",
        desc: "Allows for mapping input to the Oculus Touch Detached Controller interaction profile.",
        version: "0.0.1",
        targetApiVersion: "1.1.45",
        category: FeatureCategory.Interaction,
        extensions: new[] { "XR_META_detached_controllers" })]
#endif
    public class DetachedOculusTouchControllerProfile : OpenXRInteractionFeature
    {
        public const string featureId = "com.meta.openxr.feature.input.oculustouch.detached";

        [Preserve, InputControlLayout(displayName = "Detached Oculus Touch Controller (OpenXR)", commonUsages = new[] { "LeftHand", "RightHand" })]
        public class DetachedOculusTouchController : XRControllerWithRumble
        {
            [Preserve, InputControl(aliases = new[] { "Primary2DAxis", "Joystick" }, usage = "Primary2DAxis")]
            public ThumbstickControl thumbstick { get; private set; }

            [Preserve, InputControl(aliases = new[] { "GripAxis", "squeeze" }, usage = "Grip")]
            public AxisControl grip { get; private set; }

            [Preserve, InputControl(aliases = new[] { "GripButton", "squeezeClicked" }, usage = "GripButton")]
            public ButtonControl gripPressed { get; private set; }

            [Preserve, InputControl(aliases = new[] { "Primary", "menuButton", "systemButton" }, usage = "MenuButton")]
            public ButtonControl menu { get; private set; }

            [Preserve, InputControl(aliases = new[] { "A", "X", "buttonA", "buttonX" }, usage = "PrimaryButton")]
            public ButtonControl primaryButton { get; private set; }

            [Preserve, InputControl(aliases = new[] { "ATouched", "XTouched", "ATouch", "XTouch", "buttonATouched", "buttonXTouched" }, usage = "PrimaryTouch")]
            public ButtonControl primaryTouched { get; private set; }

            [Preserve, InputControl(aliases = new[] { "B", "Y", "buttonB", "buttonY" }, usage = "SecondaryButton")]
            public ButtonControl secondaryButton { get; private set; }

            [Preserve, InputControl(aliases = new[] { "BTouched", "YTouched", "BTouch", "YTouch", "buttonBTouched", "buttonYTouched" }, usage = "SecondaryTouch")]
            public ButtonControl secondaryTouched { get; private set; }

            [Preserve, InputControl(usage = "Trigger")]
            public AxisControl trigger { get; private set; }

            [Preserve, InputControl(aliases = new[] { "indexButton", "indexTouched", "triggerbutton" }, usage = "TriggerButton")]
            public ButtonControl triggerPressed { get; private set; }

            [Preserve, InputControl(aliases = new[] { "indexTouch", "indexNearTouched" }, usage = "TriggerTouch")]
            public ButtonControl triggerTouched { get; private set; }

            [Preserve, InputControl(aliases = new[] { "JoystickOrPadPressed", "thumbstickClick", "joystickClicked" }, usage = "Primary2DAxisClick")]
            public ButtonControl thumbstickClicked { get; private set; }

            [Preserve, InputControl(aliases = new[] { "JoystickOrPadTouched", "thumbstickTouch", "joystickTouched" }, usage = "Primary2DAxisTouch")]
            public ButtonControl thumbstickTouched { get; private set; }

            [Preserve, InputControl(usage = "ThumbrestTouch")]
            public ButtonControl thumbrestTouched { get; private set; }

            [Preserve, InputControl(offset = 0, aliases = new[] { "device", "gripPose" }, usage = "Device")]
            public PoseControl devicePose { get; private set; }

            [Preserve, InputControl(offset = 0, alias = "aimPose", usage = "Pointer")]
            public PoseControl pointer { get; private set; }

            [Preserve, InputControl(offset = 28, usage = "IsTracked")]
            new public ButtonControl isTracked { get; private set; }

            [Preserve, InputControl(offset = 32, usage = "TrackingState")]
            new public IntegerControl trackingState { get; private set; }

            [Preserve, InputControl(offset = 36, noisy = true, alias = "gripPosition")]
            new public Vector3Control devicePosition { get; private set; }

            [Preserve, InputControl(offset = 48, noisy = true, alias = "gripOrientation")]
            new public QuaternionControl deviceRotation { get; private set; }

            [Preserve, InputControl(offset = 96)]
            public Vector3Control pointerPosition { get; private set; }

            [Preserve, InputControl(offset = 108, alias = "pointerOrientation")]
            public QuaternionControl pointerRotation { get; private set; }

            [Preserve, InputControl(usage = "Haptic")]
            public HapticControl haptic { get; private set; }

            protected override void FinishSetup()
            {
                base.FinishSetup();
                thumbstick = GetChildControl<StickControl>("thumbstick");
                trigger = GetChildControl<AxisControl>("trigger");
                triggerPressed = GetChildControl<ButtonControl>("triggerPressed");
                triggerTouched = GetChildControl<ButtonControl>("triggerTouched");
                grip = GetChildControl<AxisControl>("grip");
                gripPressed = GetChildControl<ButtonControl>("gripPressed");
                menu = GetChildControl<ButtonControl>("menu");
                primaryButton = GetChildControl<ButtonControl>("primaryButton");
                primaryTouched = GetChildControl<ButtonControl>("primaryTouched");
                secondaryButton = GetChildControl<ButtonControl>("secondaryButton");
                secondaryTouched = GetChildControl<ButtonControl>("secondaryTouched");
                thumbstickClicked = GetChildControl<ButtonControl>("thumbstickClicked");
                thumbstickTouched = GetChildControl<ButtonControl>("thumbstickTouched");
                thumbrestTouched = GetChildControl<ButtonControl>("thumbrestTouched");

                devicePose = GetChildControl<PoseControl>("devicePose");
                pointer = GetChildControl<PoseControl>("pointer");

                isTracked = GetChildControl<ButtonControl>("isTracked");
                trackingState = GetChildControl<IntegerControl>("trackingState");
                devicePosition = GetChildControl<Vector3Control>("devicePosition");
                deviceRotation = GetChildControl<QuaternionControl>("deviceRotation");
                pointerPosition = GetChildControl<Vector3Control>("pointerPosition");
                pointerRotation = GetChildControl<QuaternionControl>("pointerRotation");

                haptic = GetChildControl<HapticControl>("haptic");
            }
        }

        public const string profile = "/interaction_profiles/oculus/touch_controller";

        // Available Bindings
        // Left Hand Only
        public const string buttonX = "/input/x/click";
        public const string buttonXTouch = "/input/x/touch";
        public const string buttonY = "/input/y/click";
        public const string buttonYTouch = "/input/y/touch";
        public const string menu = "/input/menu/click";

        // Right Hand Only
        public const string buttonA = "/input/a/click";
        public const string buttonATouch = "/input/a/touch";
        public const string buttonB = "/input/b/click";
        public const string buttonBTouch = "/input/b/touch";
        public const string system = "/input/system/click";

        // Both Hands
        public const string squeeze = "/input/squeeze/value";
        public const string trigger = "/input/trigger/value";
        public const string triggerTouch = "/input/trigger/touch";
        public const string thumbstick = "/input/thumbstick";
        public const string thumbstickClick = "/input/thumbstick/click";
        public const string thumbstickTouch = "/input/thumbstick/touch";
        public const string thumbrest = "/input/thumbrest/touch";
        public const string grip = "/input/grip/pose";
        public const string aim = "/input/aim/pose";
        public const string haptic = "/output/haptic";

        private const string kDeviceLocalizedName = "Detached Oculus Touch Controller OpenXR";

        protected override void RegisterDeviceLayout()
        {
            InputSystem.RegisterLayout(typeof(DetachedOculusTouchController),
                matches: new InputDeviceMatcher()
                    .WithInterface(XRUtilities.InterfaceMatchAnyVersion)
                    .WithProduct(kDeviceLocalizedName));
        }

        protected override void UnregisterDeviceLayout()
        {
            InputSystem.RemoveLayout(nameof(DetachedOculusTouchController));
        }

        protected override string GetDeviceLayoutName()
        {
            return nameof(DetachedOculusTouchController);
        }

        private const string leftDetached = "/user/detached_controller_meta/left";
        private const string rightDetached = "/user/detached_controller_meta/right";

        protected override void RegisterActionMapsWithRuntime()
        {
            ActionMapConfig actionMap = new ActionMapConfig()
            {
                name = "detachedoculustouchcontroller",
                localizedName = kDeviceLocalizedName,
                desiredInteractionProfile = profile,
                manufacturer = "Meta",
                serialNumber = "",
                deviceInfos = new List<DeviceConfig>()
                {
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left),
                        userPath = leftDetached
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right),
                        userPath = rightDetached
                    }
                },
                actions = new List<ActionConfig>()
                {
                    // Joystick
                    new ActionConfig()
                    {
                        name = "thumbstick",
                        localizedName = "Thumbstick",
                        type = ActionType.Axis2D,
                        usages = new List<string>()
                        {
                            "Primary2DAxis"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = thumbstick,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Grip
                    new ActionConfig()
                    {
                        name = "grip",
                        localizedName = "Grip",
                        type = ActionType.Axis1D,
                        usages = new List<string>()
                        {
                            "Grip"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = squeeze,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Grip Pressed
                    new ActionConfig()
                    {
                        name = "gripPressed",
                        localizedName = "Grip Pressed",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "GripButton"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = squeeze,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Menu
                    new ActionConfig()
                    {
                        name = "menu",
                        localizedName = "Menu",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "MenuButton"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = menu,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = system,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            },
                        }
                    },
                    //A / X Press
                    new ActionConfig()
                    {
                        name = "primaryButton",
                        localizedName = "Primary Button",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "PrimaryButton"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = buttonX,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = buttonA,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            },
                        }
                    },
                    //A / X Touch
                    new ActionConfig()
                    {
                        name = "primaryTouched",
                        localizedName = "Primary Touched",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "PrimaryTouch"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = buttonXTouch,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = buttonATouch,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            },
                        }
                    },
                    //B / Y Press
                    new ActionConfig()
                    {
                        name = "secondaryButton",
                        localizedName = "Secondary Button",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "SecondaryButton"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = buttonY,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = buttonB,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            },
                        }
                    },
                    //B / Y Touch
                    new ActionConfig()
                    {
                        name = "secondaryTouched",
                        localizedName = "Secondary Touched",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "SecondaryTouch"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = buttonYTouch,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = buttonBTouch,
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            },
                        }
                    },
                    // Trigger
                    new ActionConfig()
                    {
                        name = "trigger",
                        localizedName = "Trigger",
                        type = ActionType.Axis1D,
                        usages = new List<string>()
                        {
                            "Trigger"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = trigger,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Trigger Pressed
                    new ActionConfig()
                    {
                        name = "triggerPressed",
                        localizedName = "Trigger Pressed",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "TriggerButton"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = trigger,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    //Trigger Touch
                    new ActionConfig()
                    {
                        name = "triggerTouched",
                        localizedName = "Trigger Touched",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "TriggerTouch"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = triggerTouch,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    //Thumbstick Clicked
                    new ActionConfig()
                    {
                        name = "thumbstickClicked",
                        localizedName = "Thumbstick Clicked",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "Primary2DAxisClick"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = thumbstickClick,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    //Thumbstick Touched
                    new ActionConfig()
                    {
                        name = "thumbstickTouched",
                        localizedName = "Thumbstick Touched",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "Primary2DAxisTouch"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = thumbstickTouch,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    //Thumbrest Touched
                    new ActionConfig()
                    {
                        name = "thumbrestTouched",
                        localizedName = "Thumbrest Touched",
                        type = ActionType.Binary,
                        usages = new List<string>()
                        {
                            "ThumbrestTouch"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = thumbrest,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Device Pose
                    new ActionConfig()
                    {
                        name = "devicePose",
                        localizedName = "Device Pose",
                        type = ActionType.Pose,
                        usages = new List<string>()
                        {
                            "Device"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = grip,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Pointer Pose
                    new ActionConfig()
                    {
                        name = "pointer",
                        localizedName = "Pointer Pose",
                        type = ActionType.Pose,
                        usages = new List<string>()
                        {
                            "Pointer"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = aim,
                                interactionProfileName = profile,
                            }
                        }
                    },
                    // Haptics
                    new ActionConfig()
                    {
                        name = "haptic",
                        localizedName = "Haptic Output",
                        type = ActionType.Vibrate,
                        usages = new List<string>() { "Haptic" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = haptic,
                                interactionProfileName = profile,
                            }
                        }
                    }
                }
            };

            AddActionMap(actionMap);
        }
    }
#endif // USING_XR_SDK_OPENXR
}
