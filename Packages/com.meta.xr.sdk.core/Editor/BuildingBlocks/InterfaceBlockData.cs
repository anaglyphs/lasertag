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
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class InterfaceBlockData : BlockData
    {
        private InstallationRoutine _selectedRoutine;

        protected override bool UsesPrefab => false;

        private void Awake()
        {
            _selectedRoutine = null;
        }

        protected override void SetupBlockComponent(BuildingBlock block)
        {
            base.SetupBlockComponent(block);

            block.InstallationRoutineCheckpoint = _selectedRoutine.ToCheckpoint();
        }

        internal override bool CanBeAdded => HasInstallationRoutine && base.CanBeAdded;

        internal IReadOnlyList<string> ComputeMissingPackageDependencies(VariantsSelection selection)
        {
            return ComputePackageDependencies(this, selection, new HashSet<string>())
                .Where(packageId => !Utils.IsPackageInstalled(packageId)).ToList();
        }

        internal static IEnumerable<string> ComputePackageDependencies(InterfaceBlockData blockData, VariantsSelection selection)
        {
            var possibleRoutines = InstallationRoutineSelector.ComputePossibleRoutines(blockData);
            var idealRoutine = selection.ComputeIdealInstallationRoutine(possibleRoutines);
            var dependencies = blockData.PackageDependencies ?? Enumerable.Empty<string>();
            if (idealRoutine != null)
            {
                dependencies = dependencies.Concat(idealRoutine.ComputePackageDependencies(selection) ?? Enumerable.Empty<string>());
            }
            return dependencies;
        }

        private static HashSet<string> ComputePackageDependencies(InterfaceBlockData blockData, VariantsSelection selection, HashSet<string> set)
        {
            foreach (var packageDependency in ComputePackageDependencies(blockData, selection))
            {
                set.Add(packageDependency);
            }

            foreach (var dependency in blockData.Dependencies)
            {
                if (dependency is InterfaceBlockData interfaceDependency)
                {
                    ComputePackageDependencies(interfaceDependency, selection, set);
                }
                else
                {
                    dependency.CollectPackageDependencies(set);
                }
            }

            return set;
        }

        internal override async Task<List<GameObject>> InstallWithDependencies(List<GameObject> selectedGameObjects)
        {
            InstallationRoutineSelector.Selection.Setup(this);

            try
            {
                _selectedRoutine = await InstallationRoutineSelector.GetSelected(this);
                var createdObjects = new List<GameObject>();
                if (_selectedRoutine != null)
                {
                    foreach (var obj in selectedGameObjects.DefaultIfEmpty())
                    {
                        createdObjects.AddRange(await base.InstallWithDependencies(obj));
                    }
                }
                // reset the static caches
                _selectedRoutine = null;
                InstallationRoutineSelector.Selection.Release(this);
                return createdObjects;
            }
            catch (Exception)
            {
                // reset the static caches
                _selectedRoutine = null;
                InstallationRoutineSelector.Selection.Release(this, true);
                throw;
            }
        }

        internal override async Task<List<GameObject>> InstallWithDependencies(GameObject selectedGameObject = null)
        {
            return await InstallWithDependencies(new List<GameObject> { selectedGameObject });
        }

        protected override async Task<List<GameObject>> InstallRoutineAsync(GameObject selectedGameObject)
            => await _selectedRoutine.InstallAsync(this, selectedGameObject);

        #region InstallationRoutine

        internal bool HasInstallationRoutine => GetAvailableInstallationRoutines().Any();

        internal IEnumerable<InstallationRoutine> GetAvailableInstallationRoutines() =>
            InstallationRoutine.Registry.Values.Where(x => x.TargetBlockDataId == Id);

        #endregion

    }
}
