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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace Meta.XR.InputActions
{
    public class RuntimeSettings : OVRRuntimeAssetsBase
    {
        [Tooltip("A list of input action definitions, which define how certain input values can be obtained from third party devices.")]
        public List<UserInputActionSet> InputActionDefinitions = new List<UserInputActionSet>();
        [Tooltip("Allows for the inclusion of Input Actions defined in an InputActionSet Serializable Object, such as those provided in third party device samples.")]
        public List<InputActionSet> InputActionSets = new List<InputActionSet>();

        internal static string InstanceAssetName = "InputActions";
        private static RuntimeSettings _instance;
        public static RuntimeSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    LoadAsset(out RuntimeSettings debugTypes, InstanceAssetName);
                    _instance = debugTypes;
                }

                return _instance;
            }
        }

#if UNITY_EDITOR
        public static string GetRuntimeActionBindings()
        {
            var instance = Instance;
            if( instance == null )
            {
                return "";
            }
            // I'd prefer to use a json serializer for this whole block, but the ones we currently use don't
            // play nicely with SerializedObjects so I'm manually building this.
            StringBuilder builder = new StringBuilder();
            builder.Append("{");
            builder.Append("\"InputActionDefinitions\": [");
            if (instance.InputActionDefinitions != null)
            {
                for (int i = 0; i < instance.InputActionDefinitions.Count; i++)
                {
                    // These ToString methods use a json serializer, which sanitizes the strings.
                    builder.Append($"{instance.InputActionDefinitions[i]}");
                    if (i < instance.InputActionDefinitions.Count - 1)
                    {
                        builder.Append(",");
                    }
                }
            }
            builder.Append("],");
            builder.Append("\"InputActionSets\": [");
            if (instance.InputActionSets != null)
            {
                for (int i = 0; i < instance.InputActionSets.Count; i++)
                {
                    // These ToString methods use a json serializer, which sanitizes the strings.
                    builder.Append($"{instance.InputActionSets[i]}");
                    if (i < instance.InputActionSets.Count - 1)
                    {
                        builder.Append(",");
                    }
                }
            }
            builder.Append("]");
            builder.Append("}");
            string str = builder.ToString();
            str = Regex.Replace(str, "[\r\n]+", "\n");
            return str;
        }

        public static void SaveToStreamingAssets()
        {
            try
            {
                string json = GetRuntimeActionBindings();
                string destinationPath = Application.streamingAssetsPath;

                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                destinationPath = Path.Combine(destinationPath, "RuntimeActionBindings.json");
                File.WriteAllText(destinationPath, json);

                // Also save to the project directory
                // to support playing in the editor.
                destinationPath = Application.dataPath;
                destinationPath = Path.GetFullPath(Path.Combine(destinationPath, "..", "RuntimeActionBindings.json"));
                File.WriteAllText(destinationPath, json);
            } catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Error saving RuntimeActionBindings: {e.Message}");
            }
        }

        public void OnValidate()
        {
            // If we've changed, update our saved json files.
            SaveToStreamingAssets();
        }

        public void InputActionSetChanged(InputActionSet actionSet)
        {
            if( InputActionSets.Contains(actionSet) )
            {
                OnValidate();
            }
        }
#endif
    }

    [System.Serializable]
    public class UserInputActionSet
    {
        [InlineLink("https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#semantic-path-interaction-profiles")]
        [Tooltip("The interaction profile of the devices these actions should be applied to.")]
        public string InteractionProfile;
        [Tooltip("A list of the different Input Actions that this device supports.")]
        public List<InputActionDefinition> InputActionDefinitions = new List<InputActionDefinition>();

        public override string ToString()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [System.Serializable]
    public class InputActionDefinition
    {
        [Tooltip("The name of this action. This is used in functions like OVRPlugin.GetActionStateBoolean to identify this specific action.")]
        public string ActionName;
        [Tooltip("The type of this action. Does it return a bool, pose, vector2, float or trigger a vibration?")]
        public OVRPlugin.ActionTypes Type;
        [Tooltip("Paths: the path from where this action will get its data. This is based on the OpenXR specification for the device.")]
        [FormerlySerializedAs("Path")]
        public string[] Paths;
    }
}
