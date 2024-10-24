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
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using System.Linq;
using static Meta.XR.BuildingBlocks.Editor.VariantAttribute;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class InstallationRoutine : ScriptableObject, IIdentified
    {
        internal static readonly CachedIdDictionary<InstallationRoutine> Registry = new();

        [SerializeField, OVRReadOnly] internal string id = Guid.NewGuid().ToString();
        public string Id => id;

        [SerializeField] internal string targetBlockDataId;
        public string TargetBlockDataId => targetBlockDataId;

        public BlockData TargetBlockData => Utils.GetBlockData(TargetBlockDataId);

        protected virtual bool UsesPrefab => true;
        internal bool GetUsesPrefab => UsesPrefab;

        [SerializeField] internal GameObject prefab;
        protected GameObject Prefab => prefab;

        [SerializeField] private List<string> packageDependencies;
        public IEnumerable<string> PackageDependencies => packageDependencies;

        internal virtual IEnumerable<string> ComputePackageDependencies(VariantsSelection variantSelection) =>
            PackageDependencies;

        private IReadOnlyList<VariantHandle> _definitionVariants;
        internal IEnumerable<VariantHandle> DefinitionVariants =>
            _definitionVariants ??= VariantHandle.FetchVariants(this, VariantBehavior.Definition);

        private IReadOnlyList<VariantHandle> _parameterVariants;
        internal IEnumerable<VariantHandle> ParameterVariants =>
            _parameterVariants ??= VariantHandle.FetchVariants(this, VariantBehavior.Parameter);

        private IReadOnlyList<VariantHandle> _constants;
        internal IEnumerable<VariantHandle> Constants =>
            _constants ??= VariantHandle.FetchVariants(this, VariantBehavior.Constant);

        /// <summary>
        /// Whether of not this Installation Routine has the same values for all variants passed as parameter
        /// </summary>
        internal bool Fits(IReadOnlyList<VariantHandle> variants)
            => variants.Where(variant => variant.Attribute.Behavior == VariantBehavior.Definition)
                .Where(variant => DefinitionVariants.Any(variant.Matches)).All(variant => DefinitionVariants.Any(variant.Fits))
               && DefinitionVariants.All(definitionVariant => variants.Any(definitionVariant.Fits));

        internal bool FitsParameters(IReadOnlyList<VariantHandle> variants) => ParameterVariants.All(parameterVariant => !parameterVariant.Condition() || variants.Any(parameterVariant.Fits));

        internal InstallationRoutineCheckpoint ToCheckpoint()
        {
            var variantCheckpoints = ParameterVariants.Select(variant =>
                new VariantCheckpoint(variant.MemberInfo.Name, variant.ToJson())).ToList();
            return new InstallationRoutineCheckpoint(Id, variantCheckpoints);
        }

        internal void ApplySelection(IEnumerable<VariantHandle> selections)
        {
            foreach (var selection in selections)
            {
                foreach (var parameter in ParameterVariants.Where(parameter => parameter.Matches(selection)))
                {
                    parameter.RawValue = selection.RawValue;
                }
            }
        }

        public virtual List<GameObject> Install(BlockData block, GameObject selectedGameObject)
        {
            if (!UsesPrefab)
            {
                return new List<GameObject>();
            }
            var instance = Instantiate(Prefab, Vector3.zero, Quaternion.identity);
            instance.SetActive(true);
            instance.name = $"{Utils.BlockPublicTag} {block.BlockName}";
            Undo.RegisterCreatedObjectUndo(instance, "Create " + instance.name);
            return new List<GameObject> { instance };
        }

        public virtual Task<List<GameObject>> InstallAsync(BlockData block, GameObject selectedGameObject)
        {
            return Task.FromResult(Install(block, selectedGameObject));
        }

    }
}
