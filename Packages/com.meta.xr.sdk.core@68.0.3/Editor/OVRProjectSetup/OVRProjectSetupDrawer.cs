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
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Contents;

internal class OVRProjectSetupDrawer
{
    internal static class Styles
    {
        public static class Constants
        {
            public const float FixButtonWidth = 64.0f;
            public const float FixAllButtonWidth = 80.0f;
            public const float GroupSelectionWidth = 244.0f;
            public const float LabelWidth = 96f;
            public const float TitleLabelWidth = 196f;
        }

        public static class Contents
        {
            public static readonly TextureContent WarningIcon =
                TextureContent.CreateContent("ovr_icon_category_error.png", OVRProjectSetupUtils.ProjectSetupToolIcons);
            public static readonly TextureContent ErrorIcon =
                TextureContent.CreateContent("ovr_icon_category_error.png", OVRProjectSetupUtils.ProjectSetupToolIcons);
            public static readonly TextureContent InfoIcon =
                TextureContent.CreateContent("ovr_icon_category_neutral.png", OVRProjectSetupUtils.ProjectSetupToolIcons);
            public static readonly TextureContent TestPassedIcon =
                TextureContent.CreateContent("ovr_icon_category_success.png", OVRProjectSetupUtils.ProjectSetupToolIcons);
        }

        public class GUIStylesContainer
        {
            internal readonly GUIStyle Wrap = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 5, 1, 1)
            };

            internal readonly GUIStyle IssuesBackground = new GUIStyle("ScrollViewAlt")
            {
            };

            internal readonly GUIStyle ListLabel = new GUIStyle("TV Selection")
            {
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(5, 5, 5, 3),
                margin = new RectOffset(4, 4, 4, 5)
            };

            internal readonly GUIStyle IssuesTitleLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                wordWrap = false,
                stretchWidth = false,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 10, 0, 0)
            };

            internal readonly GUIStyle GenerateReportButton = new GUIStyle(EditorStyles.miniButton)
            {
                margin = new RectOffset(0, 10, 2, 2),
                stretchWidth = false,
            };

            internal readonly GUIStyle FixButton = new GUIStyle(EditorStyles.miniButton)
            {
                margin = new RectOffset(0, 10, 2, 2),
                stretchWidth = false,
                fixedWidth = Constants.FixButtonWidth,
            };

            internal readonly GUIStyle FixAllButton = new GUIStyle(EditorStyles.miniButton)
            {
                margin = new RectOffset(0, 10, 2, 2),
                stretchWidth = false,
                fixedWidth = Constants.FixAllButtonWidth,
            };

            internal readonly GUIStyle InlinedIconStyle = new GUIStyle(EditorStyles.label)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = SmallIconSize,
                fixedHeight = SmallIconSize
            };

            internal readonly GUIStyle IconStyle = new GUIStyle(EditorStyles.label)
            {
                margin = new RectOffset(5, 5, 4, 5),
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = SmallIconSize,
                fixedHeight = SmallIconSize
            };

            internal readonly GUIStyle SubtitleHelpText = new GUIStyle(EditorStyles.miniLabel)
            {
                margin = new RectOffset(10, 0, 0, 0),
                wordWrap = true
            };

            internal readonly GUIStyle NormalStyle = new GUIStyle(EditorStyles.label)
            {
                margin = new RectOffset(10, 0, 0, 0),
                wordWrap = true,
                stretchWidth = false
            };

            internal readonly GUIStyle BoldStyle = new GUIStyle(EditorStyles.label)
            {
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = false,
                wordWrap = true,
                fontStyle = FontStyle.Bold
            };

            internal readonly GUIStyle Foldout = new GUIStyle(EditorStyles.foldoutHeader)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(16, 5, 5, 5),
                fixedHeight = 26.0f
            };

            internal readonly GUIStyle FoldoutHorizontal = new GUIStyle(EditorStyles.label)
            {
                fixedHeight = 26.0f
            };

            internal readonly GUIStyle List = new GUIStyle(EditorStyles.helpBox)
            {
                margin = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(3, 3, 3, 3)
            };
        }

        private static GUIStylesContainer _guiStyles;
        public static GUIStylesContainer GUIStyles => _guiStyles ??= new GUIStylesContainer();
    }

    private readonly OVRProjectSetupSettingBool _showOutstandingItems =
        new OVRProjectSetupUserSettingBool("ShowOutstandingItems", true);

    private readonly OVRProjectSetupSettingBool _showRecommendedItems =
        new OVRProjectSetupUserSettingBool("ShowRecommendedItems", true);

    private readonly OVRProjectSetupSettingBool _showVerifiedItems =
        new OVRProjectSetupUserSettingBool("ShowVerifiedItems", false);

    private readonly OVRProjectSetupSettingBool _showIgnoredItems =
        new OVRProjectSetupUserSettingBool("ShowIgnoredItems", false);

    private static readonly GUIContent Title = new GUIContent(OVRProjectSetupUtils.ProjectSetupToolPublicName);

    private static readonly GUIContent Description =
        new GUIContent("This tool maintains a checklist of required setup tasks as well as best practices to " +
                       "ensure your project is ready to go.\nFollow our suggestions and fixes to quickly setup your project.");

    private static readonly GUIContent SummaryLabel = new GUIContent("Current project status: ");
    private static readonly GUIContent ListTitle = new GUIContent("Checklist");
    private static readonly GUIContent UnsupportedTitle = new GUIContent("Unsupported Platform");

    private static readonly GUIContent Filter =
        new GUIContent("Filter by Group :", "Filters the task to the selected group.");

    private static readonly GUIContent FixButtonContent = new GUIContent("Fix", "Fix with recommended settings");

    private static readonly GUIContent FixAllButtonContent =
        new GUIContent("Fix All", "Fix all the issues from this category");

    private static readonly GUIContent ApplyButtonContent = new GUIContent("Apply", "Apply the recommended settings");

    private static readonly GUIContent ApplyAllButtonContent =
        new GUIContent("Apply All", "Apply the recommended settings for all the items in this category");

    private static readonly GUIContent RefreshTasksButtonContent =
        new GUIContent("Refresh", "Refresh the items in the list");

    private static readonly GUIContent GenerateReportButtonContent =
        new GUIContent("Generate report", "Generate a report of all the issues");

    private const string OutstandingItems = "Outstanding Issues";
    private const string RecommendedItems = "Recommended Items";
    private const string VerifiedItems = "Verified Items";
    private const string IgnoredItems = "Ignored Items";
    private const string OutFolderTitle = "Select output folder";
    private const string ErrorTitle = "Error";
    private const string SuccessTitle = "Success";
    private const string ReportGenerationErrorMessage = "Could not generate the project setup report.";
    private const string ReportGenerationSuccessMessage = "Project setup report generated successfully at:";
    private const string TasksRefreshErrorMessage = "Could not refresh the checklist.";
    private const string TasksRefreshSuccessMessage = "Tasks refreshed successfully.";
    private const string OkButton = "ok";

    // Internals
    private OVRProjectSetup.TaskGroup _selectedTaskGroup;
    private BuildTargetGroup _selectedBuildTargetGroup = BuildTargetGroup.Unknown;
    private Vector2 _scrollViewPos = Vector2.zero;
    private OVRConfigurationTaskUpdaterSummary _lastSummary;

    internal OVRProjectSetupDrawer()
    {
        _selectedTaskGroup = OVRProjectSetup.TaskGroup.All;
    }

    private class BuildTargetSelectionScope : GUI.Scope
    {
        public BuildTargetGroup BuildTargetGroup { get; protected set; }

        public BuildTargetSelectionScope()
        {
            BuildTargetGroup = EditorGUILayout.BeginBuildTargetSelectionGrouping();
            if (BuildTargetGroup == BuildTargetGroup.Unknown)
            {
                BuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            }
        }

        protected override void CloseScope() => EditorGUILayout.EndVertical();
    }

    private TEnumType EnumPopup<TEnumType>(GUIContent content, TEnumType currentValue, Action<TEnumType> onChanged)
        where TEnumType : Enum, IComparable
    {
        var previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Styles.Constants.LabelWidth;
        TEnumType newValue =
            (TEnumType)EditorGUILayout.EnumPopup(content, currentValue, GUILayout.Width(Styles.Constants.GroupSelectionWidth));
        EditorGUIUtility.labelWidth = previousLabelWidth;

        if (!newValue.Equals(currentValue))
        {
            onChanged(newValue);
        }

        return newValue;
    }

    private bool FoldoutWithAdditionalAction(OVRProjectSetupSettingBool key, string label, Rect rect,
        Action inlineAdditionalAction)
    {
        var previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = rect.width - 8;

        bool foldout;
        using (new EditorGUILayout.HorizontalScope(Styles.GUIStyles.FoldoutHorizontal))
        {
            foldout = Foldout(key, label);
            inlineAdditionalAction?.Invoke();
        }

        EditorGUIUtility.labelWidth = previousLabelWidth;
        return foldout;
    }

    private bool Foldout(OVRProjectSetupSettingBool key, string label)
    {
        var currentValue = key.Value;
        var newValue = EditorGUILayout.Foldout(currentValue, label, true, Styles.GUIStyles.Foldout);
        if (newValue != currentValue)
        {
            key.Value = newValue;
        }

        return newValue;
    }

    private (TextureContent, Color) GetTaskIcon(OVRConfigurationTask task, BuildTargetGroup buildTargetGroup)
    {
        return task.IsDone(buildTargetGroup) ? (Styles.Contents.TestPassedIcon, SuccessColor) : GetTaskIcon(task.Level.GetValue(buildTargetGroup));
    }

    private (TextureContent, Color) GetTaskIcon(OVRProjectSetup.TaskLevel? taskLevel)
    {
        return taskLevel switch
        {
            OVRProjectSetup.TaskLevel.Required => (Styles.Contents.ErrorIcon, ErrorColor),
            OVRProjectSetup.TaskLevel.Recommended => (Styles.Contents.WarningIcon, WarningColor),
            OVRProjectSetup.TaskLevel.Optional => (Styles.Contents.InfoIcon, InfoColor),
            _ => (Styles.Contents.TestPassedIcon, SuccessColor)
        };
    }

    private string GenerateReport(BuildTargetGroup buildTargetGroup, string outputPath)
    {
        if (_lastSummary == null)
        {
            OVRProjectSetup.UpdateTasks(buildTargetGroup, logMessages: OVRProjectSetup.LogMessages.Disabled,
                blocking: true, onCompleted: processor =>
                {
                    var updater = processor as OVRConfigurationTaskUpdater;
                    _lastSummary = updater?.Summary;
                });
            return _lastSummary?.GenerateReport(outputPath);
        }

        return _lastSummary.GenerateReport(outputPath);
    }

    private void UpdateTasks(BuildTargetGroup buildTargetGroup)
    {
        OVRProjectSetup.UpdateTasks(buildTargetGroup, logMessages: OVRProjectSetup.LogMessages.Disabled,
            blocking: false, onCompleted: OnUpdated);
    }

    private void OnUpdated(OVRConfigurationTaskProcessor processor)
    {
        var updater = processor as OVRConfigurationTaskUpdater;
        _lastSummary = updater?.Summary;
    }

    internal static void ShowSettingsMenu()
    {
        var menu = new GenericMenu();
        OVRProjectSetup.Enabled.AppendToMenu(menu);
        OVRProjectSetupUpdater.Enabled.AppendToMenu(menu);
        OVRProjectSetup.RequiredThrowErrors.AppendToMenu(menu);
        OVRProjectSetup.AllowLogs.AppendToMenu(menu);
        OVRProjectSetup.ShowStatusIcon.AppendToMenu(menu);
        OVRProjectSetup.ProduceReportOnBuild.AppendToMenu(menu);
        menu.ShowAsContext();
    }

    private void ShowItemMenu(BuildTargetGroup buildTargetGroup, OVRConfigurationTask task)
    {
        var menu = new GenericMenu();
        var hasDocumentation = !string.IsNullOrEmpty(task.URL.GetValue(buildTargetGroup));
        if (hasDocumentation)
        {
            menu.AddItem(new GUIContent("Documentation"), false, OnDocumentation,
                new object[] { buildTargetGroup, task });
        }

        var hasSourceCode = task.SourceCode.Valid;
        if (hasSourceCode)
        {
            menu.AddItem(new GUIContent("Go to Source Code"), false, OnGoToSourceCode,
                new object[] { buildTargetGroup, task });
        }

        menu.AddItem(new GUIContent("Ignore"), task.IsIgnored(buildTargetGroup), OnIgnore,
            new object[] { buildTargetGroup, task });
        menu.ShowAsContext();
    }

    internal void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(GUIStyles.DialogBox);
        EditorGUILayout.LabelField(DialogIcon, GUIStyles.DialogIconStyle, GUILayout.Width(GUIStyles.DialogIconStyle.fixedWidth));
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(Description, GUIStyles.DialogTextStyle);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        var enabled = OVRProjectSetup.Enabled.Value;
        using (new EditorGUI.DisabledScope(!enabled))
        {
            // Summary
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(SummaryLabel, Styles.GUIStyles.NormalStyle);
                if (enabled)
                {
                    var (icon, color) = GetTaskIcon(_lastSummary?.HighestFixLevel);
                    using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, color))
                    {
                        GUILayout.Label(icon, Styles.GUIStyles.InlinedIconStyle);
                    }
                    GUILayout.Label(_lastSummary?.ComputeNoticeMessage() ?? "", Styles.GUIStyles.BoldStyle);
                }
                else
                {
                    GUILayout.Label($"{OVRProjectSetupUtils.ProjectSetupToolPublicName} is disabled", Styles.GUIStyles.BoldStyle);
                }
            }

            // Checklist
            using (var buildTargetSelection = new BuildTargetSelectionScope())
            {
                var buildTargetGroup = buildTargetSelection.BuildTargetGroup;
                if (_selectedBuildTargetGroup != buildTargetGroup)
                {
                    _selectedBuildTargetGroup = buildTargetGroup;
                    UpdateTasks(buildTargetGroup);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.Space();
                    DrawTasksList(_selectedBuildTargetGroup);
                }
            }
        }
    }

    private void DrawTasksList(BuildTargetGroup buildTargetGroup)
    {
        var disableTasksList = EditorApplication.isPlaying;

        using (new EditorGUI.DisabledGroupScope(disableTasksList))
        {
            // Header
            using (new EditorGUILayout.HorizontalScope())
            {
                // Title
                GUILayout.Label(ListTitle,
                    Styles.GUIStyles.IssuesTitleLabel, GUILayout.Width(Styles.Constants.TitleLabelWidth));

                GUILayout.FlexibleSpace();

                // Filter
                EnumPopup<OVRProjectSetup.TaskGroup>(Filter, _selectedTaskGroup,
                    group => _selectedTaskGroup = group);

                // More Actions Menu Button
                DrawMoreActionsMenuList(buildTargetGroup);
            }

            // Scroll View
            _scrollViewPos = EditorGUILayout.BeginScrollView(_scrollViewPos, Styles.GUIStyles.IssuesBackground,
                GUILayout.ExpandHeight(true));

            DrawCategory(_showOutstandingItems, tasks => tasks
                    .Where(task =>
                        (_selectedTaskGroup == OVRProjectSetup.TaskGroup.All || task.Group == _selectedTaskGroup)
                        && !task.IsDone(buildTargetGroup)
                        && !task.IsIgnored(buildTargetGroup)
                        && (task.Level.GetValue(buildTargetGroup) == OVRProjectSetup.TaskLevel.Required))
                    .OrderByDescending(task => task.FixAction == null)
                    .ToList(),
                buildTargetGroup, OutstandingItems, true);

            DrawCategory(_showRecommendedItems, tasks => tasks
                    .Where(task =>
                        (_selectedTaskGroup == OVRProjectSetup.TaskGroup.All || task.Group == _selectedTaskGroup)
                        && !task.IsDone(buildTargetGroup)
                        && !task.IsIgnored(buildTargetGroup)
                        && (task.Level.GetValue(buildTargetGroup) != OVRProjectSetup.TaskLevel.Required))
                    .OrderByDescending(task => task.Level.GetValue(buildTargetGroup))
                    .ThenBy(task => task.FixAction == null)
                    .ToList(),
                buildTargetGroup, RecommendedItems, true);

            DrawCategory(_showVerifiedItems, tasks => tasks
                    .Where(task =>
                        (_selectedTaskGroup == OVRProjectSetup.TaskGroup.All || task.Group == _selectedTaskGroup)
                        && task.IsDone(buildTargetGroup)
                        && !task.IsIgnored(buildTargetGroup))
                    .OrderByDescending(task => task.FixAction == null)
                    .ThenBy(task => task.Level.GetValue(buildTargetGroup))
                    .ToList(),
                buildTargetGroup, VerifiedItems, false);

            DrawCategory(_showIgnoredItems, tasks => tasks
                    .Where(task =>
                        (_selectedTaskGroup == OVRProjectSetup.TaskGroup.All || task.Group == _selectedTaskGroup)
                        && task.IsIgnored(buildTargetGroup))
                    .OrderByDescending(task => task.Level.GetValue(buildTargetGroup))
                    .ThenBy(task => task.FixAction != null)
                    .ToList(),
                buildTargetGroup, IgnoredItems, false);

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawCategory(OVRProjectSetupSettingBool key, Func<IEnumerable<OVRConfigurationTask>,
        List<OVRConfigurationTask>> filter, BuildTargetGroup buildTargetGroup, string title, bool fixAllButton)
    {
        var tasks = filter(OVRProjectSetup.GetTasks(buildTargetGroup, false));

        if (key == null || tasks == null || tasks.Count == 0)
        {
            return;
        }

        using (var scope = new EditorGUILayout.VerticalScope(Styles.GUIStyles.List))
        {
            var rect = scope.rect;

            // Foldout
            title = $"{title} ({tasks.Count})";

            var foldout = FoldoutWithAdditionalAction(key, title, rect, () =>
            {
                if (fixAllButton)
                {
                    if (tasks.Any(task => task.FixAction != null))
                    {
                        var content = tasks[0].Level.GetValue(buildTargetGroup) == OVRProjectSetup.TaskLevel.Required
                            ? FixAllButtonContent
                            : ApplyAllButtonContent;
                        EditorGUI.BeginDisabledGroup(
                            OVRProjectSetup.ProcessorQueue.BusyWith(OVRConfigurationTaskProcessor.ProcessorType.Fixer));
                        if (GUILayout.Button(content, Styles.GUIStyles.FixAllButton))
                        {
                            OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider
                                .Interaction.Fixed);

                            OVRProjectSetup.FixTasks(buildTargetGroup, filter, blocking: false,
                                onCompleted: AfterFixApply);
                        }

                        EditorGUI.EndDisabledGroup();
                    }
                }
            });

            if (foldout)
            {
                DrawIssues(tasks, buildTargetGroup);
            }
        }
    }

    private void AfterFixApply(OVRConfigurationTaskProcessor processor)
    {
        AssetDatabase.SaveAssets();
        UpdateTasks(processor.BuildTargetGroup);
    }

    private void DrawIssues(List<OVRConfigurationTask> tasks, BuildTargetGroup buildTargetGroup)
    {
        foreach (var task in tasks)
        {
            DrawIssue(task, buildTargetGroup);
        }
    }

    private void DrawIssue(OVRConfigurationTask task, BuildTargetGroup buildTargetGroup)
    {
        var ignored = task.IsIgnored(buildTargetGroup);
        var cannotBeFixed = task.IsDone(buildTargetGroup) ||
                            OVRProjectSetup.ProcessorQueue.BusyWith(OVRConfigurationTaskProcessor.ProcessorType.Fixer);
        var disabled = cannotBeFixed || ignored;

        // Note : We're not using scopes, because in this very case, we've got a cross of scopes
        EditorGUI.BeginDisabledGroup(disabled);
        var clickArea = EditorGUILayout.BeginHorizontal(Styles.GUIStyles.ListLabel);

        // Icon
        var (icon, color) = GetTaskIcon(task, buildTargetGroup);
        using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, color))
        {
            GUILayout.Label(icon, Styles.GUIStyles.IconStyle);
        }

        // Message
        GUILayout.Label(new GUIContent(task.Message.GetValue(buildTargetGroup)), Styles.GUIStyles.Wrap);

        EditorGUI.EndDisabledGroup();

        if (task.FixAction != null)
        {
            EditorGUI.BeginDisabledGroup(cannotBeFixed);
            var content = task.Level.GetValue(buildTargetGroup) == OVRProjectSetup.TaskLevel.Required
                ? FixButtonContent
                : ApplyButtonContent;

            var fixMessage = task.FixMessage.GetValue(buildTargetGroup);
            var tooltip = fixMessage != null ? $"{content.tooltip} :\n{fixMessage}" : content.tooltip;
            content = new GUIContent(content.text, tooltip);
            if (GUILayout.Button(content, Styles.GUIStyles.FixButton))
            {
                OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.Fixed);

                OVRProjectSetup.FixTask(buildTargetGroup, task, blocking: false, onCompleted: AfterFixApply);
            }

            EditorGUI.EndDisabledGroup();
        }

        var current = Event.current;
        if (GUILayout.Button("", EditorStyles.foldoutHeaderIcon, GUILayout.Width(16.0f))
            || (clickArea.Contains(current.mousePosition) && current.type == EventType.ContextClick))
        {
            ShowItemMenu(buildTargetGroup, task);
            if (current.type == EventType.ContextClick)
            {
                current.Use();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawMoreActionsMenuList(BuildTargetGroup buildTargetGroup)
    {
        var current = Event.current;
        if (GUILayout.Button("", EditorStyles.foldoutHeaderIcon, GUILayout.Width(16.0f)))
        {
            var menu = new GenericMenu();
            menu.AddItem(RefreshTasksButtonContent, false, OnRefresh, new object[] { buildTargetGroup });
            menu.AddItem(GenerateReportButtonContent, false, OnGenerateReport, new object[] { buildTargetGroup });
            menu.ShowAsContext();
            if (current.type == EventType.ContextClick)
            {
                current.Use();
            }
        }
    }

    private void ReadContextMenuArguments(
        object arg,
        out BuildTargetGroup buildTargetGroup,
        out OVRConfigurationTask task)
    {
        var args = arg as object[];
        buildTargetGroup = args != null ? (BuildTargetGroup)args[0] : BuildTargetGroup.Unknown;
        task = args?[1] as OVRConfigurationTask;
    }

    private void OnIgnore(object args)
    {
        ReadContextMenuArguments(args, out var buildTargetGroup, out var task);

        var ignore = !task.IsIgnored(buildTargetGroup);
        if (ignore)
        {
            OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.Ignored);
        }

        task?.SetIgnored(buildTargetGroup, ignore);
    }

    private void OnDocumentation(object args)
    {
        OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction
            .WentToDocumentation);

        ReadContextMenuArguments(args, out var buildTargetGroup, out var task);
        var url = task?.URL.GetValue(buildTargetGroup);

        Application.OpenURL(url);
    }

    private void OnGoToSourceCode(object args)
    {
        OVRProjectSetupSettingsProvider.SetNewInteraction(OVRProjectSetupSettingsProvider.Interaction.WentToSource);

        ReadContextMenuArguments(args, out var buildTargetGroup, out var task);
        task?.SourceCode.Open();
    }

    private void OnGenerateReport(object arg)
    {
        var buildTargetGroup = arg is object[] args ? (BuildTargetGroup)args[0] : BuildTargetGroup.Unknown;
        var path = EditorUtility.OpenFolderPanel(OutFolderTitle, "", "");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var reportFileName = GenerateReport(buildTargetGroup, path);
            EditorUtility.DisplayDialog(SuccessTitle, $"{ReportGenerationSuccessMessage}\n{reportFileName}", OkButton);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog(ErrorTitle, $"{ReportGenerationErrorMessage}\n{e.Message}", OkButton);
        }
    }

    private void OnRefresh(object arg)
    {
        var buildTargetGroup = arg is object[] args ? (BuildTargetGroup)args[0] : BuildTargetGroup.Unknown;
        try
        {
            OVRProjectSetup.UpdateTasks(buildTargetGroup, logMessages: OVRProjectSetup.LogMessages.Disabled,
                blocking: true);
            EditorUtility.DisplayDialog(SuccessTitle, TasksRefreshSuccessMessage, OkButton);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog(ErrorTitle, $"{TasksRefreshErrorMessage}\n{e.Message}", OkButton);
        }
    }
}
