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

#if USING_XR_SDK_OPENXR
using UnityEngine.InputSystem;

public class MetaQuestActionMap
{
    public enum Hand
    {
        LHand,
        RHand
    }

    public enum ControllerType
    {
        Touch = 1,
        TouchPro = 1 << 1,
        TouchPlus = 1 << 2,
        Hands = 1 << 3,
    }

    private static readonly (ControllerType controller, string name)[] ControllerNames = {
        (ControllerType.Touch, "OculusTouchController"),
        (ControllerType.Touch, "DetachedOculusTouchController"),
        (ControllerType.TouchPro, "QuestProTouchController"),
        (ControllerType.TouchPro, "DetachedQuestProTouchController"),
        (ControllerType.TouchPlus, "QuestTouchPlusController"),
        (ControllerType.TouchPlus, "DetachedQuestTouchPlusController"),
        (ControllerType.Hands, "HandInteraction"),
    };

    public const string QuestTouchDevice = "QuestTouch_";
    public const string QuestHandDevice = "QuestHand_";

    public const string LeftHand = "L_";
    public const string RightHand = "R_";

    public const string ButtonA = "ButtonA";
    public const string ButtonB = "ButtonB";
    public const string ButtonX = "ButtonX";
    public const string ButtonY = "ButtonY";
    public const string ButtonStart = "ButtonStart";
    public const string TouchA = "TouchA";
    public const string TouchB = "TouchB";
    public const string TouchX = "TouchX";
    public const string TouchY = "TouchY";

    public const string Grip = "Grip";
    public const string Trigger = "Trigger";
    public const string TriggerTouch = "TriggerTouch";
    public const string TriggerCurl = "TriggerCurl";
    public const string TriggerSlide = "TriggerSlide";
    public const string TriggerForce = "TriggerForce";

    public const string ThumbStick = "ThumbStick";
    public const string ThumbStickClick = "ThumbStickClick";
    public const string ThumbStickTouch = "ThumbStickTouch";
    public const string ThumbRestTouch = "ThumbRestTouch";
    public const string ThumbRestForce = "ThumbRestForce";
    public const string StylusForce = "StylusForce";

    public const string TrackingState = "TrackingState";
    public const string IsTracked = "IsTracked";
    public const string Position = "Position";
    public const string Rotation = "Rotation";
    public const string Velocity = "Velocity";
    public const string AngularVelocity = "AngularVelocity";

    public const string Haptic = "Haptic";
    public const string HapticThumb = "Haptic_Thumb";
    public const string HapticTrigger = "Haptic_Trigger";

    public const string NearTouchTrigger = "NearTouchTrigger";
    public const string NearTouchThumb = "NearTouchThumb";

    public readonly struct ActionBinding
    {
        public readonly string Name;
        public readonly Hand Hand;
        public readonly string BindingPath;
        public readonly InputActionType ActionType;

        public ActionBinding(string name, Hand hand, string bindingPath, InputActionType actionType)
        {
            this.Name = name;
            this.Hand = hand;
            this.BindingPath = bindingPath;
            this.ActionType = actionType;
        }
    }

    public static readonly ActionBinding[] CommonActions = {
        // Buttons
        new ActionBinding(QuestTouchDevice + ButtonX, Hand.LHand, "primaryButton", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + ButtonA, Hand.RHand, "primaryButton", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + ButtonY, Hand.LHand, "secondaryButton", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + ButtonB, Hand.RHand, "secondaryButton", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + ButtonStart, Hand.LHand, "menu", InputActionType.Button),

        // Touch Buttons
        new ActionBinding(QuestTouchDevice + TouchX, Hand.LHand, "primaryTouched", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + TouchA, Hand.RHand, "primaryTouched", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + TouchY, Hand.LHand, "secondaryTouched", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + TouchB, Hand.RHand, "secondaryTouched", InputActionType.Button),

        // Grip
        new ActionBinding(QuestTouchDevice + LeftHand + Grip, Hand.LHand, "grip", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Grip, Hand.RHand, "grip", InputActionType.Value),

        // Trigger
        new ActionBinding(QuestTouchDevice + LeftHand + Trigger, Hand.LHand, "trigger", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Trigger, Hand.RHand, "trigger", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerTouch, Hand.LHand, "triggerTouched", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerTouch, Hand.RHand, "triggerTouched", InputActionType.Value),

        // Thumbstick
        new ActionBinding(QuestTouchDevice + LeftHand + ThumbStick, Hand.LHand, "thumbstick", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + ThumbStick, Hand.RHand, "thumbstick", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + ThumbStickClick, Hand.LHand, "thumbstickClicked", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + ThumbStickClick, Hand.RHand, "thumbstickClicked", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + LeftHand + ThumbStickTouch, Hand.LHand, "thumbstickTouched", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + ThumbStickTouch, Hand.RHand, "thumbstickTouched", InputActionType.Button),

        // Thumbrest
        new ActionBinding(QuestTouchDevice + LeftHand + ThumbRestTouch, Hand.LHand, "thumbrestTouched", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + ThumbRestTouch, Hand.RHand, "thumbrestTouched", InputActionType.Button),

        // Tracking State
        new ActionBinding(QuestTouchDevice + LeftHand + TrackingState, Hand.LHand, "trackingState", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TrackingState, Hand.RHand, "trackingState", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + IsTracked, Hand.LHand, "isTracked", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + IsTracked, Hand.RHand, "isTracked", InputActionType.Value),

        // Pose
        new ActionBinding(QuestTouchDevice + LeftHand + Position, Hand.LHand, "devicePose/position", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Position, Hand.RHand, "devicePose/position", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + Rotation, Hand.LHand, "devicePose/rotation", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Rotation, Hand.RHand, "devicePose/rotation", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + Velocity, Hand.LHand, "devicePose/velocity", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Velocity, Hand.RHand, "devicePose/velocity", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + AngularVelocity, Hand.LHand, "devicePose/angularVelocity", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + AngularVelocity, Hand.RHand, "devicePose/angularVelocity", InputActionType.Value),

        // Haptic
        new ActionBinding(QuestTouchDevice + LeftHand + Haptic, Hand.LHand, "{Haptic}", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + Haptic, Hand.RHand, "{Haptic}", InputActionType.Value),
    };

    public static readonly ActionBinding[] TouchProActions =
    {
        // Force
        new ActionBinding(QuestTouchDevice + LeftHand + ThumbRestForce, Hand.LHand, "thumbrestForce", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + ThumbRestForce, Hand.RHand, "thumbrestForce", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + StylusForce, Hand.LHand, "stylusForce", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + StylusForce, Hand.RHand, "stylusForce", InputActionType.Value),

        // Trigger
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerCurl, Hand.LHand, "triggerCurl", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerCurl, Hand.RHand, "triggerCurl", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerSlide, Hand.LHand, "triggerSlide", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerSlide, Hand.RHand, "triggerSlide", InputActionType.Value),

        // Haptics
        new ActionBinding(QuestTouchDevice + LeftHand + HapticThumb, Hand.LHand, "hapticThumb", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + HapticThumb, Hand.RHand, "hapticThumb", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + HapticTrigger, Hand.LHand, "hapticTrigger", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + HapticTrigger, Hand.RHand, "hapticTrigger", InputActionType.Value),

        // Proximity
        new ActionBinding(QuestTouchDevice + LeftHand + NearTouchTrigger, Hand.LHand, "triggerProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + NearTouchTrigger, Hand.RHand, "triggerProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + LeftHand + NearTouchThumb, Hand.LHand, "thumbProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + NearTouchThumb, Hand.RHand, "thumbProximity", InputActionType.Button),
    };

    public static readonly ActionBinding[] TouchPlusActions =
    {
        // Force
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerForce, Hand.LHand, "triggerForce", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerForce, Hand.RHand, "triggerForce", InputActionType.Value),

        // Trigger
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerCurl, Hand.LHand, "triggerCurl", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerCurl, Hand.RHand, "triggerCurl", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + LeftHand + TriggerSlide, Hand.LHand, "triggerSlide", InputActionType.Value),
        new ActionBinding(QuestTouchDevice + RightHand + TriggerSlide, Hand.RHand, "triggerSlide", InputActionType.Value),

        // Proximity
        new ActionBinding(QuestTouchDevice + LeftHand + NearTouchTrigger, Hand.LHand, "triggerProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + NearTouchTrigger, Hand.RHand, "triggerProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + LeftHand + NearTouchThumb, Hand.LHand, "thumbProximity", InputActionType.Button),
        new ActionBinding(QuestTouchDevice + RightHand + NearTouchThumb, Hand.RHand, "thumbProximity", InputActionType.Button),
    };

    private static MetaQuestActionMap _instance;

    public static MetaQuestActionMap Instance => _instance ??= new MetaQuestActionMap();

    public InputActionMap ActionMap { get; } = new InputActionMap("MetaQuestActions");

    private MetaQuestActionMap()
    {
        ApplyBindings();
    }

    // Reset the instance when entering play mode in case domain reload is disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        _instance?.ActionMap.Dispose();
        _instance = null;
    }

    public static void Enable()
    {
        Instance.ActionMap.Enable();
    }

    public static InputAction GetAction(string actionName)
    {
        return Instance.ActionMap.FindAction(actionName);
    }

    public void ApplyBindings()
    {
        // Add common actions between all controllers
        foreach (ActionBinding config in CommonActions)
        {
            InputAction action = GetOrCreateAction(config.Name, config.ActionType);
            AddBinding(action, config.Hand, config.BindingPath, ControllerType.Touch | ControllerType.TouchPro | ControllerType.TouchPlus);
        }

        // Now Add Touch Pro
        foreach (ActionBinding config in TouchProActions)
        {
            InputAction action = GetOrCreateAction(config.Name, config.ActionType);
            AddBinding(action, config.Hand, config.BindingPath, ControllerType.TouchPro);
        }

        // Now Add Touch Plus
        foreach (ActionBinding config in TouchPlusActions)
        {
            InputAction action = GetOrCreateAction(config.Name, config.ActionType);
            AddBinding(action, config.Hand, config.BindingPath, ControllerType.TouchPlus);
        }
    }

    private InputAction GetOrCreateAction(string name, InputActionType type)
    {
        InputAction action = ActionMap.FindAction(name);
        if (action == null)
        {
            action = ActionMap.AddAction(name, type);
        }
        return action;
    }

    private void AddBinding(InputAction action, Hand hand, string bindingPath, ControllerType supportedControllers)
    {
        string handName = hand == Hand.LHand ? "LeftHand" : "RightHand";
        foreach (var (controller, controllerName) in ControllerNames)
        {
            if ((supportedControllers & controller) != 0)
            {
                action.AddBinding($"<{controllerName}>{{{handName}}}/{bindingPath}");
            }
        }
    }
}
#endif
