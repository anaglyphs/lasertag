using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Utilities;

namespace UnityEngine.XR.Interaction.Toolkit.Samples.InteractionSimulator
{
    class XRInteractionSimulatorInputFeedbackUI : MonoBehaviour
    {
        [Header("Input Device Modes")]

        [SerializeField]
        GameObject m_HMDPanel;

        [SerializeField]
        GameObject m_MirrorModePanel;

        [SerializeField]
        GameObject m_RightHandPanel;

        [SerializeField]
        GameObject m_LeftHandPanel;

        [SerializeField]
        GameObject m_RightControllerPanel;

        [SerializeField]
        GameObject m_LeftControllerPanel;

        [SerializeField]
        GameObject m_RightOtherHandPanel;

        [SerializeField]
        GameObject m_LeftOtherHandPanel;

        [SerializeField]
        GameObject m_RightOtherControllerPanel;

        [SerializeField]
        GameObject m_LeftOtherControllerPanel;

        [SerializeField]
        GameObject m_BothControllersPanel;

        [SerializeField]
        GameObject m_BothHandsPanel;

        [Header("Controller Input Modes")]

        [SerializeField]
        GameObject m_TriggerPanel;
        [SerializeField]
        Image m_TriggerBg;

        [SerializeField]
        GameObject m_GripPanel;
        [SerializeField]
        Image m_GripBg;

        [SerializeField]
        GameObject m_PrimaryPanel;
        [SerializeField]
        Image m_PrimaryBg;

        [SerializeField]
        GameObject m_SecondaryPanel;
        [SerializeField]
        Image m_SecondaryBg;

        [SerializeField]
        GameObject m_MenuPanel;
        [SerializeField]
        Image m_MenuBg;

        [SerializeField]
        GameObject m_Primary2DAxisClickPanel;
        [SerializeField]
        Image m_Primary2DAxisClickBg;

        [SerializeField]
        GameObject m_Secondary2DAxisClickPanel;
        [SerializeField]
        Image m_Secondary2DAxisClickBg;

        [SerializeField]
        GameObject m_Primary2DAxisTouchPanel;
        [SerializeField]
        Image m_Primary2DAxisTouchBg;

        [SerializeField]
        GameObject m_Secondary2DAxisTouchPanel;
        [SerializeField]
        Image m_Secondary2DAxisTouchBg;

        [SerializeField]
        GameObject m_PrimaryTouchPanel;
        [SerializeField]
        Image m_PrimaryTouchBg;

        [SerializeField]
        GameObject m_SecondaryTouchPanel;
        [SerializeField]
        Image m_SecondaryTouchBg;

        [SerializeField]
        GameObject m_ControllerHotkeyPanel;
        [SerializeField]
        Image m_ControllerHotkeyBg;
        [SerializeField]
        Image m_ControllerHotkeyIcon;
        [SerializeField]
        Text m_ControllerHotkeyText;
        [SerializeField]
        Sprite m_LeftControllerSprite;
        [SerializeField]
        Sprite m_RightControllerSprite;

        [SerializeField]
        GameObject m_ControllerInputRow;

        [Header("Other Controller Input Modes")]

        [SerializeField]
        GameObject m_OtherTriggerPanel;
        [SerializeField]
        Image m_OtherTriggerBg;

        [SerializeField]
        GameObject m_OtherGripPanel;
        [SerializeField]
        Image m_OtherGripBg;

        [SerializeField]
        GameObject m_OtherPrimaryPanel;
        [SerializeField]
        Image m_OtherPrimaryBg;

        [SerializeField]
        GameObject m_OtherSecondaryPanel;
        [SerializeField]
        Image m_OtherSecondaryBg;

        [SerializeField]
        GameObject m_OtherMenuPanel;
        [SerializeField]
        Image m_OtherMenuBg;

        [SerializeField]
        GameObject m_OtherPrimary2DAxisClickPanel;
        [SerializeField]
        Image m_OtherPrimary2DAxisClickBg;

        [SerializeField]
        GameObject m_OtherSecondary2DAxisClickPanel;
        [SerializeField]
        Image m_OtherSecondary2DAxisClickBg;

        [SerializeField]
        GameObject m_OtherPrimary2DAxisTouchPanel;
        [SerializeField]
        Image m_OtherPrimary2DAxisTouchBg;

        [SerializeField]
        GameObject m_OtherSecondary2DAxisTouchPanel;
        [SerializeField]
        Image m_OtherSecondary2DAxisTouchBg;

        [SerializeField]
        GameObject m_OtherPrimaryTouchPanel;
        [SerializeField]
        Image m_OtherPrimaryTouchBg;

        [SerializeField]
        GameObject m_OtherSecondaryTouchPanel;
        [SerializeField]
        Image m_OtherSecondaryTouchBg;

        [Header("Hand Input Modes")]

        [SerializeField]
        GameObject m_PokePanel;
        [SerializeField]
        Image m_PokePanelBg;

        [SerializeField]
        GameObject m_PinchPanel;
        [SerializeField]
        Image m_PinchPanelBg;

        [SerializeField]
        GameObject m_GrabPanel;
        [SerializeField]
        Image m_GrabPanelBg;

        [SerializeField]
        GameObject m_CustomPanel;

        [SerializeField]
        GameObject m_HandHotkeyPanel;
        [SerializeField]
        Image m_HandHotkeyBg;
        [SerializeField]
        Image m_HandHotkeyIcon;
        [SerializeField]
        Text m_HandHotkeyText;
        [SerializeField]
        Sprite m_LeftHandSprite;
        [SerializeField]
        Sprite m_RightHandSprite;

        [SerializeField]
        GameObject m_HandInputRow;

        [Header("Other Hand Input Modes")]

        [SerializeField]
        GameObject m_OtherIdlePanel;
        [SerializeField]
        Image m_OtherIdlePanelBg;

        [SerializeField]
        GameObject m_OtherPokePanel;
        [SerializeField]
        Image m_OtherPokePanelBg;

        [SerializeField]
        GameObject m_OtherPinchPanel;
        [SerializeField]
        Image m_OtherPinchPanelBg;

        [SerializeField]
        GameObject m_OtherGrabPanel;
        [SerializeField]
        Image m_OtherGrabPanelBg;

        [SerializeField]
        GameObject m_OtherCustomPanel;

        [Header("General Input")]

        [SerializeField]
        GameObject m_TranslateForwardPanel;

        [SerializeField]
        GameObject m_TranslateBackwardPanel;

        [SerializeField]
        GameObject m_TranslateUpPanel;

        [SerializeField]
        GameObject m_TranslateDownPanel;

        [SerializeField]
        GameObject m_TranslateLeftPanel;

        [SerializeField]
        GameObject m_TranslateRightPanel;

        [SerializeField]
        GameObject m_RotateUpPanel;

        [SerializeField]
        GameObject m_RotateDownPanel;

        [SerializeField]
        GameObject m_RotateLeftPanel;

        [SerializeField]
        GameObject m_RotateRightPanel;

        [SerializeField]
        GameObject m_OtherDeviceInputRow;

        enum ActiveDeviceMode
        {
            LeftController,
            RightController,
            BothControllers,
            LeftHand,
            RightHand,
            BothHands,
            HMD,
            None,
        }

        XRInteractionSimulator m_Simulator;
        ActiveDeviceMode m_ActiveDeviceMode = ActiveDeviceMode.None;
        SimulatedDeviceLifecycleManager.DeviceMode m_PreviousDeviceMode = SimulatedDeviceLifecycleManager.DeviceMode.None;
        TargetedDevices m_PreviousTargetedDevice = TargetedDevices.None;
        ControllerInputMode m_PreviousControllerInputMode;
        SimulatedHandExpression m_PreviousHandExpression;

        Dictionary<ControllerInputMode, GameObject> m_ControllerInputPanels;
        Dictionary<ControllerInputMode, Image> m_ControllerInputBgs;
        Dictionary<ControllerInputMode, GameObject> m_OtherControllerInputPanels;
        Dictionary<ControllerInputMode, Image> m_OtherControllerInputBgs;
        Dictionary<string, GameObject> m_HandExpressionPanels;
        Dictionary<string, Image> m_HandExpressionBgs;
        Dictionary<string, GameObject> m_OtherHandExpressionPanels;
        Dictionary<string, Image> m_OtherHandExpressionBgs;

        SimulatedDeviceLifecycleManager m_DeviceLifecycleManager;
        SimulatedHandPlaybackManager m_HandPlaybackManager;

        bool m_IsPerformingLeftInput;
        bool m_IsPerformingRightInput;
        bool m_ToggleMousePressed;

        // ReSharper disable InconsistentNaming -- Treat as constants
        static readonly Color k_DefaultPanelColor = new Color(0x55 / 255f, 0x55 / 255f, 0x55 / 255f);
        static readonly Color k_SelectedColor = new Color(0x4F / 255f, 0x65 / 255f, 0x7F / 255f);
        static readonly Color k_EnabledColor = new Color(0x88 / 255f, 0x88 / 255f, 0x88 / 255f);
        // ReSharper restore InconsistentNaming

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void Start()
        {
            if (!ComponentLocatorUtility<XRInteractionSimulator>.TryFindComponent(out m_Simulator))
            {
                Debug.LogError($"Could not find the XRInteractionSimulator component, disabling simulator UI.", this);
                gameObject.SetActive(false);
                return;
            }

            if (!ComponentLocatorUtility<SimulatedDeviceLifecycleManager>.TryFindComponent(out m_DeviceLifecycleManager))
            {
                Debug.LogError($"Could not find SimulatedDeviceLifecycleManager component in the scene, disabling simulator UI.", this);
                gameObject.SetActive(false);
                return;
            }

            if (!ComponentLocatorUtility<SimulatedHandPlaybackManager>.TryFindComponent(out m_HandPlaybackManager))
            {
                Debug.LogError($"Could not find SimulatedHandPlaybackManager component in the scene, disabling simulator UI.", this);
                gameObject.SetActive(false);
                return;
            }

            InitializeUIDictionaries();
            ActivateControllerPanels();
            ActivateHandPanels();
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void Update()
        {
            HandleActiveDeviceModePanels();
            HandleOtherDeviceModePanels();
            HandleGeneralInputFeedback();
            HandleActiveInputModePanels();
            HandleOtherActiveInputModePanels();

            if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
                HandleDeviceHotkeyPanels();
            else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                HandleHandHotkeyPanels();
        }

        void HandleGeneralInputFeedback()
        {
            if (m_Simulator.toggleMouseInput.ReadWasCompletedThisFrame())
                m_ToggleMousePressed = false;

            if (!m_ToggleMousePressed)
                ClearActiveGeneralInputPanels();

            HandleKeyboardInputFeedback();
            HandleMouseInputFeedback();
        }

        void HandleKeyboardInputFeedback()
        {
            if (m_Simulator.translateXInput.TryReadValue(out var xValue))
            {
                m_ToggleMousePressed = false;

                if (xValue >= 0f)
                    m_TranslateRightPanel.SetActive(true);
                else
                    m_TranslateLeftPanel.SetActive(true);
            }

            if (m_Simulator.translateYInput.TryReadValue(out var yValue))
            {
                m_ToggleMousePressed = false;

                if (yValue >= 0f)
                    m_TranslateUpPanel.SetActive(true);
                else
                    m_TranslateDownPanel.SetActive(true);
            }

            if (m_Simulator.translateZInput.TryReadValue(out var zValue))
            {
                m_ToggleMousePressed = false;

                if (zValue >= 0f)
                    m_TranslateForwardPanel.SetActive(true);
                else
                    m_TranslateBackwardPanel.SetActive(true);
            }

            if (m_Simulator.keyboardRotationDeltaInput.TryReadValue(out var rotValue))
            {
                m_ToggleMousePressed = false;

                if (rotValue.x > 0f)
                    m_RotateRightPanel.SetActive(true);
                else if (rotValue.x < 0f)
                    m_RotateLeftPanel.SetActive(true);

                if (rotValue.y > 0f)
                    m_RotateUpPanel.SetActive(true);
                else if (rotValue.y < 0f)
                    m_RotateDownPanel.SetActive(true);
            }
        }

        void HandleMouseInputFeedback()
        {
            if (m_Simulator.toggleMouseInput.ReadIsPerformed() && m_Simulator.mouseRotationDeltaInput.TryReadValue(out var rotValue))
            {
                m_ToggleMousePressed = true;

                m_TranslateBackwardPanel.SetActive(false);
                m_TranslateForwardPanel.SetActive(false);
                m_TranslateUpPanel.SetActive(false);
                m_TranslateDownPanel.SetActive(false);
                m_TranslateRightPanel.SetActive(false);
                m_TranslateLeftPanel.SetActive(false);

                if (rotValue.x > 0f)
                {
                    m_RotateLeftPanel.SetActive(false);
                    m_RotateRightPanel.SetActive(true);
                }
                else if (rotValue.x < 0f)
                {
                    m_RotateRightPanel.SetActive(false);
                    m_RotateLeftPanel.SetActive(true);
                }

                if (rotValue.y > 0f)
                {
                    m_RotateDownPanel.SetActive(false);
                    m_RotateUpPanel.SetActive(true);
                }
                else if (rotValue.y < 0f)
                {
                    m_RotateUpPanel.SetActive(false);
                    m_RotateDownPanel.SetActive(true);
                }
            }

            if (m_Simulator.toggleMouseInput.ReadIsPerformed() && m_Simulator.mouseScrollInput.TryReadValue(out var scrollValue))
            {
                m_ToggleMousePressed = true;

                m_RotateLeftPanel.SetActive(false);
                m_RotateRightPanel.SetActive(false);
                m_RotateDownPanel.SetActive(false);
                m_RotateUpPanel.SetActive(false);

                if (scrollValue.y >= 0f)
                {
                    m_TranslateBackwardPanel.SetActive(false);
                    m_TranslateForwardPanel.SetActive(true);
                }
                else
                {
                    m_TranslateForwardPanel.SetActive(false);
                    m_TranslateBackwardPanel.SetActive(true);
                }
            }
        }

        void HandleActiveDeviceModePanels()
        {
            if (m_Simulator.manipulatingFPS || m_Simulator.manipulatingHMD)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.HMD)
                    return;

                ClearActiveInputModePanels();
                m_HMDPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.HMD;
            }
            else if (m_Simulator.manipulatingLeftController && m_Simulator.manipulatingRightController)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.BothControllers)
                    return;

                ClearActiveInputModePanels();
                m_BothControllersPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.BothControllers;
            }
            else if (m_Simulator.manipulatingLeftController)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.LeftController)
                    return;

                ClearActiveInputModePanels();
                m_LeftControllerPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.LeftController;
            }
            else if (m_Simulator.manipulatingRightController)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.RightController)
                    return;

                ClearActiveInputModePanels();
                m_RightControllerPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.RightController;
            }
            else if (m_Simulator.manipulatingLeftHand && m_Simulator.manipulatingRightHand)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.BothHands)
                    return;

                ClearActiveInputModePanels();
                m_BothHandsPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.BothHands;
            }
            else if (m_Simulator.manipulatingLeftHand)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.LeftHand)
                    return;

                ClearActiveInputModePanels();
                m_LeftHandPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.LeftHand;
            }
            else if (m_Simulator.manipulatingRightHand)
            {
                if (m_ActiveDeviceMode == ActiveDeviceMode.RightHand)
                    return;

                ClearActiveInputModePanels();
                m_RightHandPanel.SetActive(true);
                m_ActiveDeviceMode = ActiveDeviceMode.RightHand;
            }
        }

        void HandleOtherDeviceModePanels()
        {
            ClearOtherInputModePanels();

            if (m_Simulator.manipulatingLeftController && !m_Simulator.manipulatingRightController)
            {
                m_RightOtherControllerPanel.SetActive(true);
            }
            else if (m_Simulator.manipulatingRightController && !m_Simulator.manipulatingLeftController)
            {
                m_LeftOtherControllerPanel.SetActive(true);
            }
            else if (m_Simulator.manipulatingLeftHand && !m_Simulator.manipulatingRightHand)
            {
                m_RightOtherHandPanel.SetActive(true);
            }
            else if (m_Simulator.manipulatingRightHand && !m_Simulator.manipulatingLeftHand)
            {
                m_LeftOtherHandPanel.SetActive(true);
            }
        }

        void HandleActiveInputModePanels()
        {
            if (m_Simulator.manipulatingFPS || m_Simulator.manipulatingHMD)
            {
                m_ControllerInputRow.SetActive(false);
                m_HandInputRow.SetActive(false);
                m_OtherDeviceInputRow.SetActive(false);
                return;
            }

            if (!m_ControllerInputRow.activeSelf && m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
            {
                m_ControllerInputRow.SetActive(true);
                m_OtherDeviceInputRow.SetActive(true);
            }

            if (!m_HandInputRow.activeSelf && m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
            {
                m_HandInputRow.SetActive(true);
                m_OtherDeviceInputRow.SetActive(true);
            }

            if (m_PreviousDeviceMode != m_DeviceLifecycleManager.deviceMode)
            {
                m_ControllerInputRow.SetActive(false);
                m_HandInputRow.SetActive(false);
                m_IsPerformingLeftInput = false;
                m_IsPerformingRightInput = false;

                if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
                {
                    m_ControllerInputRow.SetActive(true);
                    HighlightActiveControllerInputMode(k_SelectedColor);
                }
                else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                {
                    m_HandInputRow.SetActive(true);
                    HighlightActiveHandInputMode(k_SelectedColor);
                }

                m_PreviousDeviceMode = m_DeviceLifecycleManager.deviceMode;
            }

            if ((m_Simulator.manipulatingLeftDevice && !m_IsPerformingLeftInput) || (m_Simulator.manipulatingRightDevice && !m_IsPerformingRightInput))
            {
                if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller && m_PreviousControllerInputMode != m_Simulator.controllerInputMode)
                    HighlightActiveControllerInputMode(k_SelectedColor);
                else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand && m_PreviousHandExpression != m_Simulator.currentHandExpression)
                    HighlightActiveHandInputMode(k_SelectedColor);
            }

            if (m_PreviousTargetedDevice != m_Simulator.targetedDeviceInput && (m_Simulator.targetedDeviceInput == TargetedDevices.LeftDevice
                || m_Simulator.targetedDeviceInput == TargetedDevices.RightDevice))
            {
                if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
                {
                    if ((m_Simulator.manipulatingLeftDevice && m_IsPerformingLeftInput) || (m_Simulator.manipulatingRightDevice && m_IsPerformingRightInput))
                        HighlightActiveControllerInputMode(k_EnabledColor);
                    else
                        HighlightActiveControllerInputMode(k_SelectedColor);
                }
                else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                {
                    if ((m_Simulator.manipulatingLeftDevice && m_IsPerformingLeftInput) || (m_Simulator.manipulatingRightDevice && m_IsPerformingRightInput))
                    {
                        if (m_Simulator.currentHandExpression.sequenceType == SimulatedHandExpression.SequenceType.MultiFrame)
                            HighlightActiveHandInputMode(k_EnabledColor);
                    }
                    else
                    {
                        HighlightActiveHandInputMode(k_SelectedColor);
                    }
                }
            }

            if (m_Simulator.togglePerformQuickActionInput.ReadWasPerformedThisFrame())
            {
                if (m_Simulator.manipulatingLeftDevice)
                    m_IsPerformingLeftInput = !m_IsPerformingLeftInput;

                if (m_Simulator.manipulatingRightDevice)
                    m_IsPerformingRightInput = !m_IsPerformingRightInput;

                if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
                {
                    if ((m_Simulator.manipulatingLeftDevice && m_IsPerformingLeftInput) || (m_Simulator.manipulatingRightDevice && m_IsPerformingRightInput))
                        HighlightActiveControllerInputMode(k_EnabledColor);
                    else
                        HighlightActiveControllerInputMode(k_SelectedColor);
                }
                else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                {
                    if ((m_Simulator.manipulatingLeftDevice && m_IsPerformingLeftInput) || (m_Simulator.manipulatingRightDevice && m_IsPerformingRightInput))
                    {
                        if (m_Simulator.currentHandExpression.sequenceType == SimulatedHandExpression.SequenceType.MultiFrame)
                        {
                            HighlightActiveHandInputMode(k_EnabledColor);
                        }
                    }
                    else
                    {
                        HighlightActiveHandInputMode(k_SelectedColor);
                    }
                }
            }

            if (m_Simulator.cycleQuickActionInput.ReadWasPerformedThisFrame())
            {
                if (m_Simulator.manipulatingLeftDevice)
                    m_IsPerformingLeftInput = false;

                if (m_Simulator.manipulatingRightDevice)
                    m_IsPerformingRightInput = false;
            }
        }

        void HandleOtherActiveInputModePanels()
        {
            ClearOtherActiveInputPanels();

            if (m_Simulator.manipulatingLeftController && !m_Simulator.manipulatingRightController)
            {
                var panel = m_OtherControllerInputPanels[m_Simulator.rightControllerInputMode];
                panel.SetActive(true);

                if (m_IsPerformingRightInput)
                    HighlightOtherControllerInputMode(m_Simulator.rightControllerInputMode, k_EnabledColor);
                else
                    HighlightOtherControllerInputMode(m_Simulator.rightControllerInputMode, k_DefaultPanelColor);
            }
            else if (m_Simulator.manipulatingRightController && !m_Simulator.manipulatingLeftController)
            {
                var panel = m_OtherControllerInputPanels[m_Simulator.leftControllerInputMode];
                panel.SetActive(true);

                if (m_IsPerformingLeftInput)
                    HighlightOtherControllerInputMode(m_Simulator.leftControllerInputMode, k_EnabledColor);
                else
                    HighlightOtherControllerInputMode(m_Simulator.leftControllerInputMode, k_DefaultPanelColor);
            }
            else if (m_Simulator.manipulatingLeftHand && !m_Simulator.manipulatingRightHand)
            {
                var panel = m_OtherHandExpressionPanels[m_Simulator.rightCurrentHandExpression.name];
                panel.SetActive(true);

                if (m_IsPerformingRightInput && m_Simulator.rightCurrentHandExpression.sequenceType == SimulatedHandExpression.SequenceType.MultiFrame)
                    HighlightOtherHandInputMode(m_Simulator.rightCurrentHandExpression.name, k_EnabledColor);
                else
                    HighlightOtherHandInputMode(m_Simulator.rightCurrentHandExpression.name, k_DefaultPanelColor);
            }
            else if (m_Simulator.manipulatingRightHand && !m_Simulator.manipulatingLeftHand)
            {
                var panel = m_OtherHandExpressionPanels[m_Simulator.leftCurrentHandExpression.name];
                panel.SetActive(true);

                if (m_IsPerformingLeftInput && m_Simulator.leftCurrentHandExpression.sequenceType == SimulatedHandExpression.SequenceType.MultiFrame)
                    HighlightOtherHandInputMode(m_Simulator.leftCurrentHandExpression.name, k_EnabledColor);
                else
                    HighlightOtherHandInputMode(m_Simulator.leftCurrentHandExpression.name, k_DefaultPanelColor);
            }

            if (m_Simulator.mouseClickInput.ReadIsPerformed())
            {
                if (m_Simulator.manipulatingLeftDevice)
                    m_IsPerformingLeftInput = false;

                if (m_Simulator.manipulatingRightDevice)
                    m_IsPerformingRightInput = false;

                if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
                    HighlightActiveControllerInputMode(k_SelectedColor);
                else if (m_DeviceLifecycleManager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                    HighlightActiveHandInputMode(k_SelectedColor);
            }
        }

        void HandleDeviceHotkeyPanels()
        {
            if (m_Simulator.gripInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.gripInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.gripInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.triggerInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.triggerInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.triggerInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.primaryButtonInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.primaryButtonInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.primaryButtonInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.secondaryButtonInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.secondaryButtonInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.secondaryButtonInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.menuInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.menuInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.menuInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.primary2DAxisClickInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.primary2DAxisClickInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.primary2DAxisClickInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.secondary2DAxisClickInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.secondary2DAxisClickInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.secondary2DAxisClickInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.primary2DAxisTouchInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.primary2DAxisTouchInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.primary2DAxisTouchInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.secondary2DAxisTouchInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.secondary2DAxisTouchInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.secondary2DAxisTouchInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.primaryTouchInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.primaryTouchInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.primaryTouchInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }

            if (m_Simulator.secondaryTouchInput.ReadIsPerformed())
            {
                ApplyHotkeyText(m_Simulator.secondaryTouchInput, SimulatedDeviceLifecycleManager.DeviceMode.Controller);
                m_ControllerHotkeyPanel.SetActive(true);
            }
            else if (m_Simulator.secondaryTouchInput.ReadWasCompletedThisFrame())
            {
                m_ControllerHotkeyPanel.SetActive(false);
            }
        }

        void HandleHandHotkeyPanels()
        {
            foreach (var handExpression in m_HandPlaybackManager.simulatedHandExpressions)
            {
                if (handExpression.toggleInput.ReadIsPerformed())
                {
                    ApplyHotkeyText(handExpression.toggleInput, SimulatedDeviceLifecycleManager.DeviceMode.Hand);
                    m_HandHotkeyPanel.SetActive(true);
                }
                else if (handExpression.toggleInput.ReadWasCompletedThisFrame())
                {
                    m_HandHotkeyPanel.SetActive(false);
                }
            }
        }

        void ApplyHotkeyText(XRInputButtonReader inputReader, SimulatedDeviceLifecycleManager.DeviceMode mode)
        {
            string bindingText = inputReader.inputActionReferencePerformed.action.GetBindingDisplayString(0);

            if (mode == SimulatedDeviceLifecycleManager.DeviceMode.Controller)
            {
                if (m_Simulator.leftDeviceActionsInput.ReadIsPerformed())
                    m_ControllerHotkeyIcon.sprite = m_LeftControllerSprite;
                else
                    m_ControllerHotkeyIcon.sprite = m_RightControllerSprite;

                m_ControllerHotkeyText.text = $"{bindingText}";
            }
            else if (mode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
            {
                if (m_Simulator.leftDeviceActionsInput.ReadIsPerformed())
                    m_HandHotkeyIcon.sprite = m_LeftHandSprite;
                else
                    m_HandHotkeyIcon.sprite = m_RightHandSprite;

                m_HandHotkeyText.text = $"{bindingText}";
            }
        }

        void ActivateControllerPanels()
        {
            for (var i = 0; i < m_Simulator.quickActionControllerInputModes.Count; i++)
            {
                if (!m_ControllerInputPanels.TryGetValue(m_Simulator.quickActionControllerInputModes[i], out var panel))
                {
                    string inputModeName = m_Simulator.quickActionControllerInputModes[i].ToString();
                    Debug.LogError($"Panel for the {inputModeName} controller input mode does not exist.", this);
                    continue;
                }

                panel.SetActive(true);
                panel.transform.SetSiblingIndex(i);
            }
        }

        void ActivateHandPanels()
        {
            for (var i = 0; i < m_HandPlaybackManager.simulatedHandExpressions.Count; i++)
            {
                if (m_HandPlaybackManager.simulatedHandExpressions[i].isQuickAction)
                {
                    string handExpressionName = m_HandPlaybackManager.simulatedHandExpressions[i].name;

                    if (!m_HandExpressionPanels.TryGetValue(handExpressionName, out var panel))
                    {
                        Debug.LogError($"Panel for the {handExpressionName} hand expression does not exist.", this);
                        continue;
                    }

                    panel.SetActive(true);
                    panel.transform.SetSiblingIndex(i);
                }
            }
        }

        void InitializeUIDictionaries()
        {
            m_ControllerInputPanels = new Dictionary<ControllerInputMode, GameObject>
            {
                {ControllerInputMode.Trigger, m_TriggerPanel},
                {ControllerInputMode.Grip, m_GripPanel},
                {ControllerInputMode.PrimaryButton, m_PrimaryPanel},
                {ControllerInputMode.SecondaryButton, m_SecondaryPanel},
                {ControllerInputMode.Menu, m_MenuPanel},
                {ControllerInputMode.Primary2DAxisClick, m_Primary2DAxisClickPanel},
                {ControllerInputMode.Secondary2DAxisClick, m_Secondary2DAxisClickPanel},
                {ControllerInputMode.Primary2DAxisTouch, m_Primary2DAxisTouchPanel},
                {ControllerInputMode.Secondary2DAxisTouch, m_Secondary2DAxisTouchPanel},
                {ControllerInputMode.PrimaryTouch, m_PrimaryTouchPanel},
                {ControllerInputMode.SecondaryTouch, m_SecondaryTouchPanel},
            };

            m_OtherControllerInputPanels = new Dictionary<ControllerInputMode, GameObject>
            {
                {ControllerInputMode.Trigger, m_OtherTriggerPanel},
                {ControllerInputMode.Grip, m_OtherGripPanel},
                {ControllerInputMode.PrimaryButton, m_OtherPrimaryPanel},
                {ControllerInputMode.SecondaryButton, m_OtherSecondaryPanel},
                {ControllerInputMode.Menu, m_OtherMenuPanel},
                {ControllerInputMode.Primary2DAxisClick, m_OtherPrimary2DAxisClickPanel},
                {ControllerInputMode.Secondary2DAxisClick, m_OtherSecondary2DAxisClickPanel},
                {ControllerInputMode.Primary2DAxisTouch, m_OtherPrimary2DAxisTouchPanel},
                {ControllerInputMode.Secondary2DAxisTouch, m_OtherSecondary2DAxisTouchPanel},
                {ControllerInputMode.PrimaryTouch, m_OtherPrimaryTouchPanel},
                {ControllerInputMode.SecondaryTouch, m_OtherSecondaryTouchPanel},
            };

            m_ControllerInputBgs = new Dictionary<ControllerInputMode, Image>
            {
                {ControllerInputMode.Trigger, m_TriggerBg},
                {ControllerInputMode.Grip, m_GripBg},
                {ControllerInputMode.PrimaryButton, m_PrimaryBg},
                {ControllerInputMode.SecondaryButton, m_SecondaryBg},
                {ControllerInputMode.Menu, m_MenuBg},
                {ControllerInputMode.Primary2DAxisClick, m_Primary2DAxisClickBg},
                {ControllerInputMode.Secondary2DAxisClick, m_Secondary2DAxisClickBg},
                {ControllerInputMode.Primary2DAxisTouch, m_Primary2DAxisTouchBg},
                {ControllerInputMode.Secondary2DAxisTouch, m_Secondary2DAxisTouchBg},
                {ControllerInputMode.PrimaryTouch, m_PrimaryTouchBg},
                {ControllerInputMode.SecondaryTouch, m_SecondaryTouchBg},
            };

            m_OtherControllerInputBgs = new Dictionary<ControllerInputMode, Image>
            {
                {ControllerInputMode.Trigger, m_OtherTriggerBg},
                {ControllerInputMode.Grip, m_OtherGripBg},
                {ControllerInputMode.PrimaryButton, m_OtherPrimaryBg},
                {ControllerInputMode.SecondaryButton, m_OtherSecondaryBg},
                {ControllerInputMode.Menu, m_OtherMenuBg},
                {ControllerInputMode.Primary2DAxisClick, m_OtherPrimary2DAxisClickBg},
                {ControllerInputMode.Secondary2DAxisClick, m_OtherSecondary2DAxisClickBg},
                {ControllerInputMode.Primary2DAxisTouch, m_OtherPrimary2DAxisTouchBg},
                {ControllerInputMode.Secondary2DAxisTouch, m_OtherSecondary2DAxisTouchBg},
                {ControllerInputMode.PrimaryTouch, m_OtherPrimaryTouchBg},
                {ControllerInputMode.SecondaryTouch, m_OtherSecondaryTouchBg},
            };

            m_HandExpressionPanels = new Dictionary<string, GameObject>
            {
                {"Poke", m_PokePanel},
                {"Pinch", m_PinchPanel},
                {"Grab", m_GrabPanel},
            };

            m_OtherHandExpressionPanels = new Dictionary<string, GameObject>
            {
                {"Idle", m_OtherIdlePanel},
                {"Poke", m_OtherPokePanel},
                {"Pinch", m_OtherPinchPanel},
                {"Grab", m_OtherGrabPanel},
            };

            m_HandExpressionBgs = new Dictionary<string, Image>
            {
                {"Poke", m_PokePanelBg},
                {"Pinch", m_PinchPanelBg},
                {"Grab", m_GrabPanelBg},
            };

            m_OtherHandExpressionBgs = new Dictionary<string, Image>
            {
                {"Poke", m_OtherPokePanelBg},
                {"Pinch", m_OtherPinchPanelBg},
                {"Grab", m_OtherGrabPanelBg},
            };

            InitializeCustomHandExpressionPanels();
        }

        void InitializeCustomHandExpressionPanels()
        {
            foreach (var handExpression in m_HandPlaybackManager.simulatedHandExpressions)
            {
                InstantiateHandPanelObjects(handExpression, m_CustomPanel, m_HandExpressionPanels, m_HandExpressionBgs);
                InstantiateHandPanelObjects(handExpression, m_OtherCustomPanel, m_OtherHandExpressionPanels, m_OtherHandExpressionBgs);
            }
        }

        void InstantiateHandPanelObjects(SimulatedHandExpression handExpression, GameObject customPanel, Dictionary<string, GameObject> panels, Dictionary<string, Image> bgs)
        {
            if (!panels.ContainsKey(handExpression.name) && handExpression.isQuickAction)
            {
                var panel = Instantiate(customPanel, customPanel.transform.parent);
                panel.name = $"{handExpression.name}Panel";

                var bgImage = panel.GetComponentInChildren<Image>();
                if (bgImage == null)
                {
                    var bgImageGO = Instantiate(new GameObject(), panel.transform);
                    bgImageGO.name = "Bg";
                    bgImage = bgImageGO.AddComponent<Image>();
                }

                var textUI = panel.GetComponentInChildren<Text>();
                if (textUI == null)
                {
                    var textGO = Instantiate(new GameObject(), panel.transform);
                    textGO.name = "Text";
                    textUI = textGO.AddComponent<Text>();
                    textUI.fontStyle = FontStyle.Bold;
                }
                textUI.text = handExpression.name;

                panels[handExpression.name] = panel;
                bgs[handExpression.name] = bgImage;
            }
        }

        void HighlightActiveControllerInputMode(Color highlightColor)
        {
            ClearHighlightedControllerPanels(m_ControllerInputBgs);

            if (!m_ControllerInputBgs.TryGetValue(m_Simulator.controllerInputMode, out var bg))
            {
                string inputModeName = m_Simulator.controllerInputMode.ToString();
                Debug.LogError($"Background for the {inputModeName} controller input mode panel does not exist.", this);
                return;
            }

            bg.color = highlightColor;
            m_PreviousControllerInputMode = m_Simulator.controllerInputMode;
        }

        void HighlightOtherControllerInputMode(ControllerInputMode inputMode, Color highlightColor)
        {
            ClearHighlightedControllerPanels(m_OtherControllerInputBgs);

            if (!m_OtherControllerInputBgs.TryGetValue(inputMode, out var bg))
            {
                string inputModeName = inputMode.ToString();
                Debug.LogError($"Background for the {inputModeName} controller input mode panel does not exist.", this);
                return;
            }

            bg.color = highlightColor;
        }

        void HighlightActiveHandInputMode(Color highlightColor)
        {
            ClearHighlightedHandPanels(m_HandExpressionBgs);

            if (m_Simulator.currentHandExpression.name == m_HandPlaybackManager.restingHandExpression.name)
                return;

            var handExpressionName = m_Simulator.currentHandExpression.name;
            if (string.IsNullOrEmpty(handExpressionName))
                return;

            if (!m_HandExpressionBgs.TryGetValue(handExpressionName, out var bg))
            {
                Debug.LogError($"Background for the {handExpressionName} hand expression panel does not exist.", this);
                return;
            }

            bg.color = highlightColor;
        }

        void HighlightOtherHandInputMode(string handExpressionName, Color highlightColor)
        {
            ClearHighlightedHandPanels(m_OtherHandExpressionBgs);

            if (handExpressionName == m_HandPlaybackManager.restingHandExpression.name)
                return;

            if (string.IsNullOrEmpty(handExpressionName))
                return;

            if (!m_OtherHandExpressionBgs.TryGetValue(handExpressionName, out var bg))
            {
                Debug.LogError($"Background for the {handExpressionName} hand expression panel does not exist.", this);
                return;
            }

            bg.color = highlightColor;
        }

        void ClearActiveInputModePanels()
        {
            m_BothControllersPanel.SetActive(false);
            m_LeftControllerPanel.SetActive(false);
            m_RightControllerPanel.SetActive(false);
            m_BothHandsPanel.SetActive(false);
            m_LeftHandPanel.SetActive(false);
            m_RightHandPanel.SetActive(false);
            m_HMDPanel.SetActive(false);
        }

        void ClearOtherInputModePanels()
        {
            m_LeftOtherControllerPanel.SetActive(false);
            m_RightOtherControllerPanel.SetActive(false);
            m_LeftOtherHandPanel.SetActive(false);
            m_RightOtherHandPanel.SetActive(false);
        }

        void ClearActiveGeneralInputPanels()
        {
            m_TranslateForwardPanel.SetActive(false);
            m_TranslateBackwardPanel.SetActive(false);
            m_TranslateUpPanel.SetActive(false);
            m_TranslateDownPanel.SetActive(false);
            m_TranslateLeftPanel.SetActive(false);
            m_TranslateRightPanel.SetActive(false);
            m_RotateUpPanel.SetActive(false);
            m_RotateDownPanel.SetActive(false);
            m_RotateLeftPanel.SetActive(false);
            m_RotateRightPanel.SetActive(false);
        }

        void ClearOtherActiveInputPanels()
        {
            m_OtherTriggerPanel.SetActive(false);
            m_OtherGripPanel.SetActive(false);
            m_OtherPrimaryPanel.SetActive(false);
            m_OtherSecondaryPanel.SetActive(false);
            m_OtherMenuPanel.SetActive(false);
            m_OtherPrimary2DAxisClickPanel.SetActive(false);
            m_OtherSecondary2DAxisClickPanel.SetActive(false);
            m_OtherPrimary2DAxisTouchPanel.SetActive(false);
            m_OtherSecondary2DAxisTouchPanel.SetActive(false);
            m_OtherPrimaryTouchPanel.SetActive(false);
            m_OtherSecondaryTouchPanel.SetActive(false);

            foreach (var panel in m_OtherHandExpressionPanels.Values)
            {
                panel.SetActive(false);
            }
        }

        void ClearHighlightedHandPanels(Dictionary<string, Image> bgs)
        {
            foreach (var bg in bgs.Values)
            {
                bg.color = k_DefaultPanelColor;
            }
        }

        void ClearHighlightedControllerPanels(Dictionary<ControllerInputMode, Image> bgs)
        {
            foreach (var bg in bgs.Values)
            {
                bg.color = k_DefaultPanelColor;
            }
        }
    }
}
