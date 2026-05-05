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

namespace Meta.XR
{
    public static partial class Extensions
    {
        public static OVRPlugin.Sizef ToOVRPluginType(this XrExtent2Df value) => new()
        {
            w = value.Width,
            h = value.Height,
        };

        public static OVRPlugin.Vector2f ToOVRPluginType(this XrOffset2Df value) => new()
        {
            x = value.X,
            y = value.Y,
        };

        public static OVRPlugin.Rectf ToOVRPluginType(this XrRect2Df value) => new()
        {
            Pos = value.Offset.ToOVRPluginType(),
            Size = value.Extent.ToOVRPluginType(),
        };

        public static OVRPlugin.Size3f ToOVRPluginType(this XrExtent3Df value) => new()
        {
            w = value.Width,
            h = value.Height,
            d = value.Depth,
        };

        public static OVRPlugin.Vector3f ToOVRPluginType(this XrOffset3DfFB value) => new()
        {
            x = value.X,
            y = value.Y,
            z = value.Z,
        };

        public static OVRPlugin.Boundsf ToOVRPluginType(this XrRect3DfFB value) => new()
        {
            Pos = value.Offset.ToOVRPluginType(),
            Size = value.Extent.ToOVRPluginType(),
        };

        public static OVRPlugin.Quatf ToOVRPluginType(this XrQuaternionf value) => new()
        {
            x = value.X,
            y = value.Y,
            z = value.Z,
            w = value.W
        };

        public static XrQuaternionf ToXrQuaternionf(this OVRPlugin.Quatf value) => new()
        {
            X = value.x,
            Y = value.y,
            Z = value.z,
            W = value.w,
        };

        public static OVRPlugin.Vector2f ToOVRPluginType(this XrVector2f value) => new()
        {
            x = value.X,
            y = value.Y,
        };

        public static OVRPlugin.Vector3f ToOVRPluginType(this XrVector3f value) => new()
        {
            x = value.X,
            y = value.Y,
            z = value.Z,
        };

        public static XrVector3f ToXrVector3f(this OVRPlugin.Vector3f value) => new()
        {
            X = value.x,
            Y = value.y,
            Z = value.z
        };

        public static XrPosef ToXrPosef(this OVRPlugin.Posef value) => new()
        {
            Orientation = value.Orientation.ToXrQuaternionf(),
            Position = value.Position.ToXrVector3f(),
        };

        public static OVRPlugin.Posef ToOVRPluginType(this XrPosef value) => new()
        {
            Orientation = value.Orientation.ToOVRPluginType(),
            Position = value.Position.ToOVRPluginType(),
        };

        public static OVRPlugin.Result ToOVRPluginType(this XrResult value) => value switch
        {
            // Success codes
            0 => OVRPlugin.Result.Success,
            (XrResult)4 => OVRPlugin.Result.Success_EventUnavailable,
            (XrResult)1000528000 => OVRPlugin.Result.Warning_BoundaryVisibilitySuppressionNotAllowed,
            (XrResult)1000571003 => OVRPlugin.Result.Success_ColocationSessionAlreadyAdvertising,
            (XrResult)1000571004 => OVRPlugin.Result.Success_ColocationSessionAlreadyDiscovering,

            // Failure codes
            (XrResult)(-1) => OVRPlugin.Result.Failure_InvalidParameter,
            (XrResult)(-2) => OVRPlugin.Result.Failure_OperationFailed,
            (XrResult)(-4) => OVRPlugin.Result.Failure_Unsupported,
            (XrResult)(-6) => OVRPlugin.Result.Failure_ErrorInitializationFailed,
            (XrResult)(-7) => OVRPlugin.Result.Failure_Unsupported,
            (XrResult)(-8) => OVRPlugin.Result.Failure_Unsupported,
            (XrResult)(-10) => OVRPlugin.Result.Failure_ErrorLimitReached,
            (XrResult)(-11) => OVRPlugin.Result.Failure_InsufficientSize,
            (XrResult)(-12) => OVRPlugin.Result.Failure_HandleInvalid,
            (XrResult)(-16) => OVRPlugin.Result.Failure_InvalidOperation,
            (XrResult)(-31) => OVRPlugin.Result.Failure_Unsupported,
            (XrResult)(-51) => OVRPlugin.Result.Failure_RuntimeUnavailable,
            (XrResult)(-1000469002) => OVRPlugin.Result.Failure_FutureInvalid,
            (XrResult)(-1000469001) => OVRPlugin.Result.Failure_FuturePending,
            (XrResult)(-1000169004) => OVRPlugin.Result.Failure_SpaceCloudStorageDisabled,
            (XrResult)(-1000169000) => OVRPlugin.Result.Failure_SpaceMappingInsufficient,
            (XrResult)(-1000169001) => OVRPlugin.Result.Failure_SpaceLocalizationFailed,
            (XrResult)(-1000169002) => OVRPlugin.Result.Failure_SpaceNetworkTimeout,
            (XrResult)(-1000169003) => OVRPlugin.Result.Failure_SpaceNetworkRequestFailed,
            (XrResult)(-1000113000) => OVRPlugin.Result.Failure_SpaceComponentNotSupported,
            (XrResult)(-1000113001) => OVRPlugin.Result.Failure_SpaceComponentNotEnabled,
            (XrResult)(-1000113002) => OVRPlugin.Result.Failure_SpaceComponentStatusPending,
            (XrResult)(-1000113003) => OVRPlugin.Result.Failure_SpaceComponentStatusAlreadySet,
            (XrResult)(-1000571001) => OVRPlugin.Result.Failure_ColocationSessionNetworkFailed,
            (XrResult)(-1000571002) => OVRPlugin.Result.Failure_ColocationSessionNoDiscoveryMethodAvailable,
            (XrResult)(-1000572002) => OVRPlugin.Result.Failure_SpaceGroupNotFound,
            (XrResult)(-1000259000) => OVRPlugin.Result.Failure_SpaceInsufficientResources,
            (XrResult)(-1000259001) => OVRPlugin.Result.Failure_SpaceStorageAtCapacity,
            (XrResult)(-1000259002) => OVRPlugin.Result.Failure_SpaceInsufficientView,
            (XrResult)(-1000259003) => OVRPlugin.Result.Failure_SpacePermissionInsufficient,
            (XrResult)(-1000259004) => OVRPlugin.Result.Failure_SpaceRateLimited,
            (XrResult)(-1000259005) => OVRPlugin.Result.Failure_SpaceTooDark,
            (XrResult)(-1000259006) => OVRPlugin.Result.Failure_SpaceTooBright,

            // Catch-all (positive vs negative will be preserved)
            _ => (OVRPlugin.Result)value,
        };

        public static XrResult ToXrResult(this OVRPlugin.Result value) => value switch
        {
            // Success codes
            OVRPlugin.Result.Success => XrResult.Success,
            OVRPlugin.Result.Success_EventUnavailable => (XrResult)4,
            OVRPlugin.Result.Warning_BoundaryVisibilitySuppressionNotAllowed => (XrResult)1000528000,
            OVRPlugin.Result.Success_ColocationSessionAlreadyAdvertising => (XrResult)1000571003,
            OVRPlugin.Result.Success_ColocationSessionAlreadyDiscovering => (XrResult)1000571004,

            // Failure codes
            OVRPlugin.Result.Failure_InvalidParameter => (XrResult)(-1),
            OVRPlugin.Result.Failure_OperationFailed => (XrResult)(-2),
            OVRPlugin.Result.Failure_Unsupported => XrResult.ErrorFeatureUnsupported,
            OVRPlugin.Result.Failure_ErrorInitializationFailed => (XrResult)(-6),
            OVRPlugin.Result.Failure_ErrorLimitReached => (XrResult)(-10),
            OVRPlugin.Result.Failure_InsufficientSize => (XrResult)(-11),
            OVRPlugin.Result.Failure_HandleInvalid => (XrResult)(-12),
            OVRPlugin.Result.Failure_InvalidOperation => (XrResult)(-16),
            OVRPlugin.Result.Failure_RuntimeUnavailable => (XrResult)(-51),
            OVRPlugin.Result.Failure_FutureInvalid => (XrResult)(-1000469002),
            OVRPlugin.Result.Failure_FuturePending => (XrResult)(-1000469001),
            OVRPlugin.Result.Failure_SpaceCloudStorageDisabled => (XrResult)(-1000169004),
            OVRPlugin.Result.Failure_SpaceMappingInsufficient => (XrResult)(-1000169000),
            OVRPlugin.Result.Failure_SpaceLocalizationFailed => (XrResult)(-1000169001),
            OVRPlugin.Result.Failure_SpaceNetworkTimeout => (XrResult)(-1000169002),
            OVRPlugin.Result.Failure_SpaceNetworkRequestFailed => (XrResult)(-1000169003),
            OVRPlugin.Result.Failure_SpaceComponentNotSupported => (XrResult)(-1000113000),
            OVRPlugin.Result.Failure_SpaceComponentNotEnabled => (XrResult)(-1000113001),
            OVRPlugin.Result.Failure_SpaceComponentStatusPending => (XrResult)(-1000113002),
            OVRPlugin.Result.Failure_SpaceComponentStatusAlreadySet => (XrResult)(-1000113003),
            OVRPlugin.Result.Failure_ColocationSessionNetworkFailed => (XrResult)(-1000571001),
            OVRPlugin.Result.Failure_ColocationSessionNoDiscoveryMethodAvailable => (XrResult)(-1000571002),
            OVRPlugin.Result.Failure_SpaceGroupNotFound => (XrResult)(-1000572002),
            OVRPlugin.Result.Failure_SpaceInsufficientResources => (XrResult)(-1000259000),
            OVRPlugin.Result.Failure_SpaceStorageAtCapacity => (XrResult)(-1000259001),
            OVRPlugin.Result.Failure_SpaceInsufficientView => (XrResult)(-1000259002),
            OVRPlugin.Result.Failure_SpacePermissionInsufficient => (XrResult)(-1000259003),
            OVRPlugin.Result.Failure_SpaceRateLimited => (XrResult)(-1000259004),
            OVRPlugin.Result.Failure_SpaceTooDark => (XrResult)(-1000259005),
            OVRPlugin.Result.Failure_SpaceTooBright => (XrResult)(-1000259006),

            // Catch-all (positive vs negative will be preserved)
            _ => (XrResult)value,
        };
    }
}
