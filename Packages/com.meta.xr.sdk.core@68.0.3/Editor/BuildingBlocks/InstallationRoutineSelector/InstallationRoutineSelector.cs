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

namespace Meta.XR.BuildingBlocks.Editor
{
    internal class MissingInstallationRoutineException : InvalidOperationException
    {
        internal MissingInstallationRoutineException(string message) : base(message) { }
    }
    internal static class InstallationRoutineSelector
    {
        internal static readonly VariantsSelection Selection = new();

        public static IReadOnlyList<InstallationRoutine> ComputePossibleRoutines(InterfaceBlockData blockData)
        {
            var routines = blockData.GetAvailableInstallationRoutines().ToArray();

            // From availableInstallationRoutines, we pick the definition variants we need to find
            var definitionVariants = routines.SelectMany(routine =>
                routine.DefinitionVariants);

            // We will find in the blocks in scene if any set one of the definitions
            var foundDefinitionVariants = Utils.GetBlocksInScene()
                .Where(block => block.GetBlockData() is InterfaceBlockData)
                .Select(block => block.GetInstallationRoutine())
                .SelectMany(installationRoutine => installationRoutine?.DefinitionVariants ?? Enumerable.Empty<VariantHandle>())
                .Where(otherDefinitionVariant =>
                    definitionVariants.Any(definitionVariant => definitionVariant.Matches(otherDefinitionVariant)))
                .GroupBy(definitionVariant => definitionVariant.MemberInfo.Name)
                .Select(otherDefinitionVariants => otherDefinitionVariants.First())
                .ToArray();

            var possibleRoutines = routines;
            if (foundDefinitionVariants.Length > 0)
            {
                // As we've found definitions in the scene, we will filter by it
                possibleRoutines = routines.Where(routine => routine.Fits(foundDefinitionVariants)).ToArray();
            }

            // Backwards deduction: current inspected routines needs to be filtered by the possible routine from Selection
            if (possibleRoutines.Length != 0 && Selection.BlockData != null && Selection.BlockData.Id != blockData.Id)
            {
                var selectedDefinitionVariants = Selection.PossibleRoutines.SelectMany(routine =>
                    routine.DefinitionVariants);

                possibleRoutines = possibleRoutines.Where(routine =>
                        // routine doesn't have any definition matched with selected, not filtering
                        routine.DefinitionVariants.All(definitionVariant => !selectedDefinitionVariants.Any(definitionVariant.Matches)) ||
                        // routine has matched definition and fits with selected variant, not filtering
                        routine.DefinitionVariants.Any(definitionVariant => selectedDefinitionVariants.Any(definitionVariant.Fits))
                    )
                    .ToArray();
            }

            return possibleRoutines;
        }



        public static async Task<InstallationRoutine> GetSelected(InterfaceBlockData blockData)
        => await SelectInstallationRoutineFromAlternatives(blockData, ComputePossibleRoutines(blockData));


        private static async Task<InstallationRoutine> SelectInstallationRoutineFromAlternatives(BlockData blockData,
            IReadOnlyList<InstallationRoutine> installationRoutines)
        {
            // Early return if no routines available
            if (installationRoutines.Count == 0)
            {
                throw new MissingInstallationRoutineException($"There are no available installation routines");
            }

            // Generates the variants that need to be selected
            if (Selection.RequiresChoiceFor(installationRoutines))
            {
                // A choice is necessary
                {
                    var window = InstallationWindow.ShowWindowFor(Selection);
                    await WaitUntil(() => Selection.Completed);
                    window.Close();
                }
            }

            return Selection.ComputeIdealInstallationRoutine(installationRoutines);
        }

        private static async Task WaitUntil(Func<bool> predicate, int sleep = 50)
        {
            while (!predicate())
            {
                await Task.Delay(sleep);
            }
        }
    }
}
