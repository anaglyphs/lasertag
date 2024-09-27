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

namespace Meta.XR.BuildingBlocks
{
    [Serializable]
    public class InstallationRoutineCheckpoint
    {
        [SerializeField, HideInInspector] private string _installationRoutineId;
        public string InstallationRoutineId => _installationRoutineId;

        [SerializeField, HideInInspector] private List<VariantCheckpoint> _installationVariants;
        public List<VariantCheckpoint> InstallationVariants => _installationVariants;

        public InstallationRoutineCheckpoint(string installationRoutineId, List<VariantCheckpoint> installationVariants)
        {
            _installationRoutineId = installationRoutineId;
            _installationVariants = installationVariants;
        }
    }

    [Serializable]
    public class VariantCheckpoint
    {
        [SerializeField] protected string _memberName;
        public string MemberName => _memberName;

        [SerializeField] protected string _value;
        public string Value => _value;

        public VariantCheckpoint(string memberName, string value)
        {
            _memberName = memberName;
            _value = value;
        }
    }
}
