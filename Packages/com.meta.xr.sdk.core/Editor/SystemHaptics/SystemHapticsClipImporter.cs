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

// @lint-ignore-every LICENSELINT


using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Importer for a .systemhaptic clip, making it available for playback at runtime.
/// </summary>
/// <remarks>
/// A <c>.systemhaptic</c> clip file originates from a JSON encoded <c>.haptic</c> clip containing haptic playback data,
/// designed in <a href="https://developer.oculus.com/resources/haptics-overview/">Meta Haptics Studio</a>.
///
/// This custom extension was chosen to differentiate the system haptics clips from other custom <c>.haptic</c> clips
/// used in a Unity project, and thus to avoid conflicts with the <c>.haptic</c> importer script of the
/// <a href="https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk/">Meta XR Haptics SDK</a>.
/// </remarks>
[ScriptedImporter(version: 3, ext: "systemhaptic", AllowCaching = true)]
public class SystemHapticsClipImporter : ScriptedImporter
{
    /// <summary>
    /// Loads the raw JSON encoded string data from a <c>.systemhaptic</c> file into a <see cref="SystemHapticsClipData"/>
    /// object and imports the <see cref="SystemHapticsClipData"/> ScriptableObject into the <c>AssetDatabase</c>.
    /// </summary>
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var jsonString = File.ReadAllText(ctx.assetPath);

        try
        {
            var systemHapticsClipData = ScriptableObject.CreateInstance<SystemHapticsClipData>();

            JsonUtility.FromJsonOverwrite(jsonString, systemHapticsClipData);

            ctx.AddObjectToAsset("com.meta.xr.core.SystemHapticsClipData", systemHapticsClipData);
            ctx.SetMainObject(systemHapticsClipData);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
