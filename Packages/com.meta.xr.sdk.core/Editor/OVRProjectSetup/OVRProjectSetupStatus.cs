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
using System.Linq;
using UnityEditor;

/// <summary>
/// Centralized utility class for computing Project Setup Tool status information.
/// This ensures consistency across the UI, notifications, menu items, and other surfaces.
/// </summary>
internal static class OVRProjectSetupStatus
{
    /// <summary>
    /// Result structure containing all computed status information.
    /// </summary>
    public struct StatusResult
    {
        /// <summary>
        /// The highest priority level (Required > Recommended > Optional) that has outstanding tasks.
        /// Null if there are no outstanding tasks.
        /// </summary>
        public OVRProjectSetup.TaskLevel? HighestFixLevel;

        /// <summary>
        /// The count of outstanding tasks at the highest fix level.
        /// </summary>
        public int HighestLevelCount;

        /// <summary>
        /// The total count of all outstanding tasks (not done and not ignored).
        /// </summary>
        public int TotalOutstandingCount;

        /// <summary>
        /// The list of outstanding tasks.
        /// </summary>
        public IReadOnlyList<OVRConfigurationTask> OutstandingTasks;
    }

    /// <summary>
    /// Computes the status for the given build target group by querying the task registry directly.
    /// This bypasses any cooldown filters to ensure accurate counts that match the UI.
    /// </summary>
    /// <param name="buildTargetGroup">The build target group to compute status for.</param>
    /// <returns>A StatusResult containing all computed status information.</returns>
    public static StatusResult ComputeStatus(BuildTargetGroup buildTargetGroup)
    {
        var tasks = OVRProjectSetup.GetTasks(buildTargetGroup);
        var outstandingTasks = tasks
            .Where(task => IsOutstanding(task, buildTargetGroup))
            .ToList();

        var highestLevel = ComputeHighestFixLevel(outstandingTasks, buildTargetGroup);
        var highestLevelCount = highestLevel.HasValue
            ? outstandingTasks.Count(task => task.Level.GetValue(buildTargetGroup) == highestLevel.Value)
            : 0;

        return new StatusResult
        {
            HighestFixLevel = highestLevel,
            HighestLevelCount = highestLevelCount,
            TotalOutstandingCount = outstandingTasks.Count,
            OutstandingTasks = outstandingTasks
        };
    }

    /// <summary>
    /// Determines if a task is outstanding (not done and not ignored).
    /// </summary>
    public static bool IsOutstanding(OVRConfigurationTask task, BuildTargetGroup buildTargetGroup)
    {
        return !task.IsDone(buildTargetGroup) && !task.IsIgnored(buildTargetGroup);
    }

    /// <summary>
    /// Determines if a task is manually fixable.
    /// </summary>
    public static bool IsManuallyFixable(OVRConfigurationTask task)
    {
        return task.Tags.HasFlag(OVRProjectSetup.TaskTags.ManuallyFixable);
    }

    /// <summary>
    /// Computes the highest priority level that has outstanding tasks.
    /// </summary>
    /// <param name="outstandingTasks">The list of outstanding tasks.</param>
    /// <param name="buildTargetGroup">The build target group.</param>
    /// <returns>The highest task level with outstanding tasks, or null if none.</returns>
    public static OVRProjectSetup.TaskLevel? ComputeHighestFixLevel(
        IEnumerable<OVRConfigurationTask> outstandingTasks,
        BuildTargetGroup buildTargetGroup)
    {
        for (var level = OVRProjectSetup.TaskLevel.Required;
             level >= OVRProjectSetup.TaskLevel.Optional;
             level--)
        {
            if (outstandingTasks.Any(task => task.Level.GetValue(buildTargetGroup) == level))
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the status message for display in the UI.
    /// </summary>
    /// <param name="status">The computed status result.</param>
    /// <returns>A human-readable status message.</returns>
    public static string ComputeStatusMessage(StatusResult status)
    {
        if (!status.HighestFixLevel.HasValue || status.HighestLevelCount == 0)
        {
            return "All checks passed";
        }

        var levelName = status.HighestFixLevel.Value.ToString();
        var count = status.HighestLevelCount;

        if (count == 1)
        {
            return $"There is 1 outstanding {levelName} fix.";
        }
        else
        {
            return $"There are {count} outstanding {levelName} fixes.";
        }
    }

    /// <summary>
    /// Computes the subtitle text showing the total number of available fixes.
    /// </summary>
    /// <param name="status">The computed status result.</param>
    /// <returns>A human-readable subtitle message.</returns>
    public static string ComputeSubtitleMessage(StatusResult status)
    {
        var totalFixes = status.TotalOutstandingCount;

        if (totalFixes > 0)
        {
            return $"{totalFixes} fix{(totalFixes > 1 ? "es" : "")} available";
        }
        else
        {
            return "Your project is ready!";
        }
    }

    /// <summary>
    /// Gets the count of outstanding tasks at a specific level.
    /// </summary>
    public static int GetCountAtLevel(
        IEnumerable<OVRConfigurationTask> outstandingTasks,
        BuildTargetGroup buildTargetGroup,
        OVRProjectSetup.TaskLevel level)
    {
        return outstandingTasks.Count(task => task.Level.GetValue(buildTargetGroup) == level);
    }

    /// <summary>
    /// Gets outstanding tasks at a specific level.
    /// </summary>
    public static IEnumerable<OVRConfigurationTask> GetTasksAtLevel(
        IEnumerable<OVRConfigurationTask> outstandingTasks,
        BuildTargetGroup buildTargetGroup,
        OVRProjectSetup.TaskLevel level)
    {
        return outstandingTasks.Where(task => task.Level.GetValue(buildTargetGroup) == level);
    }

    /// <summary>
    /// Gets outstanding tasks that are not manually fixable at a specific level.
    /// Useful for notifications which should not include manually fixable tasks.
    /// </summary>
    public static IEnumerable<OVRConfigurationTask> GetAutoFixableTasksAtLevel(
        IEnumerable<OVRConfigurationTask> outstandingTasks,
        BuildTargetGroup buildTargetGroup,
        OVRProjectSetup.TaskLevel level)
    {
        return outstandingTasks.Where(task =>
            task.Level.GetValue(buildTargetGroup) == level
            && !IsManuallyFixable(task));
    }
}
