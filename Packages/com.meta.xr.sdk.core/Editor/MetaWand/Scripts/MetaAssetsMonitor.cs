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
using System.IO;
using Meta.XR.MetaWand.Editor.Telemetry;
using UnityEditor;

namespace Meta.XR.MetaWand.Editor
{
    internal class MetaAssetsMonitor : AssetModificationProcessor
    {
        private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                HandleFolderDeletion(assetPath);
            }
            else
            {
                HandleFileDeletion(assetPath);
            }

            return AssetDeleteResult.DidNotDelete;
        }

        private static void HandleFolderDeletion(string folderPath)
        {
            var allAssetGuids = AssetDatabase.FindAssets("", new[] { folderPath });

            foreach (var guid in allAssetGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                HandleFileDeletion(assetPath);
            }
        }

        private static void HandleFileDeletion(string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            var assetInfo = MetaAssetRegistry.GetAssetInfo(guid);
            if (assetInfo == null) return;

            SendAssetDeletionTelemetry(assetPath, assetInfo);
            MetaAssetRegistry.UnregisterAsset(guid);

        }

        private static void SendAssetDeletionTelemetry(string assetPath, MetaAssetInfo assetInfo)
        {
            var fileName = Path.GetFileNameWithoutExtension(assetPath);

            var unifiedEvent = new OVRPlugin.UnifiedEventData(Constants.Telemetry.EventNameAssetDeleted)
            {
                entrypoint = Constants.Telemetry.EntrypointLoadState,
                target = Constants.Telemetry.TargetProjectAssets,
                isEssential = OVRPlugin.Bool.True
            };

            unifiedEvent.SetMetadata(Constants.Telemetry.ParamSessionId, assetInfo.PromptId ?? string.Empty);
            unifiedEvent.SetMetadata(Constants.Telemetry.ParamAssetPath, assetPath);
            unifiedEvent.SetMetadata(Constants.Telemetry.ParamAssetType, assetInfo.AssetType);
            unifiedEvent.SetMetadata(Constants.Telemetry.ParamAssetName, fileName);
            unifiedEvent.SetMetadata(Constants.Telemetry.ParamAssetId, assetInfo.AssetId);
            unifiedEvent.SetMetadata(Constants.Telemetry.ParamIsPregenResult, assetInfo.IsPreGen.ToString());

            unifiedEvent.SendMetaWandEvent();
        }
    }
}
