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
using Meta.XR.Util;
using static OVRPlugin;

/// <summary>
/// Low-level functionality related to Scene Understanding
/// </summary>
/// <remarks>
/// Scene empowers you to quickly build complex and scene-aware experiences with rich interactions in the user’s
/// physical environment.
///
/// See [Unity Scene Overview](https://developer.oculus.com/documentation/unity/unity-scene-overview) for more details.
/// </remarks>
[Feature(Feature.Scene)]
public static partial class OVRScene
{
    /// <summary>
    /// Requests Space Setup.
    /// </summary>
    /// <remarks>
    /// [Space Setup](https://developer.oculus.com/documentation/unity/unity-scene-overview/#how-does-scene-work) pauses
    /// the application and prompts the user to setup their Space. The app resumes when the user either cancels or
    /// completes Space Setup.
    ///
    /// <parmaref name="labels"/> is collection of comma-separated semantic labels that the user must define during
    /// Space Setup. You may specify the same label multiple times.
    /// <example>
    /// For example, this code:
    /// <code><![CDATA[
    /// await OVRScene.RequestSpaceSetup("TABLE,TABLE");
    /// ]]></code>
    /// would prompt the user to define two tables.
    /// </example>
    ///
    /// See <see cref="OVRSemanticLabels"/> for creating the string.
    ///
    /// This method is asynchronous. The result of the task indicates whether the operation was successful. `False`
    /// usually indicates an unexpected failure; if the user cancels Space Setup, the operation still completes
    /// successfully.
    /// </remarks>
    /// <param name="labels">The types of anchors that the user should define.</param>
    /// <returns>Returns a task that can be used to track the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="labels"/> contains a label that is not one of
    /// those provided by <see cref="OVRSemanticLabels.Classification"/></exception>
    [Obsolete(OVRSemanticLabels.DeprecationMessage)]
    public static OVRTask<bool> RequestSpaceSetup(string labels)
    {
#if DEVELOPMENT_BUILD || OVRPLUGIN_TESTING
        if (!string.IsNullOrEmpty(labels))
        {
            ValidateRequestString(labels.Split(','), nameof(labels));
        }
#endif

        return RequestSceneCapture(labels, out var requestId)
            ? OVRTask.FromRequest<bool>(requestId)
            : OVRTask.FromResult(false);
    }

    /// <summary>
    /// Requests Space Setup using a list of classifications (may be null or empty).
    /// </summary>
    /// <remarks>
    /// [Space Setup](https://developer.oculus.com/documentation/unity/unity-scene-overview/#how-does-scene-work)
    /// pauses the application and prompts the user to setup their Space. The app resumes when the user
    /// either cancels or completes Space Setup.
    ///
    /// If not null or empty, <parmaref name="classifications"/> is a list of semantic labels that the user
    /// must define during Space Setup. You may specify the same label multiple times. For example,
    /// <code><![CDATA[
    /// await RequestSpaceSetup(new [] {Classification.Table, Classification.Table});
    /// ]]></code>
    /// would prompt the user to define two tables.
    ///
    /// See <see cref="OVRSemanticLabels.Classification"/> for a list of all labels.
    ///
    /// This method is asynchronous. The result of the task indicates whether the operation was successful. `false`
    /// usually indicates an unexpected failure; if the user cancels Space Setup, the operation still completes
    /// successfully.
    /// </remarks>
    /// <param name="classifications">(Optional) The types of anchors that the user should define.</param>
    /// <returns>Returns a task that can be used to track the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="classifications"/> contains a label that is not
    /// one of those provided by <see cref="OVRSemanticLabels.Classification"/></exception>
    public static OVRTask<bool> RequestSpaceSetup(IReadOnlyList<OVRSemanticLabels.Classification> classifications = null) =>
#pragma warning disable CS0618 // Type or member is obsolete
        RequestSpaceSetup(OVRSemanticLabels.ToApiString(classifications));
#pragma warning restore CS0618 // Type or member is obsolete

    static void ValidateRequestString(IEnumerable<string> labels, string paramName)
    {
        foreach (var label in labels.ToNonAlloc())
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (label == null || !OVRSceneManager.Classification.Set.Contains(label))
#pragma warning restore CS0618 // Type or member is obsolete
            {
                throw new ArgumentException($"'{label}' is not a valid label. See " +
                    $"{nameof(OVRSemanticLabels)}.{nameof(OVRSemanticLabels.Classification)}.", paramName);
            }
        }
    }
}
