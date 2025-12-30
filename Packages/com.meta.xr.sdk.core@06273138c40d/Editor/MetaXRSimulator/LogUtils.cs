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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.Simulator.Editor
{
    internal class LogUtils
    {
        public virtual void ReportInfo(string title, string body)
        {
            Debug.Log($"[{title}] {body}");
        }

        public virtual void ReportWarning(string title, string body)
        {
            Debug.LogWarning($"[{title}] {body}");
        }

        public virtual void ReportError(string title, string body)
        {
            Debug.LogError($"[{title}] {body}");
        }

        public virtual void DisplayDialogOrError(string title, string body, bool forceHideDialog = false)
        {
            if (!forceHideDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(title, body, "Ok");
            }

            ReportError(title, body);
        }

        public virtual bool DisplayDialog(string title, string body, string okButtonText, string cancelButtonText)
        {
            if (!Application.isBatchMode)
            {
                return EditorUtility.DisplayDialog(title, body, okButtonText, cancelButtonText);
            }
            return false;
        }

        public virtual int CreateProgress(string title, bool shouldReposition)
        {
            int progressId = Progress.Start(title);
            if (!Application.isBatchMode)
            {
                Progress.ShowDetails(shouldReposition);
            }
            return progressId;
        }
    }

}
