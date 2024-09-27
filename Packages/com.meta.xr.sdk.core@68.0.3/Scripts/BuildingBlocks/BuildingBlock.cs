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
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Meta.XR.BuildingBlocks
{
    [DisallowMultipleComponent, ExecuteInEditMode]
    public class BuildingBlock : MonoBehaviour
    {
        [SerializeField, OVRReadOnly] internal string blockId;
        public string BlockId => blockId;

        [SerializeField, HideInInspector] internal string instanceId = Guid.NewGuid().ToString();
        public string InstanceId => instanceId;

        [SerializeField, OVRReadOnly] internal int version = 1;
        public int Version => version;

        [SerializeField, HideInInspector] private InstallationRoutineCheckpoint installationRoutineCheckpoint;
        public InstallationRoutineCheckpoint InstallationRoutineCheckpoint
        {
            get => installationRoutineCheckpoint;
            set => installationRoutineCheckpoint = value;
        }

        private void Awake()
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (HasDuplicateInstanceId())
            {
                ResetInstanceId();
            }
        }

        private void ResetInstanceId()
        {
            instanceId = Guid.NewGuid().ToString();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private bool HasDuplicateInstanceId()
        {
            foreach (var block in FindObjectsByType<BuildingBlock>(FindObjectsSortMode.InstanceID))
            {
                if (block != this && block.InstanceId == InstanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private void Start()
        {
            OVRTelemetry.Start(OVRTelemetryConstants.BB.MarkerId.RunBlock)
                .AddBlockInfo(this)
                .SendIf(Application.isPlaying);
        }
    }
}
