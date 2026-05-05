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

using UnityEngine;

namespace Meta.XR
{
    public enum XrResult
    {
        Success = 0,
        TimeoutExpired = 1,
        SessionLossPending = 3,
        EventUnavailable = 4,
        SpaceBoundsUnavailable = 7,
        SessionNotFocused = 8,
        FrameDiscarded = 9,
        ErrorValidationFailure = -1,
        ErrorRuntimeFailure = -2,
        ErrorOutOfMemory = -3,
        ErrorApiVersionUnsupported = -4,
        ErrorInitializationFailed = -6,
        ErrorFunctionUnsupported = -7,
        ErrorFeatureUnsupported = -8,
        ErrorExtensionNotPresent = -9,
        ErrorLimitReached = -10,
        ErrorSizeInsufficient = -11,
        ErrorHandleInvalid = -12,
        ErrorInstanceLost = -13,
        ErrorSessionRunning = -14,
        ErrorSessionNotRunning = -16,
        ErrorSessionLost = -17,
        ErrorSystemInvalid = -18,
        ErrorPathInvalid = -19,
        ErrorPathCountExceeded = -20,
        ErrorPathFormatInvalid = -21,
        ErrorPathUnsupported = -22,
        ErrorLayerInvalid = -23,
        ErrorLayerLimitExceeded = -24,
        ErrorSwapchainRectInvalid = -25,
        ErrorSwapchainFormatUnsupported = -26,
        ErrorActionTypeMismatch = -27,
        ErrorSessionNotReady = -28,
        ErrorSessionNotStopping = -29,
        ErrorTimeInvalid = -30,
        ErrorReferenceSpaceUnsupported = -31,
        ErrorFileAccessError = -32,
        ErrorFileContentsInvalid = -33,
        ErrorFormFactorUnsupported = -34,
        ErrorFormFactorUnavailable = -35,
        ErrorApiLayerNotPresent = -36,
        ErrorCallOrderInvalid = -37,
        ErrorGraphicsDeviceInvalid = -38,
        ErrorPoseInvalid = -39,
        ErrorIndexOutOfRange = -40,
        ErrorViewConfigurationTypeUnsupported = -41,
        ErrorEnvironmentBlendModeUnsupported = -42,
        ErrorNameDuplicated = -44,
        ErrorNameInvalid = -45,
        ErrorActionsetNotAttached = -46,
        ErrorActionsetsAlreadyAttached = -47,
        ErrorLocalizedNameDuplicated = -48,
        ErrorLocalizedNameInvalid = -49,
        ErrorGraphicsRequirementsCallMissing = -50,
        ErrorRuntimeUnavailable = -51,
        ErrorExtensionDependencyNotEnabled = -1000710001,
        ErrorPermissionInsufficient = -1000710000,
        ErrorAndroidThreadSettingsIdInvalidKhr = -1000003000,
        ErrorAndroidThreadSettingsFailureKhr = -1000003001,
        ErrorCreateSpatialAnchorFailedMsft = -1000039001,
        ErrorSecondaryViewConfigurationTypeNotEnabledMsft = -1000053000,
        ErrorControllerModelKeyInvalidMsft = -1000055000,
        ErrorReprojectionModeUnsupportedMsft = -1000066000,
        ErrorComputeNewSceneNotCompletedMsft = -1000097000,
        ErrorSceneComponentIdInvalidMsft = -1000097001,
        ErrorSceneComponentTypeMismatchMsft = -1000097002,
        ErrorSceneMeshBufferIdInvalidMsft = -1000097003,
        ErrorSceneComputeFeatureIncompatibleMsft = -1000097004,
        ErrorSceneComputeConsistencyMismatchMsft = -1000097005,
        ErrorDisplayRefreshRateUnsupportedFb = -1000101000,
        ErrorColorSpaceUnsupportedFb = -1000108000,
        ErrorSpaceComponentNotSupportedFb = -1000113000,
        ErrorSpaceComponentNotEnabledFb = -1000113001,
        ErrorSpaceComponentStatusPendingFb = -1000113002,
        ErrorSpaceComponentStatusAlreadySetFb = -1000113003,
        ErrorUnexpectedStatePassthroughFb = -1000118000,
        ErrorFeatureAlreadyCreatedPassthroughFb = -1000118001,
        ErrorFeatureRequiredPassthroughFb = -1000118002,
        ErrorNotPermittedPassthroughFb = -1000118003,
        ErrorInsufficientResourcesPassthroughFb = -1000118004,
        ErrorUnknownPassthroughFb = -1000118050,
        ErrorRenderModelKeyInvalidFb = -1000119000,
        RenderModelUnavailableFb = 1000119020,
        ErrorMarkerNotTrackedVarjo = -1000124000,
        ErrorMarkerIdInvalidVarjo = -1000124001,
        ErrorMarkerDetectorPermissionDeniedMl = -1000138000,
        ErrorMarkerDetectorLocateFailedMl = -1000138001,
        ErrorMarkerDetectorInvalidDataQueryMl = -1000138002,
        ErrorMarkerDetectorInvalidCreateInfoMl = -1000138003,
        ErrorMarkerInvalidMl = -1000138004,
        ErrorLocalizationMapIncompatibleMl = -1000139000,
        ErrorLocalizationMapUnavailableMl = -1000139001,
        ErrorLocalizationMapFailMl = -1000139002,
        ErrorLocalizationMapImportExportPermissionDeniedMl = -1000139003,
        ErrorLocalizationMapPermissionDeniedMl = -1000139004,
        ErrorLocalizationMapAlreadyExistsMl = -1000139005,
        ErrorLocalizationMapCannotExportCloudMapMl = -1000139006,
        ErrorSpatialAnchorsPermissionDeniedMl = -1000140000,
        ErrorSpatialAnchorsNotLocalizedMl = -1000140001,
        ErrorSpatialAnchorsOutOfMapBoundsMl = -1000140002,
        ErrorSpatialAnchorsSpaceNotLocatableMl = -1000140003,
        ErrorSpatialAnchorsAnchorNotFoundMl = -1000141000,
        ErrorSpatialAnchorNameNotFoundMsft = -1000142001,
        ErrorSpatialAnchorNameInvalidMsft = -1000142002,
        SceneMarkerDataNotStringMsft = 1000147000,
        ErrorSpaceMappingInsufficientFb = -1000169000,
        ErrorSpaceLocalizationFailedFb = -1000169001,
        ErrorSpaceNetworkTimeoutFb = -1000169002,
        ErrorSpaceNetworkRequestFailedFb = -1000169003,
        ErrorSpaceCloudStorageDisabledFb = -1000169004,
        ErrorSpaceInsufficientResourcesMeta = -1000259000,
        ErrorSpaceStorageAtCapacityMeta = -1000259001,
        ErrorSpaceInsufficientViewMeta = -1000259002,
        ErrorSpacePermissionInsufficientMeta = -1000259003,
        ErrorSpaceRateLimitedMeta = -1000259004,
        ErrorSpaceTooDarkMeta = -1000259005,
        ErrorSpaceTooBrightMeta = -1000259006,
        ErrorPassthroughColorLutBufferSizeMismatchMeta = -1000266000,
        EnvironmentDepthNotAvailableMeta = 1000291000,
        ErrorRenderModelIdInvalidExt = -1000300000,
        ErrorRenderModelAssetUnavailableExt = -1000300001,
        ErrorRenderModelGltfExtensionRequiredExt = -1000300002,
        ErrorNotInteractionRenderModelExt = -1000301000,
        ErrorHintAlreadySetQcom = -1000306000,
        ErrorNotAnAnchorHtc = -1000319000,
        ErrorSpatialEntityIdInvalidBd = -1000389000,
        ErrorSpatialSensingServiceUnavailableBd = -1000389001,
        ErrorAnchorNotSupportedForEntityBd = -1000389002,
        ErrorSpatialAnchorNotFoundBd = -1000390000,
        ErrorSpatialAnchorSharingNetworkTimeoutBd = -1000391000,
        ErrorSpatialAnchorSharingAuthenticationFailureBd = -1000391001,
        ErrorSpatialAnchorSharingNetworkFailureBd = -1000391002,
        ErrorSpatialAnchorSharingLocalizationFailBd = -1000391003,
        ErrorSpatialAnchorSharingMapInsufficientBd = -1000391004,
        ErrorSceneCaptureFailureBd = -1000392000,
        ErrorSpaceNotLocatableExt = -1000429000,
        ErrorPlaneDetectionPermissionDeniedExt = -1000429001,
        ErrorMismatchingTrackableTypeAndroid = -1000455000,
        ErrorTrackableTypeNotSupportedAndroid = -1000455001,
        ErrorAnchorIdNotFoundAndroid = -1000457000,
        ErrorAnchorAlreadyPersistedAndroid = -1000457001,
        ErrorAnchorNotTrackingAndroid = -1000457002,
        ErrorPersistedDataNotReadyAndroid = -1000457003,
        ErrorFuturePendingExt = -1000469001,
        ErrorFutureInvalidExt = -1000469002,
        ErrorSystemNotificationPermissionDeniedMl = -1000473000,
        ErrorSystemNotificationIncompatibleSkuMl = -1000473001,
        ErrorWorldMeshDetectorPermissionDeniedMl = -1000474000,
        ErrorWorldMeshDetectorSpaceNotLocatableMl = -1000474001,
        ErrorFacialExpressionPermissionDeniedMl = 1000482000,
        ErrorColocationDiscoveryNetworkFailedMeta = -1000571001,
        ErrorColocationDiscoveryNoDiscoveryMethodMeta = -1000571002,
        ColocationDiscoveryAlreadyAdvertisingMeta = 1000571003,
        ColocationDiscoveryAlreadyDiscoveringMeta = 1000571004,
        ErrorSpaceGroupNotFoundMeta = -1000572002,
        ErrorAnchorNotOwnedByCallerAndroid = -1000701000,
        ErrorSpatialCapabilityUnsupportedExt = -1000740001,
        ErrorSpatialEntityIdInvalidExt = -1000740002,
        ErrorSpatialBufferIdInvalidExt = -1000740003,
        ErrorSpatialComponentUnsupportedForCapabilityExt = -1000740004,
        ErrorSpatialCapabilityConfigurationInvalidExt = -1000740005,
        ErrorSpatialComponentNotEnabledExt = -1000740006,
        ErrorSpatialPersistenceScopeUnsupportedExt = -1000763001,
        ErrorSpatialPersistenceScopeIncompatibleExt = -1000781001,
        ErrorExtensionDependencyNotEnabledKhr = ErrorExtensionDependencyNotEnabled,
        ErrorPermissionInsufficientKhr = ErrorPermissionInsufficient,
    }

    static partial class Extensions
    {

        static void LogError(object msg)
        {
            Debug.LogError(msg);
        }

        static void LogErrorFormat(string format, object msg)
        {
            Debug.LogErrorFormat(format, msg);
        }

        /// <summary>
        /// Logs an <see cref="XrResult"/> and returns the same value.
        /// </summary>
        /// <remarks>
        /// The log type (log, warning, or error) depends on whether the <see cref="XrResult"/> is an
        /// unqualified success (log), a qualified success (warning), or failure (error).
        /// </remarks>
        /// <param name="result">The <see cref="XrResult"/> to log.</param>
        /// <returns>Returns <paramref name="result"/>.</returns>
        public static XrResult Log(this XrResult result)
        {
            var value = (int)result;
            if (value == 0)
            {
                Debug.Log(result);
            }
            else if (value > 0)
            {
                Debug.LogWarning(result);
            }
            else
            {
                LogError(result);
            }

            return result;
        }

        public static XrResult LogFormat(this XrResult result, string format)
        {
            var value = (int)result;
            if (value == 0)
            {
                Debug.LogFormat(format, result);
            }
            else if (value > 0)
            {
                Debug.LogWarningFormat(format, result);
            }
            else
            {
                LogErrorFormat(format, result);
            }

            return result;
        }

        public static XrResult OrLog(this XrResult value)
        {
            if (value < 0)
            {
                LogError(value);
            }

            return value;
        }

        public static XrResult OrLogFormat(this XrResult value, string format)
        {
            if (value < 0)
            {
                LogErrorFormat(format, value);
            }

            return value;
        }

        public static bool IsSuccess(this XrResult value) => value >= 0;

        public static bool IsUnqualifiedSuccess(this XrResult value) => value == 0;

        public static bool IsError(this XrResult value) => value < 0;
    }
}
