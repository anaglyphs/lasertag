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

using System.Globalization;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.ToolingSupport;
using UnityEditor;
using UnityEngine.UIElements;

internal class OVRProjectSetupSettingsProvider : SettingsProvider
{
    public enum Interaction
    {
        None,
        WentToDocumentation,
        WentToSource,
        Fixed,
        Ignored,
        MarkedAsFixed,
        UnmarkedAsFixed,
    }

    public const string SettingsName = OVREditorUtils.MetaXRPublicName;
    public static readonly string SettingsPath = $"Project/{SettingsName}";

    private OVRProjectSetupDrawer _ovrProjectSetupDrawer;
    private OVRProjectSetupDrawer OvrProjectSetupDrawer => _ovrProjectSetupDrawer ??= new OVRProjectSetupDrawer();
    private static Origins? _lastOrigin = null;
    private static Interaction _lastInteraction = Interaction.None;
    private static bool _activated = false;

    public static double OpenTimestamp { get; set; }
    public static double TimeSpent => EditorApplication.timeSinceStartup - OpenTimestamp;
    public static OVRTelemetryMarker? InteractionFlowEvent { get; set; }

    [SettingsProvider]
    public static SettingsProvider Create()
    {
        return new OVRProjectSetupSettingsProvider(SettingsPath, SettingsScope.Project);
    }

    internal static void SetNewInteraction(Interaction interaction)
    {
        if (interaction > _lastInteraction)
        {
            InteractionFlowEvent = InteractionFlowEvent?.AddPoint(OVRProjectSetupTelemetryEvent.MarkerPoints.Interact);
            _lastInteraction = interaction;
        }
    }

    internal static void ResetInteraction()
    {
        InteractionFlowEvent = null;
        _lastInteraction = Interaction.None;
        _lastOrigin = null;
        _activated = false;
        OpenTimestamp = 0.0;
    }

    private OVRProjectSetupSettingsProvider(string path,
        SettingsScope scopes)
        : base(path, scopes)
    {
    }

    public override void OnActivate(string searchContext, VisualElement rootElement)
    {
        if (_activated)
        {
            return;
        }

        OVRProjectSetup.ProcessorQueue.OnProcessorCompleted -= OnProcessorCompleted;
        OVRProjectSetup.ProcessorQueue.OnProcessorCompleted += OnProcessorCompleted;

        OpenTimestamp = EditorApplication.timeSinceStartup;
        _activated = true;
        _lastOrigin = _lastOrigin ?? Origins.ProjectSettings;

        var unifiedEvent = new OVRPlugin.UnifiedEventData(OVRProjectSetupTelemetryEvent.FalcoEventNames.Open)
        {
            isEssential = OVRPlugin.Bool.True,
            productType = OVRPlugin.ProductType.Pst
        };
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.BuildTargetGroup,
            EditorUserBuildSettings.selectedBuildTargetGroup.ToString());
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.Origin, _lastOrigin.ToString());
        unifiedEvent.Send();

        InteractionFlowEvent = InteractionFlowEvent?.AddPoint(OVRProjectSetupTelemetryEvent.MarkerPoints.Open)
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Origin, _lastOrigin.ToString());
    }

    public override void OnDeactivate()
    {
        if (TimeSpent < 0.1)
        {
            // Ignore the entire interaction as it was too short to be meaningful
            return;
        }

        if (!_activated)
        {
            return;
        }

        OVRProjectSetup.ProcessorQueue.OnProcessorCompleted -= OnProcessorCompleted;

        var unifiedEvent = new OVRPlugin.UnifiedEventData(OVRProjectSetupTelemetryEvent.FalcoEventNames.Close)
        {
            isEssential = OVRPlugin.Bool.False,
            productType = OVRPlugin.ProductType.Pst
        };
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.BuildTargetGroup,
            EditorUserBuildSettings.selectedBuildTargetGroup.ToString());
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.Origin, _lastOrigin.ToString());
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.TimeSpent,
            TimeSpent.ToString(CultureInfo.InvariantCulture));
        unifiedEvent.SetMetadata(OVRProjectSetupTelemetryEvent.AnnotationTypes.Interaction, _lastInteraction.ToString());
        unifiedEvent.Send();

        InteractionFlowEvent = InteractionFlowEvent?.AddPoint(OVRProjectSetupTelemetryEvent.MarkerPoints.Close)
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Interaction, _lastInteraction.ToString())
            .Send();

        ResetInteraction();
    }

    public override void OnTitleBarGUI()
    {
        OVRProjectSetup.ToolDescriptor.DrawHeaderFromSettingProvider(Origins.Self);
    }

    public override void OnGUI(string searchContext)
    {
        OvrProjectSetupDrawer.OnGUI();
    }

    private void OnProcessorCompleted(OVRConfigurationTaskProcessor processor)
    {
        SettingsService.RepaintAllSettingsWindow();
    }

    public static void OpenSettingsWindow(Origins origin)
    {
        _lastOrigin = origin;
        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        EditorUserBuildSettings.selectedBuildTargetGroup = buildTargetGroup;
        SettingsService.OpenProjectSettings(SettingsPath);
    }
}
