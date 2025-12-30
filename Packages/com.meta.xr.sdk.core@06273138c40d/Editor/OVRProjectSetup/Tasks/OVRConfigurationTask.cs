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
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Reflection;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static OVRProjectSetupDrawer.Styles;
using static OVRProjectSetupDrawer.Styles.Contents;
using Styles = Meta.XR.Editor.UserInterface.Styles;
using Utils = Meta.XR.Editor.UserInterface.Utils;

[Reflection]
internal class OVRConfigurationTask : IIdentified
{
#if UNITY_XR_CORE_UTILS
    [Reflection(AssemblyTypeReference = typeof(Unity.XR.CoreUtils.Editor.BuildValidator), TypeName = "Unity.XR.CoreUtils.Editor.BuildValidator", Name = "s_PlatformRules")]
    private static readonly FieldInfoHandle<Dictionary<BuildTargetGroup, List<Unity.XR.CoreUtils.Editor.BuildValidationRule>>> UnityValidatorRulesDict = new();
#endif

    internal static readonly string ConsoleLinkHref = "OpenProjectSetupTool";
    private static readonly GUIContent FixButtonContent = new("Fix", "Fix with recommended settings");
    private static readonly GUIContent ApplyButtonContent = new("Apply", "Apply the recommended settings");

    private static readonly GUIContent MarkAsFixedButtonContent =
        new("Mark as Fixed", "Mark this as fixed. It will no longer be reported as a problem.");

    private static readonly GUIContent UnmarkAsFixedButtonContent =
        new("Unmark as Fixed", "Unmark this as fixed. It will go back under open items.");

    private static readonly GUIContent ShowGuidedSetupButtonContent =
        new("How to setup", "Show how to manually set this up.");

    private static readonly GUIContent DocumentationButtonContent =
        new("Documentation", "Open documentation for this feature");

    public Hash128 Uid { get; }
    public string Id => Uid.ToString();

    public OVRProjectSetup.TaskGroup Group { get; }

    public OVRProjectSetup.TaskTags Tags { get; }
    public BuildTargetGroup Platform { get; }

    public OptionalLambdaType<BuildTargetGroup, bool> Valid { get; }
    public OptionalLambdaType<BuildTargetGroup, OVRProjectSetup.TaskLevel> Level { get; }
    public OptionalLambdaType<BuildTargetGroup, string> Message { get; }
    public OptionalLambdaType<BuildTargetGroup, string> FixMessage { get; }
    public OptionalLambdaType<BuildTargetGroup, string> URL { get; }
    public OptionalLambdaType<BuildTargetGroup, UPSTGuidedSetup> ManualSetup { get; }
    public OVRConfigurationTaskSourceCode SourceCode { get; set; }

    private Func<BuildTargetGroup, bool> _isDone;

    public Func<BuildTargetGroup, bool> IsDone
    {
        get => GetDoneState;
        private set => _isDone = value;
    }

    public Action<BuildTargetGroup> FixAction { get; }

    public Func<BuildTargetGroup, Task> AsyncFixAction { get; }

    public bool FixAutomatic { get; }

    private readonly Dictionary<BuildTargetGroup, Setting<bool>> _ignoreSettings = new();

    private readonly Dictionary<BuildTargetGroup, Setting<bool>> _markedAsFixedSettings = new();

    private readonly Dictionary<BuildTargetGroup, bool> _isDoneCache = new();

    private bool _isFixing;

    public OVRConfigurationTask(
        OVRProjectSetup.TaskGroup group,
        BuildTargetGroup platform,
        Func<BuildTargetGroup, bool> isDone,
        Action<BuildTargetGroup> fix,
        OptionalLambdaType<BuildTargetGroup, OVRProjectSetup.TaskLevel> level,
        OptionalLambdaType<BuildTargetGroup, string> message,
        OptionalLambdaType<BuildTargetGroup, string> fixMessage,
        OptionalLambdaType<BuildTargetGroup, string> url,
        OptionalLambdaType<BuildTargetGroup, UPSTGuidedSetup> manualSetup,
        OptionalLambdaType<BuildTargetGroup, bool> valid,
        bool fixAutomatic)
        :
        this(group, OVRProjectSetup.TaskTags.None, platform, isDone, fix, null, level, message, fixMessage, url, manualSetup,
            valid, fixAutomatic)
    {
    }

    public OVRConfigurationTask(
        OVRProjectSetup.TaskGroup group,
        BuildTargetGroup platform,
        Func<BuildTargetGroup, bool> isDone,
        Func<BuildTargetGroup, Task> asyncFix,
        OptionalLambdaType<BuildTargetGroup, OVRProjectSetup.TaskLevel> level,
        OptionalLambdaType<BuildTargetGroup, string> message,
        OptionalLambdaType<BuildTargetGroup, string> fixMessage,
        OptionalLambdaType<BuildTargetGroup, string> url,
        OptionalLambdaType<BuildTargetGroup, UPSTGuidedSetup> manualSetup,
        OptionalLambdaType<BuildTargetGroup, bool> valid,
        bool fixAutomatic)
        :
        this(group, OVRProjectSetup.TaskTags.None, platform, isDone, null, asyncFix, level, message, fixMessage, url, manualSetup,
            valid, fixAutomatic)
    {
    }

    public OVRConfigurationTask(
        OVRProjectSetup.TaskGroup group,
        OVRProjectSetup.TaskTags tags,
        BuildTargetGroup platform,
        Func<BuildTargetGroup, bool> isDone,
        Action<BuildTargetGroup> fix,
        Func<BuildTargetGroup, Task> asyncFix,
        OptionalLambdaType<BuildTargetGroup, OVRProjectSetup.TaskLevel> level,
        OptionalLambdaType<BuildTargetGroup, string> message,
        OptionalLambdaType<BuildTargetGroup, string> fixMessage,
        OptionalLambdaType<BuildTargetGroup, string> url,
        OptionalLambdaType<BuildTargetGroup, UPSTGuidedSetup> manualSetup,
        OptionalLambdaType<BuildTargetGroup, bool> valid,
        bool fixAutomatic)
    {
        Platform = platform;
        Group = group;
        IsDone = isDone;
        FixAction = fix;
        AsyncFixAction = asyncFix;
        Level = level;
        Message = message;
        Tags = tags;
        FixAutomatic = fixAutomatic;

        // If parameters are null, we're creating a OptionalLambdaType that points to default values
        // We don't want a null OptionalLambdaType, but we may be okay with an OptionalLambdaType containing a null value
        // For the URL for instance
        // Mandatory parameters will be checked on the Validate method down below
        URL = url ?? new OptionalLambdaTypeWithoutLambda<BuildTargetGroup, string>(null);
        ManualSetup = manualSetup ?? new OptionalLambdaTypeWithoutLambda<BuildTargetGroup, UPSTGuidedSetup>(null);
        FixMessage = fixMessage ?? new OptionalLambdaTypeWithoutLambda<BuildTargetGroup, string>(null);
        Valid = valid ?? new OptionalLambdaTypeWithoutLambda<BuildTargetGroup, bool>(true);

        // We may want to throw in case of some invalid parameters
        Validate();

        var hash = new Hash128();
        hash.Append(Message.Default);
        Uid = hash;

        SourceCode = new OVRConfigurationTaskSourceCode(this);
    }

    private void Validate()
    {
        if (Group == OVRProjectSetup.TaskGroup.All)
        {
            throw new ArgumentException(
                $"[{nameof(OVRConfigurationTask)}] {nameof(OVRProjectSetup.TaskGroup.All)} is not meant to be used as a {nameof(OVRProjectSetup.TaskGroup)} type");
        }

        if (!Tags.HasFlag(OVRProjectSetup.TaskTags.ManuallyFixable))
        {
            if (_isDone == null)
            {
                throw new ArgumentNullException(nameof(_isDone), "isDone should not be null");
            }
        }
        else
        {
            if (FixAction != null || AsyncFixAction != null)
            {
                throw new ArgumentException("Fix actions should be null for manually fixable tasks");
            }

            if (_isDone != null)
            {
                throw new ArgumentException(nameof(_isDone), "isDone should be null");
            }
        }

        // Ensure only one type of fix action is provided
        if (FixAction != null && AsyncFixAction != null)
        {
            throw new ArgumentException("Cannot provide both sync and async fix actions. Use only one.");
        }

        if (Level == null)
        {
            throw new ArgumentNullException(nameof(Level));
        }

        if (Message == null || !Message.Valid || string.IsNullOrEmpty(Message.Default))
        {
            throw new ArgumentNullException(nameof(Message));
        }
    }

    public void InvalidateCache(BuildTargetGroup buildTargetGroup)
    {
        Level.InvalidateCache(buildTargetGroup);
        Message.InvalidateCache(buildTargetGroup);
        URL.InvalidateCache(buildTargetGroup);
        Valid.InvalidateCache(buildTargetGroup);
        _isDoneCache.Remove(buildTargetGroup);
    }

    public bool IsIgnored(BuildTargetGroup buildTargetGroup)
    {
        return GetIgnoreSetting(buildTargetGroup).Value;
    }

    public void SetIgnored(BuildTargetGroup buildTargetGroup, bool ignored)
    {
        GetIgnoreSetting(buildTargetGroup).SetValue(ignored);
        OnIgnore(buildTargetGroup, ignored);
    }

#if UNITY_XR_CORE_UTILS
    private void RemoveFromBuildValidator(BuildTargetGroup buildTargetGroup)
    {
        try
        {
            if (!UnityValidatorRulesDict.Valid)
            {
                return;
            }

            var dict = UnityValidatorRulesDict.Get(null);
            if (dict == null || !dict.TryGetValue(buildTargetGroup, out var rules))
            {
                return;
            }

            var message = Message.GetValue(buildTargetGroup);
            var ruleToRemove = rules.Find(rule => rule.Message == message);
            if (ruleToRemove != null)
            {
                rules.Remove(ruleToRemove);
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }
#endif

    public bool IsMarkedAsFixed(BuildTargetGroup buildTargetGroup)
    {
        return GetMarkedAsFixedSetting(buildTargetGroup).Value;
    }

    public void SetMarkedAsFixed(BuildTargetGroup buildTargetGroup, bool markedAsFixed)
    {
        GetMarkedAsFixedSetting(buildTargetGroup).SetValue(markedAsFixed);
    }

    public bool Fix(BuildTargetGroup buildTargetGroup)
    {
        OVRProjectSetup.ToolDescriptor.Usage.RecordUsage();

        var previousResult = IsDone(buildTargetGroup);

        if (previousResult)
        {
            return true; // Task is already fixed.
        }

        var fixEvent = OVRTelemetry.Start(OVRProjectSetupTelemetryEvent.EventTypes.Fix);
        try
        {
            FixAction(buildTargetGroup);
            if (Tags.HasFlag(OVRProjectSetup.TaskTags.RegenerateAndroidManifest))
            {
                OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: Application.isBatchMode);
            }
        }
        catch (OVRConfigurationTaskException exception)
        {
            Debug.LogWarning(
                $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Failed to fix task \"{Message.GetValue(buildTargetGroup)}\" : {exception}");
            fixEvent.SetResult(OVRPlugin.Qpl.ResultType.Fail);
        }

        InvalidateCache(buildTargetGroup);
        var currentResult = IsDone(buildTargetGroup);

        if (currentResult)
        {
            var fixMessage = FixMessage.GetValue(buildTargetGroup);
            Debug.Log(
                fixMessage != null
                    ? $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Fixed task \"{Message.GetValue(buildTargetGroup)}\" : {fixMessage}"
                    : $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Fixed task \"{Message.GetValue(buildTargetGroup)}\"");
        }
        else
        {
            fixEvent.SetResult(OVRPlugin.Qpl.ResultType.Cancel);
        }


        fixEvent
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Uid, Uid.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Level,
                Level.GetValue(buildTargetGroup).ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Group, Group.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.BuildTargetGroup, buildTargetGroup.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Value, currentResult ? "true" : "false")
            .Send();

        return currentResult;
    }

    public async Task<bool> FixAsync(BuildTargetGroup buildTargetGroup)
    {
        OVRProjectSetup.ToolDescriptor.Usage.RecordUsage();

        var previousResult = IsDone(buildTargetGroup);

        if (previousResult)
        {
            return true; // Task is already fixed.
        }

        var fixEvent = OVRTelemetry.Start(OVRProjectSetupTelemetryEvent.EventTypes.Fix);
        try
        {
            await AsyncFixAction(buildTargetGroup);
            if (Tags.HasFlag(OVRProjectSetup.TaskTags.RegenerateAndroidManifest))
            {
                OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: Application.isBatchMode);
            }
        }
        catch (OVRConfigurationTaskException exception)
        {
            Debug.LogWarning(
                $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Failed to fix task \"{Message.GetValue(buildTargetGroup)}\" : {exception}");
            fixEvent.SetResult(OVRPlugin.Qpl.ResultType.Fail);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Failed to fix task \"{Message.GetValue(buildTargetGroup)}\" : {exception}");
            fixEvent.SetResult(OVRPlugin.Qpl.ResultType.Fail);
        }

        InvalidateCache(buildTargetGroup);
        var currentResult = IsDone(buildTargetGroup);

        if (currentResult)
        {
            var fixMessage = FixMessage.GetValue(buildTargetGroup);
            Debug.Log(
                fixMessage != null
                    ? $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Fixed task \"{Message.GetValue(buildTargetGroup)}\" : {fixMessage}"
                    : $"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Fixed task \"{Message.GetValue(buildTargetGroup)}\"");
        }
        else
        {
            fixEvent.SetResult(OVRPlugin.Qpl.ResultType.Cancel);
        }


        fixEvent
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Uid, Uid.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Level,
                Level.GetValue(buildTargetGroup).ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Group, Group.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.BuildTargetGroup, buildTargetGroup.ToString())
            .AddAnnotation(OVRProjectSetupTelemetryEvent.AnnotationTypes.Value, currentResult ? "true" : "false")
            .Send();

        return currentResult;
    }

    private Setting<bool> GetIgnoreSetting(BuildTargetGroup buildTargetGroup)
    {
        if (_ignoreSettings.TryGetValue(buildTargetGroup, out var item))
        {
            return item;
        }

        item = new OVRProjectSetupSettings.SettingBool
        {
            Owner = this,
            Uid = $"Ignored.{buildTargetGroup.ToString()}",
            OldKey = $"OVRProjectSetup.{GetType().Name}.{Uid}.Ignored.{buildTargetGroup.ToString()}",
            Default = false,
            Label = "Ignore"
        };

        _ignoreSettings.Add(buildTargetGroup, item);

        return item;
    }

    private Setting<bool> GetMarkedAsFixedSetting(BuildTargetGroup buildTargetGroup)
    {
        if (!_markedAsFixedSettings.TryGetValue(buildTargetGroup, out var item))
        {
            item = new OVRProjectSetupSettings.SettingBool
            {
                Owner = this,
                Uid = $"MarkedAsFixed.{buildTargetGroup.ToString()}",
                Default = false,
                Label = "Marked as Fixed"
            };

            _markedAsFixedSettings.Add(buildTargetGroup, item);
        }

        return item;
    }

    internal void LogMessage(BuildTargetGroup buildTargetGroup)
    {
        var logMessage = GetFullLogMessage(buildTargetGroup);

        switch (Level.GetValue(buildTargetGroup))
        {
            case OVRProjectSetup.TaskLevel.Optional:
                break;
            case OVRProjectSetup.TaskLevel.Recommended:
                Debug.LogWarning(logMessage);
                break;
            case OVRProjectSetup.TaskLevel.Required:
                if (OVRProjectSetup.RequiredThrowErrors.Value)
                {
                    Debug.LogError(logMessage);
                }
                else
                {
                    Debug.LogWarning(logMessage);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal string GetFullLogMessage(BuildTargetGroup buildTargetGroup)
    {
        return $"{GetLogMessage(buildTargetGroup)}.\nYou can fix this by going to " +
               $"<a href=\"{ConsoleLinkHref}\">Edit > Project Settings > {OVRProjectSetupSettingsProvider.SettingsName}</a>";
    }

    internal string GetLogMessage(BuildTargetGroup buildTargetGroup)
    {
        return $"[{Group}] {Message.GetValue(buildTargetGroup)}";
    }

    private bool GetDoneState(BuildTargetGroup buildTargetGroup)
    {
        if (Tags.HasFlag(OVRProjectSetup.TaskTags.ManuallyFixable))
        {
            return IsMarkedAsFixed(buildTargetGroup);
        }

        if (_isDoneCache.TryGetValue(buildTargetGroup, out var cachedState))
        {
            return cachedState;
        }

        var result = _isDone(buildTargetGroup);
        _isDoneCache[buildTargetGroup] = result;
        return result;
    }

    internal void Draw(BuildTargetGroup buildTargetGroup, Action<OVRConfigurationTaskProcessor> onAfterFixed)
    {
        var ignored = IsIgnored(buildTargetGroup);
        var cannotBeFixed = IsDone(buildTargetGroup) ||
                            OVRProjectSetup.ProcessorQueue.BusyWith(OVRConfigurationTaskProcessor.ProcessorType.Fixer);
        var disabled = cannotBeFixed || ignored;

        // Note : We're not using scopes, because in this very case, we've got a cross of scopes
        EditorGUI.BeginDisabledGroup(disabled);
        var clickArea = EditorGUILayout.BeginHorizontal(GUIStyles.ListLabel);

        // Icon
        var (icon, color) = GetTaskIcon(buildTargetGroup);
        using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, color))
        {
            GUILayout.Label(icon, Styles.GUIStyles.IconStyle);
        }

        // Message
        GUILayout.Label(new GUIContent(Message.GetValue(buildTargetGroup)), GUIStyles.Wrap);

        EditorGUI.EndDisabledGroup();

        if (FixAction != null || AsyncFixAction != null)
        {
            EditorGUI.BeginDisabledGroup(cannotBeFixed);
            var content = Level.GetValue(buildTargetGroup) == OVRProjectSetup.TaskLevel.Required
                ? FixButtonContent
                : ApplyButtonContent;

            var fixMessage = FixMessage.GetValue(buildTargetGroup);
            var tooltip = fixMessage != null ? $"{content.tooltip} :\n{fixMessage}" : content.tooltip;

            var buttonText = _isFixing
                ? Level.GetValue(buildTargetGroup) == OVRProjectSetup.TaskLevel.Required
                    ? "Fixing..."
                    : "Applying..."
                : content.text;

            content = new GUIContent(buttonText, tooltip);
            if (GUILayout.Button(content, GUIStyles.FixButton))
            {
                _isFixing = true;
                OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.Fixed);
                OVRProjectSetup.FixTask(buildTargetGroup, this, blocking: false, onCompleted: processor =>
                {
                    _isFixing = false;
                    onAfterFixed?.Invoke(processor);
                });
            }

            EditorGUI.EndDisabledGroup();
        }
        else if (Tags.HasFlag(OVRProjectSetup.TaskTags.ManuallyFixable))
        {
            var manualSetup = ManualSetup?.GetValue(buildTargetGroup);
            if (manualSetup != null && GUILayout.Button(ShowGuidedSetupButtonContent, GUIStyles.MarkAsFixedButton))
            {
                manualSetup.ShowWindow(Origins.Component, true);
            }

            if (!IsMarkedAsFixed(buildTargetGroup) &&
                GUILayout.Button(MarkAsFixedButtonContent, GUIStyles.MarkAsFixedButton))
            {
                SetMarkedAsFixed(buildTargetGroup, true);
                OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction
                    .MarkedAsFixed);
            }

            if (IsMarkedAsFixed(buildTargetGroup) &&
                GUILayout.Button(UnmarkAsFixedButtonContent, GUIStyles.UnmarkAsFixedButton))
            {
                SetMarkedAsFixed(buildTargetGroup, false);
                OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction
                    .UnmarkedAsFixed);
            }
        }

        var current = Event.current;
        if (GUILayout.Button("", EditorStyles.foldoutHeaderIcon, GUILayout.Width(16.0f))
            || (clickArea.Contains(current.mousePosition) && current.type == EventType.ContextClick))
        {
            ShowItemMenu(buildTargetGroup);
            if (current.type == EventType.ContextClick)
            {
                current.Use();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private (TextureContent, Color) GetTaskIcon(BuildTargetGroup buildTargetGroup)
    {
        return IsDone(buildTargetGroup)
            ? (TestPassedIcon, SuccessColor)
            : GetTaskIcon(Level.GetValue(buildTargetGroup));
    }

    public static (TextureContent, Color) GetTaskIcon(OVRProjectSetup.TaskLevel? taskLevel)
    {
        return taskLevel switch
        {
            OVRProjectSetup.TaskLevel.Required => (ErrorIcon, ErrorColor),
            OVRProjectSetup.TaskLevel.Recommended => (WarningIcon, WarningColor),
            OVRProjectSetup.TaskLevel.Optional => (InfoIcon, InfoColor),
            _ => (TestPassedIcon, SuccessColor)
        };
    }

    private void ShowItemMenu(BuildTargetGroup buildTargetGroup)
    {
        var menu = new GenericMenu();
        var hasDocumentation = !string.IsNullOrEmpty(URL.GetValue(buildTargetGroup));
        if (hasDocumentation)
        {
            menu.AddItem(new GUIContent("Documentation"), false, OnDocumentation,
                new object[] { buildTargetGroup, this });
        }

        var hasSourceCode = SourceCode.Valid;
        if (hasSourceCode)
        {
            menu.AddItem(new GUIContent("Go to Source Code"), false, OnGoToSourceCode,
                new object[] { buildTargetGroup, this });
        }

        GetIgnoreSetting(buildTargetGroup).DrawForMenu(menu, Origins.Self, this, () =>
        {
            OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.Ignored);
            OnIgnore(buildTargetGroup, IsIgnored(buildTargetGroup));
        });

        menu.ShowAsContext();
    }


    private void OnIgnore(BuildTargetGroup buildTargetGroup, bool ignored)
    {
#if UNITY_XR_CORE_UTILS
        if (ignored)
        {
            RemoveFromBuildValidator(buildTargetGroup);
        }
#endif
    }

    private static void OnDocumentation(object args)
    {
        OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction
            .WentToDocumentation);

        ReadContextMenuArguments(args, out var buildTargetGroup, out var task);
        var url = task?.URL.GetValue(buildTargetGroup);

        Application.OpenURL(url);
    }

    private static void OnGoToSourceCode(object args)
    {
        OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.WentToSource);

        ReadContextMenuArguments(args, out _, out var task);
        task?.SourceCode.Open();
    }

    private static void ReadContextMenuArguments(
        object arg,
        out BuildTargetGroup buildTargetGroup,
        out OVRConfigurationTask task)
    {
        var args = arg as object[];
        buildTargetGroup = args != null ? (BuildTargetGroup)args[0] : BuildTargetGroup.Unknown;
        task = args?[1] as OVRConfigurationTask;
    }

#if UNITY_XR_CORE_UTILS
    internal Unity.XR.CoreUtils.Editor.BuildValidationRule ToValidationRule(BuildTargetGroup platform)
    {
        if (FixAction == null)
        {
            return null;
        }

        if (Tags.HasFlag(OVRProjectSetup.TaskTags.HeavyProcessing))
        {
            return null;
        }

        if (platform == BuildTargetGroup.Unknown)
        {
            return null;
        }

        var validationRule = new Unity.XR.CoreUtils.Editor.BuildValidationRule
        {
            IsRuleEnabled = () => Valid.GetValue(platform),
            Category = Group.ToString(),
            Message = Message.GetValue(platform),
            CheckPredicate = () => IsDone(platform),
            FixIt = () => FixAction(platform),
            FixItAutomatic = FixAutomatic,
            FixItMessage = FixMessage.GetValue(platform),
            HelpText = null,
            HelpLink = null,
            SceneOnlyValidation = false,
            OnClick = null,
            Error = Level.GetValue(platform) == OVRProjectSetup.TaskLevel.Required
        };
        return validationRule;
    }
#endif
}
