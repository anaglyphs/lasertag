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
using System.Linq;
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using Meta.XR.ImmersiveDebugger.Utils;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    /// <summary>
    /// This is a <see cref="MonoBehaviour"/> for the most high-level Panel UI of Immersive Debugger.
    /// Containing UI elements of all the panels (for now 3 panels - debug bar, inspector, console) and
    /// performs registration of the inter-links and control buttons between the panels.
    /// For more info about Immersive Debugger, check out the [official doc](https://developer.oculus.com/documentation/unity/immersivedebugger-overview)
    /// </summary>
    public class DebugInterface : Interface
    {
        private DebugBar _bar;

        private Toggle _showAllButton;
        private Toggle _followButton;
        private Toggle _rotateButton;
        private Toggle _opacityButton;
        private Toggle _distanceButton;
        private InspectorPanel _inspectorPanel;
        private Console _console;
        private int _distanceToggleIndex;
        private readonly int _distanceOptionSize = Enum.GetValues(typeof(RuntimeSettings.DistanceOption)).Length;

        private List<DebugPanel> _allPanels = new List<DebugPanel>();
        private ValueContainer<float> _panelPositionConstants;

        protected override bool FollowOverride
        {
            get => _followButton.State;
            set => _followButton.State = value;
        }

        protected override bool RotateOverride
        {
            get => _rotateButton.State;
            set => _rotateButton.State = value;
        }

        public bool OpacityOverride
        {
            get => _opacityButton.State;
            set
            {
                _opacityButton.State = value;

                foreach (var child in Children)
                {
                    if (child is InteractableController controller)
                    {
                        SetTransparencyRecursive(controller, !OpacityOverride);
                    }
                }
            }
        }

        internal void SetTransparencyRecursive(Controller controller, bool transparent)
        {
            controller.Transparent = transparent;
            if (controller.Children == null) return;

            foreach (var child in controller.Children)
            {
                SetTransparencyRecursive(child, transparent);
            }
        }

        internal override void Awake()
        {
            base.Awake();

            // Hide by default early on during initialization
            Hide();

            _panelPositionConstants = ValueContainer<float>.Load("PanelPositionConstants");
            _inspectorPanel = Append<InspectorPanel>("inspectors");
            _inspectorPanel.LayoutStyle = Style.Load<LayoutStyle>("InspectorPanel");
            _inspectorPanel.BackgroundStyle = Style.Load<ImageStyle>("PanelBackground");
            _inspectorPanel.Title = "Inspectors";
            _inspectorPanel.Icon = Resources.Load<Texture2D>("Textures/inspectors_icon");

            _console = Append<Console>("console");
            _console.LayoutStyle = Style.Load<LayoutStyle>("ConsolePanel");
            _console.BackgroundStyle = Style.Load<ImageStyle>("PanelBackground");
            _console.Title = "Console";
            _console.Icon = Resources.Load<Texture2D>("Textures/console_icon");

            _distanceToggleIndex = (int)RuntimeSettings.Instance.PanelDistance;

            _bar = Append<DebugBar>("bar");
            _bar.LayoutStyle = Style.Load<LayoutStyle>("Bar");
            _bar.BackgroundStyle = Style.Load<ImageStyle>("BarBackground");
            _bar.SphericalCoordinates = new Vector3(0.7f, 0.0f, -0.5f);
            _bar.RegisterPanel(_console);
            _bar.RegisterPanel(_inspectorPanel);

            // Initialize dynamic panel positioning system
            InitializePanelPositioning();

            _opacityButton = _bar.RegisterControl("opacity", Resources.Load<Texture2D>("Textures/opacity_icon"),
                (() => OpacityOverride = !OpacityOverride));
            _followButton = _bar.RegisterControl("followMove", Resources.Load<Texture2D>("Textures/move_icon"), ToggleFollowTranslation);
            _rotateButton = _bar.RegisterControl("followRotation", Resources.Load<Texture2D>("Textures/rotate_icon"), ToggleFollowRotation);
            _distanceButton = _bar.RegisterControl("setDistance", Resources.Load<Texture2D>("Textures/shift_icon"), ToggleDistances);
            _distanceButton.State = true;

            var runtimeSettings = RuntimeSettings.Instance;
            FollowOverride = runtimeSettings.FollowOverride;
            RotateOverride = runtimeSettings.RotateOverride;
            OpacityOverride = true;

            if (runtimeSettings.ShowInspectors)
            {
                _inspectorPanel.Show();
            }

            if (runtimeSettings.ShowConsole)
            {
                _console.Show();
            }

            if (runtimeSettings.ImmersiveDebuggerDisplayAtStartup)
            {
                Show();
            }

            var debugManager = DebugManager.Instance;
            if (debugManager != null)
            {
                debugManager.OnUpdateAction += UpdateVisibility;
                debugManager.CustomShouldRetrieveInstanceCondition += IsInspectorPanelVisible;
            }

            DiscoverAndRegisterDynamicPanels();
        }

        private void ToggleDistances()
        {
            _distanceToggleIndex = ++_distanceToggleIndex % _distanceOptionSize;
            UpdateDynamicPanelPositions();
        }

        private void ToggleFollowTranslation()
        {
            FollowOverride = !FollowOverride;
        }

        private void ToggleFollowRotation()
        {
            RotateOverride = !RotateOverride;
        }

        private void UpdateVisibility()
        {
            if (OVRInput.GetDown(RuntimeSettings.Instance.ImmersiveDebuggerToggleDisplayButton))
            {
                ToggleVisibility();
            }
        }

        private void Update()
        {
            var settings = RuntimeSettings.Instance;
            if (OVRInput.GetDown(settings.ToggleFollowTranslationButton))
            {
                ToggleFollowTranslation();
            }

            if (OVRInput.GetDown(settings.ToggleFollowRotationButton))
            {
                ToggleFollowRotation();
            }
        }

        private bool IsInspectorPanelVisible()
        {
            return Visibility && _inspectorPanel is { Visibility: true };
        }

        private void InitializePanelPositioning()
        {
            _allPanels.Add(_console);
            _allPanels.Add(_inspectorPanel);

            foreach (var panel in _allPanels)
            {
                panel.OnVisibilityChangedEvent += OnPanelVisibilityChanged;
            }

            UpdateDynamicPanelPositions();
        }

        private void OnPanelVisibilityChanged(Controller panel)
        {
            if (panel is DebugPanel debugPanel && _allPanels.Contains(debugPanel))
            {
                UpdateDynamicPanelPositions();
            }
        }

        private void UpdateDynamicPanelPositions()
        {
            var visiblePanels = _allPanels.Where(panel => panel.Visibility).ToList();
            var panelCount = visiblePanels.Count;

            if (panelCount == 0) return;

            var positions = CalculateDynamicPositions(panelCount, (RuntimeSettings.DistanceOption)_distanceToggleIndex);

            for (int i = 0; i < visiblePanels.Count; i++)
            {
                var panel = visiblePanels[i];
                var targetPosition = positions[i];

                panel.SphericalCoordinates = targetPosition;
            }
        }

        private List<Vector3> CalculateDynamicPositions(int panelCount, RuntimeSettings.DistanceOption distanceOption)
        {
            var positions = new List<Vector3>();
            // Base position parameters based on distance option
            var baseDistance = distanceOption switch
            {
                RuntimeSettings.DistanceOption.Close => _panelPositionConstants["DistanceClose"],
                RuntimeSettings.DistanceOption.Far => _panelPositionConstants["DistanceFar"],
                _ => _panelPositionConstants["DistanceDefault"]
            };

            var baseZ = _panelPositionConstants["PanelDefaultZ"];
            var panelSpacing = _panelPositionConstants["PanelSpacing"];
            var startY = 0f; // Start position above debug bar

            // Calculate positions based on panel count
            var startYOffset = startY + (panelCount - 1) * panelSpacing / 2f;

            for (int i = 0; i < panelCount; i++)
            {
                var yPosition = startYOffset - (i * panelSpacing);
                positions.Add(new Vector3(baseDistance, yPosition, baseZ));
            }

            return positions;
        }

        /// <summary>
        /// Discover and register dynamic panels using reflection.
        /// This method finds all types that implement IPanelRegistrar and calls their RegisterPanel method.
        /// </summary>
        private void DiscoverAndRegisterDynamicPanels()
        {
            try
            {
                // Get all loaded assemblies
                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        // Find all types that implement IPanelRegistrar
                        var registrarTypes = assembly.GetTypes()
                            .Where(type => typeof(IPanelRegistrar).IsAssignableFrom(type) &&
                                          !type.IsInterface &&
                                          !type.IsAbstract)
                            .ToArray();

                        foreach (var registrarType in registrarTypes)
                        {
                            try
                            {
                                // Create instance and register panel
                                var registrar = Activator.CreateInstance(registrarType) as IPanelRegistrar;
                                registrar?.RegisterPanel(this);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Failed to create instance of panel registrar {registrarType.Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        // Some assemblies might have loading issues, skip them
                        Debug.LogWarning($"Failed to load types from assembly {assembly.FullName}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error processing assembly {assembly.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during dynamic panel discovery: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a debug panel dynamically at runtime.
        /// This method allows external packages to register their panels with the debug interface.
        /// </summary>
        /// <param name="panel">The debug panel to register</param>
        public void RegisterDebugPanel(DebugPanel panel)
        {
            if (panel == null)
            {
                Debug.LogWarning("Attempted to register null debug panel");
                return;
            }

            // Add to our tracking list
            _allPanels.Add(panel);

            // Register with the debug bar
            _bar.RegisterPanel(panel);

            // Set up event handling for dynamic positioning
            panel.OnVisibilityChangedEvent += OnPanelVisibilityChanged;

            // Update positions of all panels
            UpdateDynamicPanelPositions();

            Debug.Log($"Successfully registered dynamic debug panel: {panel.Title}");
        }
    }
}
