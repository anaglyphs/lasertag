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

namespace Meta.XR.ImmersiveDebugger
{
    /// <summary>
    /// Possible results of runtime API operations.
    /// </summary>
    public enum RuntimeAPIResult
    {
        /// <summary>The operation succeeded</summary>
        Success = OVRPlugin.Result.Success,

        /// <summary>The operation failed</summary>
        Failure = OVRPlugin.Result.Failure,

        /// <summary>An invalid parameter was supplied to the operation</summary>
        Failure_InvalidParameter = OVRPlugin.Result.Failure_InvalidParameter
    }

    /// <summary>
    /// Represents the result of a runtime API operation with detailed message information.
    /// </summary>
    public struct RuntimeAPIOperationResult
    {
        /// <summary>
        /// The status code of the operation.
        /// </summary>
        public RuntimeAPIResult Status { get; }

        /// <summary>
        /// A detailed message describing the result of the operation.
        /// This is particularly useful for AI agents that need context about what happened.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Additional context information about the operation (optional).
        /// </summary>
        public string Context { get; }

        /// <summary>
        /// Whether the operation was successful.
        /// </summary>
        public bool IsSuccess => Status == RuntimeAPIResult.Success;

        private RuntimeAPIOperationResult(RuntimeAPIResult status, string message, string context = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Context = context ?? string.Empty;
        }

        /// <summary>
        /// Creates a successful result with a message.
        /// </summary>
        public static RuntimeAPIOperationResult CreateSuccess(string message, string context = null)
        {
            return new RuntimeAPIOperationResult(RuntimeAPIResult.Success, message, context);
        }

        /// <summary>
        /// Creates a failure result with a status code and message.
        /// </summary>
        public static RuntimeAPIOperationResult CreateFailure(RuntimeAPIResult status, string message, string context = null)
        {
            return new RuntimeAPIOperationResult(status, message, context);
        }

        /// <summary>
        /// Returns a string representation of the result.
        /// </summary>
        public override string ToString()
        {
            var result = $"[{Status}] {Message}";
            if (!string.IsNullOrEmpty(Context))
            {
                result += $" | Context: {Context}";
            }
            return result;
        }
    }
}
