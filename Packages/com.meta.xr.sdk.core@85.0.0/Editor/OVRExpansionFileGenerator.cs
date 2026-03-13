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
using System.IO;
using System.Xml;
using Meta.XR.Telemetry;
using UnityEngine;
using UnityEditor;

public class BuildAssetBundles : MonoBehaviour
{
    [MenuItem("Meta/Tools/Build Mobile-Quest Expansion File", false, 100000)]
    public static void BuildBundles()
    {
        // Create expansion file directory and call build asset bundles
        string path = Application.dataPath + "/../Asset Bundles/";
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }

        BuildPipeline.BuildAssetBundles(path, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);

        // Rename asset bundle file to the proper obb string
        if (File.Exists(path + "Asset Bundles"))
        {
            string expansionName = "main." + PlayerSettings.Android.bundleVersionCode + "." +
                                   PlayerSettings.applicationIdentifier + ".obb";
            try
            {
                if (File.Exists(path + expansionName))
                {
                    File.Delete(path + expansionName);
                }

                File.Move(path + "Asset Bundles", path + expansionName);
                UnityEngine.Debug.Log("OBB expansion file " + expansionName + " has been successfully created at " +
                                      path);
            }
            catch (Exception e)
            {
                IssueTracker.TrackError(IssueTracker.SDK.Core, "obb-expansion-file-creation-failed",
                    $"Failed to create OBB expansion file '{expansionName}' at {path}: {e.Message}");
            }
        }
    }
}
