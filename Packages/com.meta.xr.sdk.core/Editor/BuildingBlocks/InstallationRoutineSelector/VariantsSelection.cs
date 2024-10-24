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
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Meta.XR.BuildingBlocks.Editor
{
    internal class VariantsSelection : IReadOnlyList<VariantHandle>
    {
        private readonly List<VariantHandle> _variants = new();
        public BlockData BlockData { get; private set; }
        public bool Completed { get; set; }
        public bool Canceled { get; set; }
        private bool HasMissingDependencies => ((InterfaceBlockData)BlockData).ComputeMissingPackageDependencies(this).Count > 0;
        public IReadOnlyList<InstallationRoutine> PossibleRoutines;

        public bool RequiresChoiceFor(IReadOnlyList<InstallationRoutine> routines)
        {
            if (Completed)
                return false;

            if (!this.Any())
                return false;

            if (this.Any(variant => variant.Attribute.Behavior == VariantAttribute.VariantBehavior.Parameter))
                return true;

            if (HasMissingDependencies)
                return true;

            return routines.Count > 1;
        }

        public InstallationRoutine ComputeIdealInstallationRoutine(
            IReadOnlyList<InstallationRoutine> routines)
        {
            if (Canceled) return null;


            var selectedRoutine = routines.Count() <= 1 ? routines.FirstOrDefault()
                : routines.FirstOrDefault(routine => routine.Fits(this));

            if (selectedRoutine == null || !selectedRoutine.FitsParameters(this))
            {
                throw new MissingInstallationRoutineException($"There are no available installation routines");
            }

            selectedRoutine.ApplySelection(this);
            return selectedRoutine;
        }

        public void Setup(BlockData blockData, bool force = false)
        {
            // Do not setup if already setup (unless forced)
            if (!force && BlockData != null || blockData is not InterfaceBlockData data) return;

            Reset();

            BlockData = data;
            _variants.AddRange(
                GetVariantsRecursive(data)
                    .Select(variant => variant.ToSelection(false)));
            PossibleRoutines = InstallationRoutineSelector.ComputePossibleRoutines(data);
        }

        public void Release(BlockData blockData, bool force = false)
        {
            // Do not release if not the original block (unless forced)
            if (!force && BlockData != blockData) return;

            Reset();
        }

        private void Reset()
        {
            BlockData = null;
            Completed = false;
            Canceled = false;
            _variants.Clear();
        }

        private static IEnumerable<VariantHandle> GetVariantsRecursive(BlockData blockData)
        {
            var variantHandles = GetVariants(blockData);
            var dependenciesHandles = (blockData.dependencies ?? Enumerable.Empty<string>())
                .Select(Utils.GetBlockData)
                .SelectMany(GetVariantsRecursive);

            return variantHandles
                .Union(dependenciesHandles)
                .GroupBy(variant => variant.MemberInfo.Name)
                .Select(variants => variants.First());
        }

        private static IEnumerable<VariantHandle> GetVariants(BlockData blockData)
        {
            if (blockData is InterfaceBlockData interfaceBlockData)
            {
                return GetVariants(interfaceBlockData.GetAvailableInstallationRoutines());
            }

            return Enumerable.Empty<VariantHandle>();
        }

        private static IEnumerable<VariantHandle> GetVariants(IEnumerable<InstallationRoutine> routinesFilter)
        {
            return routinesFilter.SelectMany(routine =>
                    routine.DefinitionVariants.Union(
                        routine.ParameterVariants.Where(variant => variant.Condition())))
                .GroupBy(variant => variant.MemberInfo.Name)
                .Select(variants => variants.First());
        }

        public IEnumerator<VariantHandle> GetEnumerator() => _variants.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _variants.GetEnumerator();
        public int Count => _variants.Count;
        public VariantHandle this[int index] => _variants[index];

    }
}
