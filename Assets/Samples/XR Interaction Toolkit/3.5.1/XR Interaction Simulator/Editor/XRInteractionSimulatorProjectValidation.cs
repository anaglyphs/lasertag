using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils.Editor;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Interaction.Toolkit.ProjectValidation;
using UnityEngine;

namespace UnityEditor.XR.Interaction.Toolkit.Samples.InteractionSimulator.Editor
{
    static class XRInteractionSimulatorProjectValidation
    {
        const string k_SampleDisplayName = "XR Interaction Simulator";
        const string k_Category = "XR Interaction Toolkit";
        const string k_DeviceSimulatorSampleName = "XR Device Simulator";
        const string k_XRIPackageName = "com.unity.xr.interaction.toolkit";
        const string k_ProjectValidationSettingsPath = "Project/XR Plug-in Management/Project Validation";
        static readonly PackageVersion s_MinSubFolderVersion = new PackageVersion("3.1.0-pre.1");
        static readonly PackageVersion s_MaxSubFolderVersion = new PackageVersion("3.2.0-pre.1");

        static readonly BuildTargetGroup[] s_BuildTargetGroups =
            ((BuildTargetGroup[])Enum.GetValues(typeof(BuildTargetGroup))).Distinct().ToArray();

        static readonly List<BuildValidationRule> s_BuildValidationRules = new List<BuildValidationRule>
        {
            new BuildValidationRule
            {
                Message = $"[{k_SampleDisplayName}] The {k_DeviceSimulatorSampleName} sample must be updated as to not have GUID conflicts with the {k_SampleDisplayName} sample.",
                Category = k_Category,
                CheckPredicate = () => !ProjectValidationUtility.SampleImportMeetsVersionRange(k_Category, k_DeviceSimulatorSampleName, s_MinSubFolderVersion, s_MaxSubFolderVersion, false),
                FixIt = () =>
                {
                    // First import updated Device Simulator sample
                    if (TryFindSample(k_XRIPackageName, string.Empty, k_DeviceSimulatorSampleName, out var sample))
                    {
                        sample.Import(Sample.ImportOptions.OverridePreviousImports);
                    }
                    // Then re-import Interaction Simulator sample to clear GUID issues.
                    if (TryFindSample(k_XRIPackageName, string.Empty, k_SampleDisplayName, out sample))
                    {
                        sample.Import(Sample.ImportOptions.OverridePreviousImports);
                    }
                },
                FixItAutomatic = true,
                Error = true,
                HelpText = $"The {k_DeviceSimulatorSampleName} sample must be updated as to not have GUID conflicts with the {k_SampleDisplayName} sample.",
            }
        };

        [InitializeOnLoadMethod]
        static void RegisterProjectValidationRules()
        {
            foreach (var buildTargetGroup in s_BuildTargetGroups)
            {
                BuildValidator.AddRules(buildTargetGroup, s_BuildValidationRules);
            }

            EditorApplication.delayCall += ShowWindowIfIssuesExist;
        }

        static void ShowWindowIfIssuesExist()
        {
            foreach (var validation in s_BuildValidationRules)
            {
                if (validation.CheckPredicate == null || !validation.CheckPredicate.Invoke())
                {
                    ShowWindow();
                    return;
                }
            }
        }

        internal static void ShowWindow()
        {
            // Delay opening the window since sometimes other settings in the player settings provider redirect to the
            // project validation window causing serialized objects to be nullified.
            EditorApplication.delayCall += () =>
            {
                SettingsService.OpenProjectSettings(k_ProjectValidationSettingsPath);
            };
        }

        static bool TryFindSample(string packageName, string packageVersion, string sampleDisplayName, out Sample sample)
        {
            sample = default;

            var packageSamples = Sample.FindByPackage(packageName, packageVersion);
            if (packageSamples == null)
            {
                Debug.LogError($"Couldn't find samples of the {ToString(packageName, packageVersion)} package; aborting project validation rule.");
                return false;
            }

            foreach (var packageSample in packageSamples)
            {
                if (packageSample.displayName == sampleDisplayName)
                {
                    sample = packageSample;
                    return true;
                }
            }

            Debug.LogError($"Couldn't find {sampleDisplayName} sample in the {ToString(packageName, packageVersion)} package; aborting project validation rule.");
            return false;
        }

        static string ToString(string packageName, string packageVersion)
        {
            return string.IsNullOrEmpty(packageVersion) ? packageName : $"{packageName}@{packageVersion}";
        }

    }
}
