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
    public enum XrSession : ulong { }

    public enum XrInstance : ulong { }

    public enum XrSystemId : ulong { }

    public enum XrPath : ulong { }

    public enum XrSpace : ulong { }

    public enum XrAction : ulong { }

    public enum XrSwapchain : ulong { }

    public enum XrActionSet : ulong { }

    public enum XrStructureType
    {
        Unknown = 0,
        ApiLayerProperties = 1,
        ExtensionProperties = 2,
        InstanceCreateInfo = 3,
        SystemGetInfo = 4,
        SystemProperties = 5,
        ViewLocateInfo = 6,
        View = 7,
        SessionCreateInfo = 8,
        SwapchainCreateInfo = 9,
        SessionBeginInfo = 10,
        ViewState = 11,
        FrameEndInfo = 12,
        HapticVibration = 13,
        EventDataBuffer = 16,
        EventDataInstanceLossPending = 17,
        EventDataSessionStateChanged = 18,
        ActionStateBoolean = 23,
        ActionStateFloat = 24,
        ActionStateVector2f = 25,
        ActionStatePose = 27,
        ActionSetCreateInfo = 28,
        ActionCreateInfo = 29,
        InstanceProperties = 32,
        FrameWaitInfo = 33,
        CompositionLayerProjection = 35,
        CompositionLayerQuad = 36,
        ReferenceSpaceCreateInfo = 37,
        ActionSpaceCreateInfo = 38,
        EventDataReferenceSpaceChangePending = 40,
        ViewConfigurationView = 41,
        SpaceLocation = 42,
        SpaceVelocity = 43,
        FrameState = 44,
        ViewConfigurationProperties = 45,
        FrameBeginInfo = 46,
        CompositionLayerProjectionView = 48,
        EventDataEventsLost = 49,
        InteractionProfileSuggestedBinding = 51,
        EventDataInteractionProfileChanged = 52,
        InteractionProfileState = 53,
        SwapchainImageAcquireInfo = 55,
        SwapchainImageWaitInfo = 56,
        SwapchainImageReleaseInfo = 57,
        ActionStateGetInfo = 58,
        HapticActionInfo = 59,
        SessionActionSetsAttachInfo = 60,
        ActionsSyncInfo = 61,
        BoundSourcesForActionEnumerateInfo = 62,
        InputSourceLocalizedNameGetInfo = 63,
        SpacesLocateInfo = 1000471000,
        SpaceLocations = 1000471001,
        SpaceVelocities = 1000471002,
        CompositionLayerCubeKhr = 1000006000,
        InstanceCreateInfoAndroidKhr = 1000008000,
        CompositionLayerDepthInfoKhr = 1000010000,
        VulkanSwapchainFormatListCreateInfoKhr = 1000014000,
        EventDataPerfSettingsExt = 1000015000,
        CompositionLayerCylinderKhr = 1000017000,
        CompositionLayerEquirectKhr = 1000018000,
        DebugUtilsObjectNameInfoExt = 1000019000,
        DebugUtilsMessengerCallbackDataExt = 1000019001,
        DebugUtilsMessengerCreateInfoExt = 1000019002,
        DebugUtilsLabelExt = 1000019003,
        GraphicsBindingOpenglWin32Khr = 1000023000,
        GraphicsBindingOpenglXlibKhr = 1000023001,
        GraphicsBindingOpenglXcbKhr = 1000023002,
        GraphicsBindingOpenglWaylandKhr = 1000023003,
        SwapchainImageOpenglKhr = 1000023004,
        GraphicsRequirementsOpenglKhr = 1000023005,
        GraphicsBindingOpenglEsAndroidKhr = 1000024001,
        SwapchainImageOpenglEsKhr = 1000024002,
        GraphicsRequirementsOpenglEsKhr = 1000024003,
        GraphicsBindingVulkanKhr = 1000025000,
        SwapchainImageVulkanKhr = 1000025001,
        GraphicsRequirementsVulkanKhr = 1000025002,
        GraphicsBindingD3d11Khr = 1000027000,
        SwapchainImageD3d11Khr = 1000027001,
        GraphicsRequirementsD3d11Khr = 1000027002,
        GraphicsBindingD3d12Khr = 1000028000,
        SwapchainImageD3d12Khr = 1000028001,
        GraphicsRequirementsD3d12Khr = 1000028002,
        GraphicsBindingMetalKhr = 1000029000,
        SwapchainImageMetalKhr = 1000029001,
        GraphicsRequirementsMetalKhr = 1000029002,
        SystemEyeGazeInteractionPropertiesExt = 1000030000,
        EyeGazeSampleTimeExt = 1000030001,
        VisibilityMaskKhr = 1000031000,
        EventDataVisibilityMaskChangedKhr = 1000031001,
        SessionCreateInfoOverlayExtx = 1000033000,
        EventDataMainSessionVisibilityChangedExtx = 1000033003,
        CompositionLayerColorScaleBiasKhr = 1000034000,
        SpatialAnchorCreateInfoMsft = 1000039000,
        SpatialAnchorSpaceCreateInfoMsft = 1000039001,
        CompositionLayerImageLayoutFb = 1000040000,
        CompositionLayerAlphaBlendFb = 1000041001,
        ViewConfigurationDepthRangeExt = 1000046000,
        GraphicsBindingEglMndx = 1000048004,
        SpatialGraphNodeSpaceCreateInfoMsft = 1000049000,
        SpatialGraphStaticNodeBindingCreateInfoMsft = 1000049001,
        SpatialGraphNodeBindingPropertiesGetInfoMsft = 1000049002,
        SpatialGraphNodeBindingPropertiesMsft = 1000049003,
        SystemHandTrackingPropertiesExt = 1000051000,
        HandTrackerCreateInfoExt = 1000051001,
        HandJointsLocateInfoExt = 1000051002,
        HandJointLocationsExt = 1000051003,
        HandJointVelocitiesExt = 1000051004,
        SystemHandTrackingMeshPropertiesMsft = 1000052000,
        HandMeshSpaceCreateInfoMsft = 1000052001,
        HandMeshUpdateInfoMsft = 1000052002,
        HandMeshMsft = 1000052003,
        HandPoseTypeInfoMsft = 1000052004,
        SecondaryViewConfigurationSessionBeginInfoMsft = 1000053000,
        SecondaryViewConfigurationStateMsft = 1000053001,
        SecondaryViewConfigurationFrameStateMsft = 1000053002,
        SecondaryViewConfigurationFrameEndInfoMsft = 1000053003,
        SecondaryViewConfigurationLayerInfoMsft = 1000053004,
        SecondaryViewConfigurationSwapchainCreateInfoMsft = 1000053005,
        ControllerModelKeyStateMsft = 1000055000,
        ControllerModelNodePropertiesMsft = 1000055001,
        ControllerModelPropertiesMsft = 1000055002,
        ControllerModelNodeStateMsft = 1000055003,
        ControllerModelStateMsft = 1000055004,
        ViewConfigurationViewFovEpic = 1000059000,
        HolographicWindowAttachmentMsft = 1000063000,
        CompositionLayerReprojectionInfoMsft = 1000066000,
        CompositionLayerReprojectionPlaneOverrideMsft = 1000066001,
        AndroidSurfaceSwapchainCreateInfoFb = 1000070000,
        CompositionLayerSecureContentFb = 1000072000,
        BodyTrackerCreateInfoFb = 1000076001,
        BodyJointsLocateInfoFb = 1000076002,
        SystemBodyTrackingPropertiesFb = 1000076004,
        BodyJointLocationsFb = 1000076005,
        BodySkeletonFb = 1000076006,
        InteractionProfileDpadBindingExt = 1000078000,
        InteractionProfileAnalogThresholdValve = 1000079000,
        HandJointsMotionRangeInfoExt = 1000080000,
        LoaderInitInfoAndroidKhr = 1000089000,
        VulkanInstanceCreateInfoKhr = 1000090000,
        VulkanDeviceCreateInfoKhr = 1000090001,
        VulkanGraphicsDeviceGetInfoKhr = 1000090003,
        CompositionLayerEquirect2Khr = 1000091000,
        SceneObserverCreateInfoMsft = 1000097000,
        SceneCreateInfoMsft = 1000097001,
        NewSceneComputeInfoMsft = 1000097002,
        VisualMeshComputeLodInfoMsft = 1000097003,
        SceneComponentsMsft = 1000097004,
        SceneComponentsGetInfoMsft = 1000097005,
        SceneComponentLocationsMsft = 1000097006,
        SceneComponentsLocateInfoMsft = 1000097007,
        SceneObjectsMsft = 1000097008,
        SceneComponentParentFilterInfoMsft = 1000097009,
        SceneObjectTypesFilterInfoMsft = 1000097010,
        ScenePlanesMsft = 1000097011,
        ScenePlaneAlignmentFilterInfoMsft = 1000097012,
        SceneMeshesMsft = 1000097013,
        SceneMeshBuffersGetInfoMsft = 1000097014,
        SceneMeshBuffersMsft = 1000097015,
        SceneMeshVertexBufferMsft = 1000097016,
        SceneMeshIndicesUint32Msft = 1000097017,
        SceneMeshIndicesUint16Msft = 1000097018,
        SerializedSceneFragmentDataGetInfoMsft = 1000098000,
        SceneDeserializeInfoMsft = 1000098001,
        EventDataDisplayRefreshRateChangedFb = 1000101000,
        ViveTrackerPathsHtcx = 1000103000,
        EventDataViveTrackerConnectedHtcx = 1000103001,
        SystemFacialTrackingPropertiesHtc = 1000104000,
        FacialTrackerCreateInfoHtc = 1000104001,
        FacialExpressionsHtc = 1000104002,
        SystemColorSpacePropertiesFb = 1000108000,
        HandTrackingMeshFb = 1000110001,
        HandTrackingScaleFb = 1000110003,
        HandTrackingAimStateFb = 1000111001,
        HandTrackingCapsulesStateFb = 1000112000,
        SystemSpatialEntityPropertiesFb = 1000113004,
        SpatialAnchorCreateInfoFb = 1000113003,
        SpaceComponentStatusSetInfoFb = 1000113007,
        SpaceComponentStatusFb = 1000113001,
        EventDataSpatialAnchorCreateCompleteFb = 1000113005,
        EventDataSpaceSetStatusCompleteFb = 1000113006,
        FoveationProfileCreateInfoFb = 1000114000,
        SwapchainCreateInfoFoveationFb = 1000114001,
        SwapchainStateFoveationFb = 1000114002,
        FoveationLevelProfileCreateInfoFb = 1000115000,
        KeyboardSpaceCreateInfoFb = 1000116009,
        KeyboardTrackingQueryFb = 1000116004,
        SystemKeyboardTrackingPropertiesFb = 1000116002,
        TriangleMeshCreateInfoFb = 1000117001,
        SystemPassthroughPropertiesFb = 1000118000,
        PassthroughCreateInfoFb = 1000118001,
        PassthroughLayerCreateInfoFb = 1000118002,
        CompositionLayerPassthroughFb = 1000118003,
        GeometryInstanceCreateInfoFb = 1000118004,
        GeometryInstanceTransformFb = 1000118005,
        SystemPassthroughProperties2Fb = 1000118006,
        PassthroughStyleFb = 1000118020,
        PassthroughColorMapMonoToRgbaFb = 1000118021,
        PassthroughColorMapMonoToMonoFb = 1000118022,
        PassthroughBrightnessContrastSaturationFb = 1000118023,
        EventDataPassthroughStateChangedFb = 1000118030,
        RenderModelPathInfoFb = 1000119000,
        RenderModelPropertiesFb = 1000119001,
        RenderModelBufferFb = 1000119002,
        RenderModelLoadInfoFb = 1000119003,
        SystemRenderModelPropertiesFb = 1000119004,
        RenderModelCapabilitiesRequestFb = 1000119005,
        BindingModificationsKhr = 1000120000,
        ViewLocateFoveatedRenderingVarjo = 1000121000,
        FoveatedViewConfigurationViewVarjo = 1000121001,
        SystemFoveatedRenderingPropertiesVarjo = 1000121002,
        CompositionLayerDepthTestVarjo = 1000122000,
        SystemMarkerTrackingPropertiesVarjo = 1000124000,
        EventDataMarkerTrackingUpdateVarjo = 1000124001,
        MarkerSpaceCreateInfoVarjo = 1000124002,
        FrameEndInfoMl = 1000135000,
        GlobalDimmerFrameEndInfoMl = 1000136000,
        CoordinateSpaceCreateInfoMl = 1000137000,
        SystemMarkerUnderstandingPropertiesMl = 1000138000,
        MarkerDetectorCreateInfoMl = 1000138001,
        MarkerDetectorArucoInfoMl = 1000138002,
        MarkerDetectorSizeInfoMl = 1000138003,
        MarkerDetectorAprilTagInfoMl = 1000138004,
        MarkerDetectorCustomProfileInfoMl = 1000138005,
        MarkerDetectorSnapshotInfoMl = 1000138006,
        MarkerDetectorStateMl = 1000138007,
        MarkerSpaceCreateInfoMl = 1000138008,
        LocalizationMapMl = 1000139000,
        EventDataLocalizationChangedMl = 1000139001,
        MapLocalizationRequestInfoMl = 1000139002,
        LocalizationMapImportInfoMl = 1000139003,
        LocalizationEnableEventsInfoMl = 1000139004,
        SpatialAnchorsCreateInfoFromPoseMl = 1000140000,
        CreateSpatialAnchorsCompletionMl = 1000140001,
        SpatialAnchorStateMl = 1000140002,
        SpatialAnchorsCreateStorageInfoMl = 1000141000,
        SpatialAnchorsQueryInfoRadiusMl = 1000141001,
        SpatialAnchorsQueryCompletionMl = 1000141002,
        SpatialAnchorsCreateInfoFromUuidsMl = 1000141003,
        SpatialAnchorsPublishInfoMl = 1000141004,
        SpatialAnchorsPublishCompletionMl = 1000141005,
        SpatialAnchorsDeleteInfoMl = 1000141006,
        SpatialAnchorsDeleteCompletionMl = 1000141007,
        SpatialAnchorsUpdateExpirationInfoMl = 1000141008,
        SpatialAnchorsUpdateExpirationCompletionMl = 1000141009,
        SpatialAnchorsPublishCompletionDetailsMl = 1000141010,
        SpatialAnchorsDeleteCompletionDetailsMl = 1000141011,
        SpatialAnchorsUpdateExpirationCompletionDetailsMl = 1000141012,
        EventDataHeadsetFitChangedMl = 1000472000,
        EventDataEyeCalibrationChangedMl = 1000472001,
        UserCalibrationEnableEventsInfoMl = 1000472002,
        SpatialAnchorPersistenceInfoMsft = 1000142000,
        SpatialAnchorFromPersistedAnchorCreateInfoMsft = 1000142001,
        SceneMarkersMsft = 1000147000,
        SceneMarkerTypeFilterMsft = 1000147001,
        SceneMarkerQrCodesMsft = 1000147002,
        SpaceQueryInfoFb = 1000156001,
        SpaceQueryResultsFb = 1000156002,
        SpaceStorageLocationFilterInfoFb = 1000156003,
        SpaceUuidFilterInfoFb = 1000156054,
        SpaceComponentFilterInfoFb = 1000156052,
        EventDataSpaceQueryResultsAvailableFb = 1000156103,
        EventDataSpaceQueryCompleteFb = 1000156104,
        SpaceSaveInfoFb = 1000158000,
        SpaceEraseInfoFb = 1000158001,
        EventDataSpaceSaveCompleteFb = 1000158106,
        EventDataSpaceEraseCompleteFb = 1000158107,
        SwapchainImageFoveationVulkanFb = 1000160000,
        SwapchainStateAndroidSurfaceDimensionsFb = 1000161000,
        SwapchainStateSamplerOpenglEsFb = 1000162000,
        SwapchainStateSamplerVulkanFb = 1000163000,
        SpaceShareInfoFb = 1000169001,
        EventDataSpaceShareCompleteFb = 1000169002,
        CompositionLayerSpaceWarpInfoFb = 1000171000,
        SystemSpaceWarpPropertiesFb = 1000171001,
        HapticAmplitudeEnvelopeVibrationFb = 1000173001,
        SemanticLabelsFb = 1000175000,
        RoomLayoutFb = 1000175001,
        Boundary2dFb = 1000175002,
        SemanticLabelsSupportInfoFb = 1000175010,
        DigitalLensControlAlmalence = 1000196000,
        EventDataSceneCaptureCompleteFb = 1000198001,
        SceneCaptureRequestInfoFb = 1000198050,
        SpaceContainerFb = 1000199000,
        FoveationEyeTrackedProfileCreateInfoMeta = 1000200000,
        FoveationEyeTrackedStateMeta = 1000200001,
        SystemFoveationEyeTrackedPropertiesMeta = 1000200002,
        SystemFaceTrackingPropertiesFb = 1000201004,
        FaceTrackerCreateInfoFb = 1000201005,
        FaceExpressionInfoFb = 1000201002,
        FaceExpressionWeightsFb = 1000201006,
        EyeTrackerCreateInfoFb = 1000202001,
        EyeGazesInfoFb = 1000202002,
        EyeGazesFb = 1000202003,
        SystemEyeTrackingPropertiesFb = 1000202004,
        PassthroughKeyboardHandsIntensityFb = 1000203002,
        CompositionLayerSettingsFb = 1000204000,
        HapticPcmVibrationFb = 1000209001,
        DevicePcmSampleRateStateFb = 1000209002,
        FrameSynthesisInfoExt = 1000211000,
        FrameSynthesisConfigViewExt = 1000211001,
        CompositionLayerDepthTestFb = 1000212000,
        LocalDimmingFrameEndInfoMeta = 1000216000,
        PassthroughPreferencesMeta = 1000217000,
        ExternalCameraOculus = 1000226000,
        VulkanSwapchainCreateInfoMeta = 1000227000,
        PerformanceMetricsStateMeta = 1000232001,
        PerformanceMetricsCounterMeta = 1000232002,
        SpaceListSaveInfoFb = 1000238000,
        EventDataSpaceListSaveCompleteFb = 1000238001,
        SpaceUserCreateInfoFb = 1000241001,
        SystemHeadsetIdPropertiesMeta = 1000245000,
        SystemSpaceDiscoveryPropertiesMeta = 1000247000,
        SpaceDiscoveryInfoMeta = 1000247001,
        SpaceFilterUuidMeta = 1000247003,
        SpaceFilterComponentMeta = 1000247004,
        SpaceDiscoveryResultMeta = 1000247005,
        SpaceDiscoveryResultsMeta = 1000247006,
        EventDataSpaceDiscoveryResultsAvailableMeta = 1000247007,
        EventDataSpaceDiscoveryCompleteMeta = 1000247008,
        RecommendedLayerResolutionMeta = 1000254000,
        RecommendedLayerResolutionGetInfoMeta = 1000254001,
        SystemSpacePersistencePropertiesMeta = 1000259000,
        SpacesSaveInfoMeta = 1000259001,
        EventDataSpacesSaveResultMeta = 1000259002,
        SpacesEraseInfoMeta = 1000259003,
        EventDataSpacesEraseResultMeta = 1000259004,
        SystemPassthroughColorLutPropertiesMeta = 1000266000,
        PassthroughColorLutCreateInfoMeta = 1000266001,
        PassthroughColorLutUpdateInfoMeta = 1000266002,
        PassthroughColorMapLutMeta = 1000266100,
        PassthroughColorMapInterpolatedLutMeta = 1000266101,
        SpaceTriangleMeshGetInfoMeta = 1000269001,
        SpaceTriangleMeshMeta = 1000269002,
        SystemPropertiesBodyTrackingFullBodyMeta = 1000274000,
        EventDataPassthroughLayerResumedMeta = 1000282000,
        BodyTrackingCalibrationInfoMeta = 1000283002,
        BodyTrackingCalibrationStatusMeta = 1000283003,
        SystemPropertiesBodyTrackingCalibrationMeta = 1000283004,
        SystemFaceTrackingProperties2Fb = 1000287013,
        FaceTrackerCreateInfo2Fb = 1000287014,
        FaceExpressionInfo2Fb = 1000287015,
        FaceExpressionWeights2Fb = 1000287016,
        SystemSpatialEntitySharingPropertiesMeta = 1000290000,
        ShareSpacesInfoMeta = 1000290001,
        EventDataShareSpacesCompleteMeta = 1000290002,
        EnvironmentDepthProviderCreateInfoMeta = 1000291000,
        EnvironmentDepthSwapchainCreateInfoMeta = 1000291001,
        EnvironmentDepthSwapchainStateMeta = 1000291002,
        EnvironmentDepthImageAcquireInfoMeta = 1000291003,
        EnvironmentDepthImageViewMeta = 1000291004,
        EnvironmentDepthImageMeta = 1000291005,
        EnvironmentDepthHandRemovalSetInfoMeta = 1000291006,
        SystemEnvironmentDepthPropertiesMeta = 1000291007,
        RenderModelCreateInfoExt = 1000300000,
        RenderModelPropertiesGetInfoExt = 1000300001,
        RenderModelPropertiesExt = 1000300002,
        RenderModelSpaceCreateInfoExt = 1000300003,
        RenderModelStateGetInfoExt = 1000300004,
        RenderModelStateExt = 1000300005,
        RenderModelAssetCreateInfoExt = 1000300006,
        RenderModelAssetDataGetInfoExt = 1000300007,
        RenderModelAssetDataExt = 1000300008,
        RenderModelAssetPropertiesGetInfoExt = 1000300009,
        RenderModelAssetPropertiesExt = 1000300010,
        InteractionRenderModelIdsEnumerateInfoExt = 1000301000,
        InteractionRenderModelSubactionPathInfoExt = 1000301001,
        EventDataInteractionRenderModelsChangedExt = 1000301002,
        InteractionRenderModelTopLevelUserPathGetInfoExt = 1000301003,
        PassthroughCreateInfoHtc = 1000317001,
        PassthroughColorHtc = 1000317002,
        PassthroughMeshTransformInfoHtc = 1000317003,
        CompositionLayerPassthroughHtc = 1000317004,
        FoveationApplyInfoHtc = 1000318000,
        FoveationDynamicModeInfoHtc = 1000318001,
        FoveationCustomModeInfoHtc = 1000318002,
        SystemAnchorPropertiesHtc = 1000319000,
        SpatialAnchorCreateInfoHtc = 1000319001,
        SystemBodyTrackingPropertiesHtc = 1000320000,
        BodyTrackerCreateInfoHtc = 1000320001,
        BodyJointsLocateInfoHtc = 1000320002,
        BodyJointLocationsHtc = 1000320003,
        BodySkeletonHtc = 1000320004,
        ActiveActionSetPrioritiesExt = 1000373000,
        SystemForceFeedbackCurlPropertiesMndx = 1000375000,
        ForceFeedbackCurlApplyLocationsMndx = 1000375001,
        BodyTrackerCreateInfoBd = 1000385001,
        BodyJointsLocateInfoBd = 1000385002,
        BodyJointLocationsBd = 1000385003,
        SystemBodyTrackingPropertiesBd = 1000385004,
        SystemFacialSimulationPropertiesBd = 1000386001,
        FaceTrackerCreateInfoBd = 1000386002,
        FacialSimulationDataGetInfoBd = 1000386003,
        FacialSimulationDataBd = 1000386004,
        LipExpressionDataBd = 1000386005,
        SystemSpatialSensingPropertiesBd = 1000389000,
        SpatialEntityComponentGetInfoBd = 1000389001,
        SpatialEntityLocationGetInfoBd = 1000389002,
        SpatialEntityComponentDataLocationBd = 1000389003,
        SpatialEntityComponentDataSemanticBd = 1000389004,
        SpatialEntityComponentDataBoundingBox2dBd = 1000389005,
        SpatialEntityComponentDataPolygonBd = 1000389006,
        SpatialEntityComponentDataBoundingBox3dBd = 1000389007,
        SpatialEntityComponentDataTriangleMeshBd = 1000389008,
        SenseDataProviderCreateInfoBd = 1000389009,
        SenseDataProviderStartInfoBd = 1000389010,
        EventDataSenseDataProviderStateChangedBd = 1000389011,
        EventDataSenseDataUpdatedBd = 1000389012,
        SenseDataQueryInfoBd = 1000389013,
        SenseDataQueryCompletionBd = 1000389014,
        SenseDataFilterUuidBd = 1000389015,
        SenseDataFilterSemanticBd = 1000389016,
        QueriedSenseDataGetInfoBd = 1000389017,
        QueriedSenseDataBd = 1000389018,
        SpatialEntityStateBd = 1000389019,
        SpatialEntityAnchorCreateInfoBd = 1000389020,
        AnchorSpaceCreateInfoBd = 1000389021,
        SystemSpatialAnchorPropertiesBd = 1000390000,
        SpatialAnchorCreateInfoBd = 1000390001,
        SpatialAnchorCreateCompletionBd = 1000390002,
        SpatialAnchorPersistInfoBd = 1000390003,
        SpatialAnchorUnpersistInfoBd = 1000390004,
        SystemSpatialAnchorSharingPropertiesBd = 1000391000,
        SpatialAnchorShareInfoBd = 1000391001,
        SharedSpatialAnchorDownloadInfoBd = 1000391002,
        SystemSpatialScenePropertiesBd = 1000392000,
        SceneCaptureInfoBd = 1000392001,
        SystemSpatialMeshPropertiesBd = 1000393000,
        SenseDataProviderCreateInfoSpatialMeshBd = 1000393001,
        FuturePollResultProgressBd = 1000394001,
        SystemSpatialPlanePropertiesBd = 1000396000,
        SpatialEntityComponentDataPlaneOrientationBd = 1000396001,
        SenseDataFilterPlaneOrientationBd = 1000396002,
        HandTrackingDataSourceInfoExt = 1000428000,
        HandTrackingDataSourceStateExt = 1000428001,
        PlaneDetectorCreateInfoExt = 1000429001,
        PlaneDetectorBeginInfoExt = 1000429002,
        PlaneDetectorGetInfoExt = 1000429003,
        PlaneDetectorLocationsExt = 1000429004,
        PlaneDetectorLocationExt = 1000429005,
        PlaneDetectorPolygonBufferExt = 1000429006,
        SystemPlaneDetectionPropertiesExt = 1000429007,
        TrackableGetInfoAndroid = 1000455000,
        AnchorSpaceCreateInfoAndroid = 1000455001,
        TrackablePlaneAndroid = 1000455003,
        TrackableTrackerCreateInfoAndroid = 1000455004,
        SystemTrackablesPropertiesAndroid = 1000455005,
        PersistedAnchorSpaceCreateInfoAndroid = 1000457001,
        PersistedAnchorSpaceInfoAndroid = 1000457002,
        DeviceAnchorPersistenceCreateInfoAndroid = 1000457003,
        SystemDeviceAnchorPersistencePropertiesAndroid = 1000457004,
        FaceTrackerCreateInfoAndroid = 1000458000,
        FaceStateGetInfoAndroid = 1000458001,
        FaceStateAndroid = 1000458002,
        SystemFaceTrackingPropertiesAndroid = 1000458003,
        PassthroughCameraStateGetInfoAndroid = 1000460000,
        SystemPassthroughCameraStatePropertiesAndroid = 1000460001,
        RaycastInfoAndroid = 1000463000,
        RaycastHitResultsAndroid = 1000463001,
        TrackableObjectAndroid = 1000466000,
        TrackableObjectConfigurationAndroid = 1000466001,
        FutureCancelInfoExt = 1000469000,
        FuturePollInfoExt = 1000469001,
        FutureCompletionExt = 1000469002,
        FuturePollResultExt = 1000469003,
        EventDataUserPresenceChangedExt = 1000470000,
        SystemUserPresencePropertiesExt = 1000470001,
        SystemNotificationsSetInfoMl = 1000473000,
        WorldMeshDetectorCreateInfoMl = 1000474001,
        WorldMeshStateRequestInfoMl = 1000474002,
        WorldMeshBlockStateMl = 1000474003,
        WorldMeshStateRequestCompletionMl = 1000474004,
        WorldMeshBufferRecommendedSizeInfoMl = 1000474005,
        WorldMeshBufferSizeMl = 1000474006,
        WorldMeshBufferMl = 1000474007,
        WorldMeshBlockRequestMl = 1000474008,
        WorldMeshGetInfoMl = 1000474009,
        WorldMeshBlockMl = 1000474010,
        WorldMeshRequestCompletionMl = 1000474011,
        WorldMeshRequestCompletionInfoMl = 1000474012,
        SystemFacialExpressionPropertiesMl = 1000482004,
        FacialExpressionClientCreateInfoMl = 1000482005,
        FacialExpressionBlendShapeGetInfoMl = 1000482006,
        FacialExpressionBlendShapePropertiesMl = 1000482007,
        SystemSimultaneousHandsAndControllersPropertiesMeta = 1000532001,
        SimultaneousHandsAndControllersTrackingResumeInfoMeta = 1000532002,
        SimultaneousHandsAndControllersTrackingPauseInfoMeta = 1000532003,
        ColocationDiscoveryStartInfoMeta = 1000571010,
        ColocationDiscoveryStopInfoMeta = 1000571011,
        ColocationAdvertisementStartInfoMeta = 1000571012,
        ColocationAdvertisementStopInfoMeta = 1000571013,
        EventDataStartColocationAdvertisementCompleteMeta = 1000571020,
        EventDataStopColocationAdvertisementCompleteMeta = 1000571021,
        EventDataColocationAdvertisementCompleteMeta = 1000571022,
        EventDataStartColocationDiscoveryCompleteMeta = 1000571023,
        EventDataColocationDiscoveryResultMeta = 1000571024,
        EventDataColocationDiscoveryCompleteMeta = 1000571025,
        EventDataStopColocationDiscoveryCompleteMeta = 1000571026,
        SystemColocationDiscoveryPropertiesMeta = 1000571030,
        ShareSpacesRecipientGroupsMeta = 1000572000,
        SpaceGroupUuidFilterInfoMeta = 1000572001,
        SystemSpatialEntityGroupSharingPropertiesMeta = 1000572100,
        AnchorSharingInfoAndroid = 1000701000,
        AnchorSharingTokenAndroid = 1000701001,
        SystemAnchorSharingExportPropertiesAndroid = 1000701002,
        SystemMarkerTrackingPropertiesAndroid = 1000707000,
        TrackableMarkerConfigurationAndroid = 1000707001,
        TrackableMarkerAndroid = 1000707002,
        SpatialCapabilityComponentTypesExt = 1000740000,
        SpatialContextCreateInfoExt = 1000740001,
        CreateSpatialContextCompletionExt = 1000740002,
        SpatialDiscoverySnapshotCreateInfoExt = 1000740003,
        CreateSpatialDiscoverySnapshotCompletionInfoExt = 1000740004,
        CreateSpatialDiscoverySnapshotCompletionExt = 1000740005,
        SpatialComponentDataQueryConditionExt = 1000740006,
        SpatialComponentDataQueryResultExt = 1000740007,
        SpatialBufferGetInfoExt = 1000740008,
        SpatialComponentBounded2dListExt = 1000740009,
        SpatialComponentBounded3dListExt = 1000740010,
        SpatialComponentParentListExt = 1000740011,
        SpatialComponentMesh3dListExt = 1000740012,
        SpatialEntityFromIdCreateInfoExt = 1000740013,
        SpatialUpdateSnapshotCreateInfoExt = 1000740014,
        EventDataSpatialDiscoveryRecommendedExt = 1000740015,
        SpatialFilterTrackingStateExt = 1000740016,
        SpatialCapabilityConfigurationPlaneTrackingExt = 1000741000,
        SpatialComponentPlaneAlignmentListExt = 1000741001,
        SpatialComponentMesh2dListExt = 1000741002,
        SpatialComponentPolygon2dListExt = 1000741003,
        SpatialComponentPlaneSemanticLabelListExt = 1000741004,
        SpatialCapabilityConfigurationQrCodeExt = 1000743000,
        SpatialCapabilityConfigurationMicroQrCodeExt = 1000743001,
        SpatialCapabilityConfigurationArucoMarkerExt = 1000743002,
        SpatialCapabilityConfigurationAprilTagExt = 1000743003,
        SpatialMarkerSizeExt = 1000743004,
        SpatialMarkerStaticOptimizationExt = 1000743005,
        SpatialComponentMarkerListExt = 1000743006,
        SpatialCapabilityConfigurationAnchorExt = 1000762000,
        SpatialComponentAnchorListExt = 1000762001,
        SpatialAnchorCreateInfoExt = 1000762002,
        SpatialPersistenceContextCreateInfoExt = 1000763000,
        CreateSpatialPersistenceContextCompletionExt = 1000763001,
        SpatialContextPersistenceConfigExt = 1000763002,
        SpatialDiscoveryPersistenceUuidFilterExt = 1000763003,
        SpatialComponentPersistenceListExt = 1000763004,
        SpatialEntityPersistInfoExt = 1000781000,
        PersistSpatialEntityCompletionExt = 1000781001,
        SpatialEntityUnpersistInfoExt = 1000781002,
        UnpersistSpatialEntityCompletionExt = 1000781003,
        LoaderInitInfoPropertiesExt = 1000838000,
        GraphicsBindingVulkan2Khr = GraphicsBindingVulkanKhr,
        SwapchainImageVulkan2Khr = SwapchainImageVulkanKhr,
        GraphicsRequirementsVulkan2Khr = GraphicsRequirementsVulkanKhr,
        DevicePcmSampleRateGetInfoFb = DevicePcmSampleRateStateFb,
        SpacesLocateInfoKhr = SpacesLocateInfo,
        SpaceLocationsKhr = SpaceLocations,
        SpaceVelocitiesKhr = SpaceVelocities,
    }

    public enum XrFormFactor
    {
        HeadMountedDisplay = 1,
        HandheldDisplay = 2,
    }

    public struct XrVector2f
    {
        public float X;
        public float Y;
    }

    public struct XrVector3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    public struct XrQuaternionf
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    public struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;
    }

    public struct XrExtent2Df
    {
        public float Width;
        public float Height;
    }

    public struct XrOffset2Df
    {
        public float X;
        public float Y;
    }

    public struct XrRect2Df
    {
        public XrOffset2Df Offset;
        public XrExtent2Df Extent;
    }

    public struct XrBaseStructure
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public struct XrSystemGetInfo
    {
        // XR_TYPE_SYSTEM_GET_INFO = 4,
        public const XrStructureType StructureType = (XrStructureType)4;

        public XrStructureType Type;
        public unsafe void* Next;
        public XrFormFactor FormFactor;
    }

    public struct XrSpaceLocation
    {
        // XR_TYPE_SPACE_LOCATION = 42,
        public const XrStructureType StructureType = (XrStructureType)42;

        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceLocationFlags LocationFlags;
        public XrPosef Pose;
    }

    public struct XrSystemGraphicsProperties
    {
        public uint MaxSwapchainImageHeight;
        public uint MaxSwapchainImageWidth;
        public uint MaxLayerCount;
    }

    public struct XrSystemTrackingProperties
    {
        public XrBool32 OrientationTracking;
        public XrBool32 PositionTracking;
    }

    public struct XrSystemProperties
    {
        public const int MaxSystemNameSize = 256;
        public const XrStructureType StructureType = (XrStructureType)5;

        public XrStructureType Type;
        public unsafe void* Next;
        public XrSystemId SystemId;
        public uint VendorId;
        public unsafe fixed byte SystemName[MaxSystemNameSize];
        public XrSystemGraphicsProperties GraphicsProperties;
        public XrSystemTrackingProperties TrackingProperties;
    }
}
