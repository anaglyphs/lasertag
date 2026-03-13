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
using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine.Rendering;

/// <summary>
/// Enables efficient rendering of UI elements and media as compositor layers that bypass Unity's rendering pipeline.
/// Overlays are rendered directly by the VR compositor at native display resolution for improved visual quality.
/// Supports multiple shapes (quad, cylinder, equirect, cubemap), stereo textures, and advanced filtering options.
/// Use for UI elements, HUD components, video playback, and content requiring maximum visual clarity.
/// Works with <see cref="OVRManager"/> for VR runtime integration and <see cref="OVRPlugin"/> for native functionality.
/// </summary>
[ExecuteInEditMode]
[HelpURL("https://developer.oculus.com/documentation/unity/unity-ovroverlay/")]
public class OVROverlay : MonoBehaviour
{
    #region Interface

    /// <summary>
    /// Geometric shape and projection method for overlay rendering. Platform support varies by shape.
    /// Defines rendering projections from flat quads to 360° environments. Used with <see cref="currentOverlayShape"/> property.
    /// Each shape interprets <see cref="Transform"/> components differently for positioning, rotation, and scaling.
    /// Supports standard surfaces (Quad, Cylinder), 360° content (Cubemap, Equirect), and mixed reality (Passthrough shapes).
    /// </summary>
    public enum OverlayShape
    {
        /// <summary>
        /// Flat rectangular surface for UI panels and video content. Most efficient option.
        /// Position: Uses Transform.position for world placement. Rotation: Uses Transform.rotation for orientation.
        /// Scale: Transform.lossyScale defines width (X), height (Y), and depth (Z, typically ignored).
        /// </summary>
        Quad = OVRPlugin.OverlayShape.Quad,

        /// <summary>
        /// Cylindrical surface wrapping around user. [Mobile Only]
        /// Position: Uses Transform.position for cylinder center. Rotation: Uses Transform.rotation for cylinder orientation.
        /// Scale: lossyScale.z defines radius, lossyScale.x defines the arc angle, lossyScale.y defines height.
        /// </summary>
        Cylinder = OVRPlugin.OverlayShape.Cylinder,

        /// <summary>
        /// 360° cube map using 6 faces. Requires <see cref="Cubemap"/> texture.
        /// Position: Always positioned at head camera location (Transform.position ignored). Rotation: Uses Transform.rotation with platform-specific adjustments.
        /// Scale: Transform.lossyScale typically ignored as cubemap fills entire view. Legacy rotation behavior controlled by useLegacyCubemapRotation.
        /// </summary>
        Cubemap = OVRPlugin.OverlayShape.Cubemap,

        /// <summary>
        /// Off-center cube map with custom positioning. [Mobile Only]
        /// Position: Transform.position defines offset from head center (magnitude must be ≤ 1.0 to avoid invisible pixels). Rotation: Uses Transform.rotation.
        /// Scale: Transform.lossyScale typically ignored as cubemap fills entire view. Used for asymmetric 360° environments or rooms with off-center viewing positions.
        /// </summary>
        OffcenterCubemap = OVRPlugin.OverlayShape.OffcenterCubemap,

        /// <summary>
        /// Equirectangular projection for 360° content at infinite distance. 2:1 aspect ratio optimal.
        /// Position: Rendered at infinite distance (Transform.position typically ignored). Rotation: Uses Transform.rotation for environment orientation.
        /// Scale: Transform.scale has no effect on the visuals of the layer. Ideal for skyboxes and distant 360° backgrounds.
        /// </summary>
        Equirect = OVRPlugin.OverlayShape.Equirect,

        /// <summary>
        /// Finite-distance equirectangular projection with depth positioning.
        /// Position: Uses Transform.position for distance from viewer (enables parallax and depth perception). Rotation: Uses Transform.rotation for orientation.
        /// Scale: Transform.lossyScale controls apparent size at the specified distance. Allows 360° content with spatial depth relationships.
        /// </summary>
        ScaledEquirect = OVRPlugin.OverlayShape.ScaledEquirect,

        /// <summary>
        /// Passthrough overlay displaying real-world environment with reconstruction. No texture required.
        /// </summary>
        ReconstructionPassthrough = OVRPlugin.OverlayShape.ReconstructionPassthrough,

        /// <summary>
        /// Surface-projected passthrough mapping virtual content onto real surfaces. No texture required.
        /// </summary>
        SurfaceProjectedPassthrough = OVRPlugin.OverlayShape.SurfaceProjectedPassthrough,

        /// <summary>
        /// Fisheye projection for wide field of view content. Not supported on OpenXR.
        /// Position: Uses Transform.position for placement. Rotation: Uses Transform.rotation for orientation.
        /// Scale: Transform.lossyScale controls fisheye effect intensity and apparent size.
        /// </summary>
        Fisheye = OVRPlugin.OverlayShape.Fisheye,

        /// <summary>
        /// Passthrough showing hands over keyboard for mixed reality typing. No texture required.
        /// </summary>
        KeyboardHandsPassthrough = OVRPlugin.OverlayShape.KeyboardHandsPassthrough,

        /// <summary>
        /// Masked passthrough showing hands over keyboard with occlusion handling. No texture required.
        /// </summary>
        KeyboardMaskedHandsPassthrough = OVRPlugin.OverlayShape.KeyboardMaskedHandsPassthrough,
    }

    /// <summary>
    /// Depth ordering and compositing behavior relative to main scene content.
    /// Ordered by <see cref="compositionDepth"/> within each type.
    /// Controls whether overlay renders behind (Underlay), in front (Overlay), or disabled (None) relative to scene content.
    /// </summary>
    public enum OverlayType
    {
        /// <summary>Disables overlay rendering completely.</summary>
        None,

        /// <summary>Renders behind main scene content. Ideal for background environments and skyboxes.</summary>
        Underlay,

        /// <summary>Renders in front of main scene content. Most common type for UI elements and HUD components.</summary>
        Overlay,
    };

    /// <summary>
    /// Controls overlay depth ordering relative to scene content. Combined with <see cref="compositionDepth"/> for final render order.
    /// Determines whether overlay renders behind, in front, or disabled relative to scene content.
    /// Underlay for backgrounds/skyboxes, Overlay for UI/HUD, None to disable rendering completely.
    /// </summary>
    public OverlayType currentOverlayType = OverlayType.Overlay;

    /// <summary>
    /// Controls whether texture content is updated every frame (dynamic) or once (static).
    /// Dynamic mode enables video and animated UI but consumes more GPU resources.
    /// </summary>
    public bool isDynamic = false;

    /// <summary>
    /// Enables protected content rendering to prevent the overlay from appearing in screenshots, recordings, or screen captures.
    /// When true, the layer uses HDCP (High-bandwidth Digital Content Protection) or similar mechanisms to protect copyrighted content.
    /// Use for DRM-protected media, copyrighted video content, or sensitive information. May have performance overhead and platform limitations.
    /// </summary>
    public bool isProtectedContent = false;

    /// <summary>
    /// Source rectangle for left eye in normalized coordinates (0,0) to (1,1).
    /// For side-by-side stereo: use (0,0,0.5,1). For over-under: use (0,0.5,1,1).
    /// </summary>
    public Rect srcRectLeft = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Source rectangle for right eye in normalized coordinates (0,0) to (1,1).
    /// For side-by-side stereo: use (0.5,0,0.5,1). For over-under: use (0,0,1,0.5).
    /// </summary>
    public Rect srcRectRight = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Destination rectangle for left eye in normalized coordinates (0-1).
    /// Requires <see cref="overrideTextureRectMatrix"/> enabled to take effect.
    /// </summary>
    public Rect destRectLeft = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Destination rectangle for right eye in normalized coordinates (0-1).
    /// Requires <see cref="overrideTextureRectMatrix"/> enabled to take effect.
    /// </summary>
    public Rect destRectRight = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Inverts texture coordinates vertically to support legacy content where the texture origin was at the top-left.
    /// Modern VR systems typically use bottom-left as the texture origin, so this property provides backward compatibility.
    /// Enable when working with legacy textures or content that appears upside-down in the overlay.
    /// </summary>
    public bool invertTextureRects = false;

    /// <summary>
    /// Internal texture rect matrix used for advanced texture coordinate transformations.
    /// Stores UV mapping parameters for source and destination rectangle adjustments.
    /// </summary>
    private OVRPlugin.TextureRectMatrixf textureRectMatrix = OVRPlugin.TextureRectMatrixf.zero;

    /// <summary>
    /// Enables the use of custom source and destination rectangles instead of default full-texture mapping.
    /// When true, the <see cref="srcRectLeft"/>, <see cref="srcRectRight"/>, <see cref="destRectLeft"/>, and <see cref="destRectRight"/>
    /// properties control texture sampling and positioning. Use for stereo layouts, texture atlasing, or custom viewport positioning.
    /// </summary>
    public bool overrideTextureRectMatrix = false;

    /// <summary>
    /// Enables per-layer color adjustment by applying custom color scaling and offset transformations.
    /// When true, allows fine-tuning the visual appearance of overlays without modifying source textures.
    /// Use <see cref="colorScale"/> and <see cref="colorOffset"/> to control the adjustments.
    /// Final color = (SourceColor * colorScale) + colorOffset. More efficient than CPU texture modifications.
    /// </summary>
    public bool overridePerLayerColorScaleAndOffset = false;

    /// <summary>
    /// RGBA color multiplier applied to the overlay when <see cref="overridePerLayerColorScaleAndOffset"/> is enabled.
    /// Each component multiplies the corresponding channel in the source texture before <see cref="colorOffset"/> is applied.
    /// Default value (1,1,1,1) applies no scaling. Use for brightness adjustment, color tinting, or transparency fading.
    /// </summary>
    public Vector4 colorScale = Vector4.one;

    /// <summary>
    /// RGBA color offset added to the overlay when <see cref="overridePerLayerColorScaleAndOffset"/> is enabled.
    /// Each component is added to the corresponding channel after <see cref="colorScale"/> multiplication.
    /// Default value (0,0,0,0) applies no offset. Use for color tinting, brightness boost, or fade to color.
    /// </summary>
    public Vector4 colorOffset = Vector4.zero;

    /// <summary>
    /// Enables expensive super sampling for maximum image quality.
    /// <para>
    /// <b>WARNING:</b> Performance-intensive feature that should only be used
    /// when you have sufficient GPU budget and require the highest possible visual quality.
    /// Not recommended for most applications.
    /// </para>
    /// <para>
    /// Consider useAutomaticFiltering instead, to achieve better quality filtering
    /// when performance headroom allows.
    /// </para>
    /// </summary>
    public bool useExpensiveSuperSample = false;

    /// <summary>
    /// Enables expensive sharpening filter for enhanced edge clarity.
    /// <para>
    /// <b>WARNING:</b> Performance-intensive feature that should only be used
    /// when you have sufficient GPU budget and require the highest possible visual quality.
    /// Not recommended for most applications.
    /// </para>
    /// <para>
    /// Consider useAutomaticFiltering instead, to achieve better quality filtering
    /// when performance headroom allows.
    /// </para>
    /// </summary>
    public bool useExpensiveSharpen = false;

    /// <summary>
    /// Controls overlay visibility. When true, the overlay is hidden from rendering.
    /// Use this property to dynamically show/hide overlays without disabling the component.
    /// This is useful when the overlay may be frequently hidden and shown, without
    /// the performance hit of full layer teardown and setup.
    /// </summary>
    public bool hidden = false;


    /// <summary>
    /// [Android Only] Enables external surface rendering for advanced video and media integration.
    /// When true, creates an Android Surface object that can be used with MediaPlayer, Camera2 API,
    /// or other native Android media frameworks. Use for video playback, camera feeds, or streaming content.
    /// External surfaces provide optimal video performance by bypassing Unity's texture management.
    /// </summary>
    public bool isExternalSurface = false;

    /// <summary>
    /// [Android Only] Specifies the width in pixels for the external surface when <see cref="isExternalSurface"/> is enabled.
    /// This dimension determines the resolution of the media content that can be rendered to the surface.
    /// Choose dimensions that match your media content resolution for optimal quality. Higher resolutions consume more GPU resources.
    /// Some image producers may override these dimensions to match source content size.
    /// </summary>
    public int externalSurfaceWidth = 0;

    /// <summary>
    /// [Android Only] Specifies the height in pixels for the external surface when <see cref="isExternalSurface"/> is enabled.
    /// This dimension determines the resolution of the media content that can be rendered to the surface.
    /// Ensure the width/height ratio matches your content's aspect ratio to prevent distortion. Use the lowest resolution that provides acceptable quality.
    /// Some image producers may override these dimensions to match source content size.
    /// </summary>
    public int externalSurfaceHeight = 0;

    /// <summary>
    /// Controls depth ordering within the same <see cref="OverlayType"/>. Lower values render first (behind).
    /// </summary>
    public int compositionDepth = 0;

    private int layerCompositionDepth = 0;

    /// <summary>
    /// Disables depth buffer-based compositing and forces overlay ordering based solely on <see cref="compositionDepth"/> and <see cref="currentOverlayType"/>.
    /// When true, prevents the overlay from being occluded by scene geometry even when "Shared Depth Buffer" is enabled in the VR runtime.
    /// Enable for UI elements that should always be visible (HUD, menus). Disable for 3D UI that should interact with scene geometry.
    /// </summary>
    public bool noDepthBufferTesting = true;

    /// <summary>
    /// Specifies the pixel format for the overlay layer's texture data.
    /// Controls color depth, precision, and gamma correction behavior. Use sRGB for standard UI/video, floating point for HDR content, linear for custom color management.
    /// The system automatically detects HDR formats from texture types. Higher precision formats consume more memory and bandwidth.
    /// </summary>
    public OVRPlugin.EyeTextureFormat layerTextureFormat = OVRPlugin.EyeTextureFormat.R8G8B8A8_sRGB;

    /// <summary>
    /// Defines the geometric projection and rendering method for the overlay layer.
    /// Each shape provides different ways to display content in 3D space, from flat panels to immersive 360-degree environments.
    /// Choose based on content type: Quad for UI/video, Cylinder for wrap-around content, Cubemap/Equirect for 360° content, Passthrough for mixed reality.
    /// Some shapes have platform limitations (Cylinder/OffcenterCubemap are mobile-only, Fisheye not supported on OpenXR).
    /// </summary>
    public OverlayShape currentOverlayShape = OverlayShape.Quad;

    private OverlayShape prevOverlayShape = OverlayShape.Quad;

    /// <summary>
    /// Defines the texture content displayed by the overlay layer for left and right eyes respectively.
    /// Array index 0 contains the left eye texture, index 1 contains the right eye texture.
    /// For mono content, only index 0 is used and the same texture is displayed to both eyes.
    /// Use Cubemap textures for Cubemap shapes, Texture2D/RenderTexture for others.
    /// For dynamic content, use OverrideOverlayTextureInfo() to avoid expensive native pointer lookups per frame.
    /// </summary>
    public Texture[] textures = new Texture[] { null, null };

    /// <summary>
    /// Specifies whether the texture's alpha channel has been pre-multiplied with the RGB color channels.
    /// This affects alpha blending behavior during overlay composition with scene content.
    /// In premultiplied alpha, RGB values are already multiplied by alpha (e.g., red pixel (1,0,0) with 50% alpha becomes (0.5,0,0,0.5)).
    /// Enable for modern rendering pipelines that use premultiplied alpha. Disable for standard "straight alpha" textures.
    /// </summary>
    public bool isAlphaPremultiplied = false;

    /// <summary>
    /// Enables bicubic texture filtering for higher quality image scaling at the cost of increased GPU processing.
    /// Provides smoother visual results compared to standard bilinear filtering when textures are scaled up or down.
    /// Use for high-resolution UI elements, text overlays, or detailed images when you have sufficient GPU budget.
    /// Consider useAutomaticFiltering to let the runtime decide based on performance characteristics.
    /// </summary>
    public bool useBicubicFiltering = false;

    /// <summary>
    /// Enables legacy cubemap rotation behavior for backward compatibility.
    /// <para>
    /// <b>DEPRECATED:</b> This setting will be removed in future versions.
    /// Fix your cubemap textures instead of relying on this legacy behavior.
    /// </para>
    /// </summary>
    public bool useLegacyCubemapRotation = false;

    /// <summary>
    /// Enables efficient super sampling for improved visual quality with moderate performance impact.
    /// This is a performance-optimized alternative to <see cref="useExpensiveSuperSample"/> that provides better quality
    /// with reasonable GPU cost. Super sampling renders content at higher resolution then downsamples for display, reducing aliasing.
    /// Use for text overlays, UI elements with fine details, or when visual quality is prioritized over performance.
    /// Cannot be used simultaneously with sharpening filters unless using useAutomaticFiltering.
    /// </summary>
    public bool useEfficientSupersample = false;

    /// <summary>
    /// Enables efficient sharpening filter to enhance edge clarity and text readability with moderate performance impact.
    /// This is a performance-optimized alternative to <see cref="useExpensiveSharpen"/> that provides good quality
    /// enhancement with reasonable GPU cost. Sharpening enhances edge contrast to make content appear crisper.
    /// Use for text-heavy UI overlays, soft/blurry images, or content with fine details. Cannot be used simultaneously with super sampling unless using useAutomaticFiltering.
    /// </summary>
    public bool useEfficientSharpen = false;

    /// <summary>
    /// Enables intelligent filtering where the runtime automatically selects optimal image enhancement
    /// based on performance headroom and content. Recommended for most applications.
    /// </summary>
    public bool useAutomaticFiltering = false;

    /// <summary>
    /// [Editor Only] Enables preview visualization of the overlay in the Unity Scene view using a mesh renderer.
    /// This helps with positioning and setup during development but has no effect at runtime.
    /// Creates a visual representation for positioning overlays, visualizing size/shape, and debugging placement.
    /// Only approximates VR appearance and may not represent all shapes accurately. No impact on runtime performance.
    /// </summary>
    public bool previewInEditor
    {
        get { return _previewInEditor; }
        set
        {
            if (_previewInEditor != value)
            {
                _previewInEditor = value;
                SetupEditorPreview();
            }
        }
    }

    [SerializeField]
    internal bool _previewInEditor = false;

#if UNITY_EDITOR
    private GameObject previewObject;
#endif

    protected IntPtr[] texturePtrs = new IntPtr[] { IntPtr.Zero, IntPtr.Zero };

    /// <summary>
    /// The Surface object (Android only).
    /// </summary>
    public System.IntPtr externalSurfaceObject;

    public delegate void ExternalSurfaceObjectCreated();

    /// <summary>
    /// Will be triggered after externalSurfaceTextueObject get created.
    /// </summary>
    public ExternalSurfaceObjectCreated externalSurfaceObjectCreated;

    /// <summary>
    /// Overrides the overlay texture information for dynamic texture updates at runtime.
    /// Use this method to efficiently update overlay textures without triggering expensive native texture pointer lookups each frame.
    /// GetNativeTexturePtr() is expensive - pre-cache the pointer and use this method instead of directly assigning to textures array.
    /// </summary>
    /// <param name="srcTexture">The source texture to display in the overlay</param>
    /// <param name="nativePtr">Pre-cached native texture pointer obtained from GetNativeTexturePtr()</param>
    /// <param name="node">XR node specifying which eye (LeftEye=0, RightEye=1) to update</param>
    public void OverrideOverlayTextureInfo(Texture srcTexture, IntPtr nativePtr, UnityEngine.XR.XRNode node)
    {
        int index = (node == UnityEngine.XR.XRNode.RightEye) ? 1 : 0;

        if (textures.Length <= index)
            return;

        textures[index] = srcTexture;
        texturePtrs[index] = nativePtr;

        isOverridePending = true;
    }

    /// <summary>
    /// Indicates whether a texture override operation is pending application.
    /// Set to true when <see cref="OverrideOverlayTextureInfo"/> is called, reset after processing.
    /// </summary>
    protected bool isOverridePending;

    /// <summary>
    /// Global registry of all active OVROverlay instances in the scene.
    /// Used by the overlay system to manage layer indices, track overlay lifecycle, and optimize resource allocation.
    /// Provides automatic layer index assignment, efficient reuse of destroyed overlay slots, and proper cleanup coordination.
    /// Not thread-safe - use only on main Unity thread. Total overlay count can impact VR compositor performance.
    /// </summary>
    public static readonly List<OVROverlay> instances = new();

    /// <summary>
    /// The unique identifier assigned by the VR compositor for this overlay layer.
    /// This handle is used internally to reference the layer in all compositor operations and submissions.
    /// Set to 0 initially, positive values indicate active layer, reset to 0 when destroyed.
    /// Used for texture submission, property updates, and cleanup operations. Managed on main Unity thread only.
    /// A layerId of 0 indicates initialization failure or destroyed state.
    /// </summary>
    public int layerId { get; private set; } = 0;

    #endregion

    /// <summary>
    /// Shared material used for blitting 2D textures to overlay swap chains.
    /// Contains the shader and settings for efficient texture copying and format conversion.
    /// </summary>
    protected static Material tex2DMaterial;

    /// <summary>
    /// Array of materials used for blitting individual cubemap faces to overlay swap chains.
    /// Each material extracts and processes a specific face of the cubemap.
    /// </summary>
    protected static readonly Material[] cubeMaterial = new Material[6];

    /// <summary>
    /// Determines texture layout for the overlay layer based on configured textures.
    /// Returns Stereo if separate left/right eye textures are provided on Android, otherwise Mono.
    /// </summary>
    protected OVRPlugin.LayerLayout layout
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (textures.Length == 2 && textures[1] != null && textures[1] != textures[0])
                return OVRPlugin.LayerLayout.Stereo;
#endif
            return OVRPlugin.LayerLayout.Mono;
        }
    }

    /// <summary>
    /// Texture information for a single eye/stage in the overlay rendering pipeline.
    /// Manages Unity textures and compositor swap chain textures for multi-buffered rendering.
    /// Contains both application textures from <see cref="textures"/> and compositor-managed swap chain for efficient GPU rendering.
    /// Used in <see cref="layerTextures"/> array with one entry per eye/stage for stereo or mono content.
    /// </summary>
    protected struct LayerTexture
    {
        /// <summary>
        /// Unity application texture provided through <see cref="textures"/> array. Contains the original texture source for overlay rendering.
        /// This is the source texture that gets copied to the compositor swap chain for VR display. Can be Texture2D, RenderTexture, or Cubemap.
        /// </summary>
        public Texture appTexture;

        /// <summary>Native pointer to application texture for direct compositor access. Cached pointer for efficient texture submission to VR runtime.</summary>
        public IntPtr appTexturePtr;

        /// <summary>Compositor-managed textures forming the swap chain for multi-buffered rendering. Array of textures provided by VR compositor for direct rendering.</summary>
        public Texture[] swapChain;

        /// <summary>Native pointers to each swap chain texture for efficient GPU memory access. Cached native handles for direct GPU texture operations.</summary>
        public IntPtr[] swapChainPtr;
    };

    /// <summary>
    /// Array of texture structures for each eye/stage. Single entry for mono, two entries for stereo rendering.
    /// </summary>
    protected LayerTexture[] layerTextures;

    /// <summary>
    /// Layer descriptor containing the complete specification for the overlay layer as submitted to the compositor.
    /// Defines all rendering parameters including format, size, layout, and feature flags.
    /// </summary>
    protected OVRPlugin.LayerDesc layerDesc;

    /// <summary>
    /// Number of texture stages in the compositor swap chain for this overlay layer.
    /// Typically 3 for triple buffering to prevent blocking during texture updates.
    /// </summary>
    protected int stageCount = -1;

    /// <summary>
    /// Index of this overlay instance within the global instances registry.
    /// Used for layer composition ordering and efficient overlay management.
    /// </summary>
    public int layerIndex { get; protected set; } = -1;

    /// <summary>
    /// GC handle maintaining a pinned reference to the layerId for safe compositor access.
    /// Prevents garbage collection from moving the layerId value while compositor holds a pointer to it.
    /// </summary>
    protected GCHandle layerIdHandle;

    /// <summary>
    /// Native pointer to the pinned layerId value for efficient compositor communication.
    /// Allows VR runtime to directly access and modify the layer ID without managed/native transitions.
    /// </summary>
    protected IntPtr layerIdPtr = IntPtr.Zero;

    /// <summary>
    /// Current frame index used for swap chain stage selection and texture update timing.
    /// Increments each frame for dynamic overlays to cycle through available texture stages.
    /// </summary>
    protected int frameIndex = 0;

    /// <summary>
    /// Previous frame index used to detect when texture updates are needed and prevent duplicate processing.
    /// Helps optimize rendering by avoiding redundant texture population within the same frame.
    /// </summary>
    protected int prevFrameIndex = -1;

    /// <summary>
    /// Reference to the Renderer component attached to this GameObject, if any.
    /// Used for backward compatibility and automatic renderer visibility management.
    /// </summary>
    protected Renderer rend;

    private static readonly int _tempRenderTextureId = Shader.PropertyToID("_OVROverlayTempTexture");
    private CommandBuffer _commandBuffer;
    private Mesh _blitMesh;


    /// <summary>
    /// Indicates whether the overlay layer is currently visible and successfully submitted to the compositor.
    /// Updated each frame during rendering to reflect the actual visibility state.
    /// </summary>
    /// <remarks>
    /// This property is set to true when the overlay is successfully submitted to the VR compositor
    /// and not hidden via the <see cref="hidden"/> flag. Used to determine whether to disable
    /// the associated <see cref="Renderer"/> component for performance optimization.
    /// </remarks>
    /// <seealso cref="TrySubmitLayer"/>
    /// <seealso cref="hidden"/>
    /// <seealso cref="rend"/>
    public bool isOverlayVisible { get; private set; }

    /// <summary>
    /// Returns the number of textures needed per stage based on the current layout configuration.
    /// Returns 2 for stereo layout (separate left/right eye textures), 1 for mono layout (shared texture).
    /// </summary>
    /// <remarks>
    /// Used to determine array sizes for texture management and processing loops.
    /// Stereo layout is only supported on Android platforms with separate eye textures.
    /// </remarks>
    /// <seealso cref="layout"/>
    /// <seealso cref="layerTextures"/>
    protected int texturesPerStage
    {
        get { return (layout == OVRPlugin.LayerLayout.Stereo) ? 2 : 1; }
    }

    /// <summary>
    /// Determines if the specified overlay shape requires texture content.
    /// Passthrough shapes don't need textures as they display real-world content.
    /// </summary>
    /// <param name="shape">The overlay shape to check</param>
    /// <returns>True if the shape requires texture content, false for passthrough shapes</returns>
    protected static bool NeedsTexturesForShape(OverlayShape shape)
    {
        return !IsPassthroughShape(shape);
    }

    /// <summary>
    /// Creates a new overlay layer in the VR compositor with the specified parameters.
    /// Handles layer descriptor creation, compositor setup, and instance registry management.
    /// </summary>
    /// <param name="mipLevels">Number of mip levels for the layer texture</param>
    /// <param name="sampleCount">Sample count for multisampling</param>
    /// <param name="etFormat">Texture format for the layer</param>
    /// <param name="flags">Layer feature flags</param>
    /// <param name="size">Layer texture dimensions</param>
    /// <param name="shape">Geometric shape of the overlay</param>
    /// <returns>True if layer creation succeeded, false otherwise</returns>
    protected bool CreateLayer(int mipLevels, int sampleCount, OVRPlugin.EyeTextureFormat etFormat, int flags,
        OVRPlugin.Sizei size, OVRPlugin.OverlayShape shape)
    {
        if (!layerIdHandle.IsAllocated || layerIdPtr == IntPtr.Zero)
        {
            layerIdHandle = GCHandle.Alloc(layerId, GCHandleType.Pinned);
            layerIdPtr = layerIdHandle.AddrOfPinnedObject();
        }

        if (layerIndex == -1)
        {
            layerIndex = instances.IndexOf(this);
            if (layerIndex == -1)
            {
                // Try to reuse the empty spot from destroyed Overlay
                layerIndex = instances.IndexOf(null);
                if (layerIndex == -1)
                {
                    layerIndex = instances.Count;
                    instances.Add(this);
                }
                else
                {
                    instances[layerIndex] = this;
                }
            }
        }

        bool needsSetup = (
                              isOverridePending ||
                              layerDesc.MipLevels != mipLevels ||
                              layerDesc.SampleCount != sampleCount ||
                              layerDesc.Format != etFormat ||
                              layerDesc.Layout != layout ||
                              layerDesc.LayerFlags != flags ||
                              !layerDesc.TextureSize.Equals(size) ||
                              layerDesc.Shape != shape ||
                              layerCompositionDepth != compositionDepth);

        if (!needsSetup)
            return false;

        OVRPlugin.LayerDesc desc =
            OVRPlugin.CalculateLayerDesc(shape, layout, size, mipLevels, sampleCount, etFormat, flags);


        var setupSuccess = OVRPlugin.EnqueueSetupLayer(desc, compositionDepth, layerIdPtr);
        Debug.Assert(setupSuccess, "OVRPlugin.EnqueueSetupLayer failed");

        layerId = (int)layerIdHandle.Target;
        if (layerId > 0)
        {
            layerDesc = desc;
            layerCompositionDepth = compositionDepth;
            if (isExternalSurface)
            {
                stageCount = 1;
            }
            else
            {
                stageCount = OVRPlugin.GetLayerTextureStageCount(layerId);
            }
        }

        isOverridePending = false;

        return true;
    }

    /// <summary>
    /// Creates compositor-managed texture swap chains for this overlay layer.
    /// </summary>
    /// <param name="useMipmaps">Whether to create textures with mipmap support</param>
    /// <param name="size">Dimensions of the textures to create in pixels</param>
    /// <param name="isHdr">Whether to use HDR texture format (16-bit floating point)</param>
    /// <returns>True if new textures were created or texture copying is needed for this frame</returns>
    protected bool CreateLayerTextures(bool useMipmaps, OVRPlugin.Sizei size, bool isHdr)
    {
        if (isExternalSurface)
        {
            if (externalSurfaceObject == System.IntPtr.Zero)
            {
                externalSurfaceObject = OVRPlugin.GetLayerAndroidSurfaceObject(layerId);
                if (externalSurfaceObject != System.IntPtr.Zero)
                {
                    Debug.LogFormat("GetLayerAndroidSurfaceObject returns {0}", externalSurfaceObject);
                    if (externalSurfaceObjectCreated != null)
                    {
                        externalSurfaceObjectCreated();
                    }
                }
            }

            return false;
        }

        bool needsCopy = false;

        if (stageCount <= 0)
            return false;

        // For newer SDKs, blit directly to the surface that will be used in compositing.

        if (layerTextures == null)
            layerTextures = new LayerTexture[texturesPerStage];

        for (int eyeId = 0; eyeId < texturesPerStage; ++eyeId)
        {
            if (layerTextures[eyeId].swapChain == null)
                layerTextures[eyeId].swapChain = new Texture[stageCount];

            if (layerTextures[eyeId].swapChainPtr == null)
                layerTextures[eyeId].swapChainPtr = new IntPtr[stageCount];

            for (int stage = 0; stage < stageCount; ++stage)
            {
                Texture sc = layerTextures[eyeId].swapChain[stage];
                IntPtr scPtr = layerTextures[eyeId].swapChainPtr[stage];

                if (sc != null && scPtr != IntPtr.Zero && size.w == sc.width && size.h == sc.height)
                    continue;

                if (scPtr == IntPtr.Zero)
                    scPtr = OVRPlugin.GetLayerTexture(layerId, stage, (OVRPlugin.Eye)eyeId);

                if (scPtr == IntPtr.Zero)
                    continue;

                var txFormat = (isHdr) ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;

                if (currentOverlayShape != OverlayShape.Cubemap && currentOverlayShape != OverlayShape.OffcenterCubemap)
                    sc = Texture2D.CreateExternalTexture(size.w, size.h, txFormat, useMipmaps, false, scPtr);
                else
                    sc = Cubemap.CreateExternalTexture(size.w, txFormat, useMipmaps, scPtr);

                layerTextures[eyeId].swapChain[stage] = sc;
                layerTextures[eyeId].swapChainPtr[stage] = scPtr;

                needsCopy = true;
            }
        }

        return needsCopy;
    }

    /// <summary>
    /// Destroys all layer textures and cleans up swap chain resources.
    /// Called during layer teardown to free compositor texture memory.
    /// </summary>
    protected void DestroyLayerTextures()
    {
        if (isExternalSurface)
        {
            return;
        }

        for (int eyeId = 0; layerTextures != null && eyeId < texturesPerStage; ++eyeId)
        {
            if (layerTextures[eyeId].swapChain != null)
            {
                for (int stage = 0; stage < stageCount; ++stage)
                    Destroy(layerTextures[eyeId].swapChain[stage]);
            }
        }

        layerTextures = null;
    }

    /// <summary>
    /// Destroys the overlay layer and releases all associated compositor resources.
    /// Cleans up the layer ID, destroys textures, and removes the overlay from the global instances registry.
    /// Performs comprehensive cleanup: hides overlay, removes from registry, destroys layer, releases handles, resets state.
    /// Can be called multiple times safely. Automatically invoked by OnDisable, OnDestroy, and TrySubmitLayer when needed.
    /// </summary>
    protected void DestroyLayer()
    {
        if (layerIndex != -1)
        {
            // Turn off the overlay if it was on.
            OVRPlugin.EnqueueSubmitLayer(true, false, false, IntPtr.Zero, IntPtr.Zero, -1, 0,
                OVRPose.identity.ToPosef_Legacy(), Vector3.one.ToVector3f(), layerIndex,
                (OVRPlugin.OverlayShape)prevOverlayShape);
            instances[layerIndex] = null;
            layerIndex = -1;
        }

        if (layerIdPtr != IntPtr.Zero)
        {
            OVRPlugin.EnqueueDestroyLayer(layerIdPtr);
            layerIdPtr = IntPtr.Zero;
            layerIdHandle.Free();
            layerId = 0;
        }

        layerDesc = new OVRPlugin.LayerDesc();

        frameIndex = 0;
        prevFrameIndex = -1;
    }

    /// <summary>
    /// Configures texture mapping rectangles for stereo rendering by setting source and destination rectangles for both left and right eyes simultaneously.
    /// Source rectangles define which portion of the input texture is sampled (normalized 0-1 coordinates).
    /// Destination rectangles define where the content appears in the final overlay rendering.
    /// </summary>
    /// <param name="srcLeft">Source rectangle for left eye texture sampling (normalized 0-1)</param>
    /// <param name="srcRight">Source rectangle for right eye texture sampling (normalized 0-1)</param>
    /// <param name="destLeft">Destination rectangle for left eye rendering (normalized 0-1)</param>
    /// <param name="destRight">Destination rectangle for right eye rendering (normalized 0-1)</param>
    public void SetSrcDestRects(Rect srcLeft, Rect srcRight, Rect destLeft, Rect destRight)
    {
        srcRectLeft = srcLeft;
        srcRectRight = srcRight;
        destRectLeft = destLeft;
        destRectRight = destRight;
    }

    /// <summary>
    /// Updates the internal texture rectangle matrix for advanced UV coordinate transformations.
    /// Handles coordinate conversions for external surfaces, texture inversion, and fisheye projections.
    /// Converts source/destination rectangles into GPU-ready transformation matrices. External surfaces use inverted Y coordinates,
    /// fisheye applies -0.5 offset for centering. Populates textureRectMatrix with scale/bias values for compositor UV transformation.
    /// </summary>
    public void UpdateTextureRectMatrix()
    {
        // External surfaces are encoded with reversed UV's, so our texture rects are also inverted
        Rect srcRectLeftConverted = new Rect(srcRectLeft.x,
            isExternalSurface ^ invertTextureRects ? 1 - srcRectLeft.y - srcRectLeft.height : srcRectLeft.y,
            srcRectLeft.width, srcRectLeft.height);
        Rect srcRectRightConverted = new Rect(srcRectRight.x,
            isExternalSurface ^ invertTextureRects ? 1 - srcRectRight.y - srcRectRight.height : srcRectRight.y,
            srcRectRight.width, srcRectRight.height);
        Rect destRectLeftConverted = new Rect(destRectLeft.x,
            isExternalSurface ^ invertTextureRects ? 1 - destRectLeft.y - destRectLeft.height : destRectLeft.y,
            destRectLeft.width, destRectLeft.height);
        Rect destRectRightConverted = new Rect(destRectRight.x,
            isExternalSurface ^ invertTextureRects ? 1 - destRectRight.y - destRectRight.height : destRectRight.y,
            destRectRight.width, destRectRight.height);
        textureRectMatrix.leftRect = srcRectLeftConverted;
        textureRectMatrix.rightRect = srcRectRightConverted;

        // Fisheye layer requires a 0.5f offset for texture to be centered on the fisheye projection
        if (currentOverlayShape == OverlayShape.Fisheye)
        {
            destRectLeftConverted.x -= 0.5f;
            destRectLeftConverted.y -= 0.5f;
            destRectRightConverted.x -= 0.5f;
            destRectRightConverted.y -= 0.5f;
        }

        float leftWidthFactor = srcRectLeft.width / destRectLeft.width;
        float leftHeightFactor = srcRectLeft.height / destRectLeft.height;
        textureRectMatrix.leftScaleBias = new Vector4(leftWidthFactor, leftHeightFactor,
            srcRectLeftConverted.x - destRectLeftConverted.x * leftWidthFactor,
            srcRectLeftConverted.y - destRectLeftConverted.y * leftHeightFactor);
        float rightWidthFactor = srcRectRight.width / destRectRight.width;
        float rightHeightFactor = srcRectRight.height / destRectRight.height;
        textureRectMatrix.rightScaleBias = new Vector4(rightWidthFactor, rightHeightFactor,
            srcRectRightConverted.x - destRectRightConverted.x * rightWidthFactor,
            srcRectRightConverted.y - destRectRightConverted.y * rightHeightFactor);
    }

    /// <summary>
    /// Sets custom color scaling and offset values for the overlay layer and enables color adjustment.
    /// This method provides a convenient way to apply color transformations without manually setting individual properties.
    /// </summary>
    /// <param name="scale">RGBA color multiplier values. Default (1,1,1,1) applies no scaling.</param>
    /// <param name="offset">RGBA color offset values added after scaling. Default (0,0,0,0) applies no offset.</param>
    /// <remarks>
    /// Applies the provided scale and offset values. The final color is calculated as: (SourceColor * scale) + offset.
    /// </remarks>
    /// <seealso cref="overridePerLayerColorScaleAndOffset"/>
    /// <seealso cref="colorScale"/>
    /// <seealso cref="colorOffset"/>
    public void SetPerLayerColorScaleAndOffset(Vector4 scale, Vector4 offset)
    {
        colorScale = scale;
        colorOffset = offset;
    }

    /// <summary>
    /// Validates and caches native texture pointers for overlay rendering.
    /// Ensures application textures are properly prepared and accessible to the VR compositor.
    /// </summary>
    /// <returns>True if all required textures are valid and ready for rendering</returns>
    protected bool LatchLayerTextures()
    {
        if (isExternalSurface)
        {
            return true;
        }

        for (int i = 0; i < texturesPerStage; ++i)
        {
            if (textures[i] != layerTextures[i].appTexture || layerTextures[i].appTexturePtr == IntPtr.Zero)
            {
                if (textures[i] != null)
                {
#if UNITY_EDITOR
                    var assetPath = UnityEditor.AssetDatabase.GetAssetPath(textures[i]);
                    var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
                    if (importer != null && importer.textureType != UnityEditor.TextureImporterType.Default)
                    {
                        Debug.LogError("Need Default Texture Type for overlay");
                        return false;
                    }
#endif
                    var rt = textures[i] as RenderTexture;
                    if (rt && !rt.IsCreated())
                        rt.Create();

                    layerTextures[i].appTexturePtr = (texturePtrs[i] != IntPtr.Zero)
                        ? texturePtrs[i]
                        : textures[i].GetNativeTexturePtr();

                    if (layerTextures[i].appTexturePtr != IntPtr.Zero)
                        layerTextures[i].appTexture = textures[i];
                }
            }

            if (currentOverlayShape == OverlayShape.Cubemap)
            {
                if (textures[i] as Cubemap == null)
                {
                    Debug.LogError("Need Cubemap texture for cube map overlay");
                    return false;
                }
            }
        }

#if !UNITY_ANDROID || UNITY_EDITOR
        if (currentOverlayShape == OverlayShape.OffcenterCubemap)
        {
            Debug.LogWarning("Overlay shape " + currentOverlayShape + " is not supported on current platform");
            return false;
        }
#endif

        if (layerTextures[0].appTexture == null || layerTextures[0].appTexturePtr == IntPtr.Zero)
            return false;

        return true;
    }

    /// <summary>
    /// Creates a layer descriptor structure with current overlay configuration for compositor submission.
    /// Analyzes texture properties and overlay settings to generate the appropriate layer specification.
    /// Automatically detects HDR formats from texture types, sets feature flags based on enabled properties,
    /// and calculates appropriate dimensions from external surfaces or texture dimensions.
    /// </summary>
    /// <returns>Complete layer descriptor containing format, size, flags, and shape information for the compositor</returns>
    protected OVRPlugin.LayerDesc GetCurrentLayerDesc()
    {
        OVRPlugin.Sizei textureSize = new OVRPlugin.Sizei() { w = 0, h = 0 };

        if (isExternalSurface)
        {
            textureSize.w = externalSurfaceWidth;
            textureSize.h = externalSurfaceHeight;
        }
        else if (NeedsTexturesForShape(currentOverlayShape))
        {
            if (textures[0] == null)
            {
                Debug.LogWarning("textures[0] hasn't been set");
            }

            textureSize.w = textures[0] ? textures[0].width : 0;
            textureSize.h = textures[0] ? textures[0].height : 0;
        }

        OVRPlugin.LayerDesc newDesc = new OVRPlugin.LayerDesc()
        {
            Format = layerTextureFormat,
            LayerFlags = isExternalSurface ? 0 : (int)OVRPlugin.LayerFlags.TextureOriginAtBottomLeft,
            Layout = layout,
            MipLevels = 1,
            SampleCount = 1,
            Shape = (OVRPlugin.OverlayShape)currentOverlayShape,
            TextureSize = textureSize
        };

        var tex2D = textures[0] as Texture2D;
        if (tex2D != null)
        {
            if (tex2D.format == TextureFormat.RGBAHalf || tex2D.format == TextureFormat.RGBAFloat)
                newDesc.Format = OVRPlugin.EyeTextureFormat.R16G16B16A16_FP;

            newDesc.MipLevels = tex2D.mipmapCount;
        }

        var texCube = textures[0] as Cubemap;
        if (texCube != null)
        {
            if (texCube.format == TextureFormat.RGBAHalf || texCube.format == TextureFormat.RGBAFloat)
                newDesc.Format = OVRPlugin.EyeTextureFormat.R16G16B16A16_FP;

            newDesc.MipLevels = texCube.mipmapCount;
        }

        var rt = textures[0] as RenderTexture;
        if (rt != null)
        {
            newDesc.SampleCount = rt.antiAliasing;

            if (rt.format == RenderTextureFormat.ARGBHalf || rt.format == RenderTextureFormat.ARGBFloat ||
                rt.format == RenderTextureFormat.RGB111110Float)
                newDesc.Format = OVRPlugin.EyeTextureFormat.R16G16B16A16_FP;

            newDesc.MipLevels = rt.mipmapCount;
        }

        if (isProtectedContent)
        {
            newDesc.LayerFlags |= (int)OVRPlugin.LayerFlags.ProtectedContent;
        }

        if (isExternalSurface)
        {
            newDesc.LayerFlags |= (int)OVRPlugin.LayerFlags.AndroidSurfaceSwapChain;
        }

        if (useBicubicFiltering)
        {
            newDesc.LayerFlags |= (int)OVRPlugin.LayerFlags.BicubicFiltering;
        }

        return newDesc;
    }

    /// <summary>
    /// Calculates the pixel-perfect blit rectangle for texture copying operations.
    /// Converts normalized source rectangles to exact pixel coordinates with appropriate padding.
    /// For stereo textures, uses appropriate eye rectangle. For shared textures, calculates union of both eyes.
    /// Adds 2-pixel border to handle texture filtering edge cases.
    /// </summary>
    /// <param name="eyeId">Eye index (0=left, 1=right) for stereo or shared texture rectangle calculation</param>
    /// <param name="width">Target texture width in pixels</param>
    /// <param name="height">Target texture height in pixels</param>
    /// <param name="invertRect">Whether to invert Y coordinates for different texture coordinate systems</param>
    /// <returns>Pixel-accurate rectangle with 2-pixel padding for safe blitting operations</returns>
    protected Rect GetBlitRect(int eyeId, int width, int height, bool invertRect)
    {
        Rect rect;
        if (texturesPerStage == 2)
        {
            rect = eyeId == 0 ? srcRectLeft : srcRectRight;
        }
        else
        {
            // Get intersection of both rects if we use the same texture for both eyes
            float minX = Mathf.Min(srcRectLeft.x, srcRectRight.x);
            float minY = Mathf.Min(srcRectLeft.y, srcRectRight.y);
            float maxX = Mathf.Max(srcRectLeft.x + srcRectLeft.width, srcRectRight.x + srcRectRight.width);
            float maxY = Mathf.Max(srcRectLeft.y + srcRectLeft.height, srcRectRight.y + srcRectRight.height);
            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        if (invertRect)
        {
            // our rects are inverted
            rect.y = 1 - rect.y - rect.height;
        }

        // Round our rect to the bounding pixel rect, and add two pixel padding
        float xMin = Mathf.Max(0, Mathf.Round(width * rect.x) - 2);
        float yMin = Mathf.Max(0, Mathf.Round(height * rect.y) - 2);
        float xMax = Mathf.Min(width, Mathf.Round(width * rect.xMax) + 2);
        float yMax = Mathf.Min(height, Mathf.Round(height * rect.yMax) + 2);
        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    /// <summary>
    /// Performs optimized texture blitting to a specific region using command buffer operations.
    /// Renders the source texture to a sub-region with custom projection and viewport settings.
    /// Uses scissor rectangle and viewport offsetting to blit only the necessary region, reducing GPU bandwidth.
    /// Integrates with Unity's command buffer system for efficient GPU command submission.
    /// </summary>
    /// <param name="src">Source texture to blit from</param>
    /// <param name="width">Full target texture width in pixels</param>
    /// <param name="height">Full target texture height in pixels</param>
    /// <param name="mat">Material/shader to use for the blit operation</param>
    /// <param name="rect">Target rectangle in pixel coordinates within the destination texture</param>
    protected void BlitSubImage(Texture src, int width, int height, Material mat, Rect rect)
    {
        // do our blit using our command buffer
        _commandBuffer.SetRenderTarget(_tempRenderTextureId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        _commandBuffer.SetProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1));
        _commandBuffer.SetViewMatrix(Matrix4x4.identity);
        _commandBuffer.EnableScissorRect(new Rect(0, 0, rect.width, rect.height));
        _commandBuffer.SetViewport(new Rect(-rect.x, -rect.y, width, height));
        mat.mainTexture = src;
        mat.SetPass(0);

        if (_blitMesh == null)
        {
            _blitMesh = new Mesh() { name = "OVROverlay Blit Mesh" };
            _blitMesh.SetVertices(new Vector3[] { new Vector3(-1, -1, 0), new Vector3(-1, 3, 0), new Vector3(3, -1, 0) });
            _blitMesh.SetUVs(0, new Vector2[] { new Vector2(0, 0), new Vector2(0, 2), new Vector2(2, 0) });
            _blitMesh.SetIndices(new ushort[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            _blitMesh.UploadMeshData(true);
        }
        _commandBuffer.DrawMesh(_blitMesh, Matrix4x4.identity, mat);
    }

    /// <summary>
    /// Copies application textures to compositor swap chain textures using optimized blitting operations.
    /// Handles format conversion, alpha premultiplication, and texture rectangle processing.
    /// </summary>
    /// <param name="mipLevels">Number of mip levels to populate</param>
    /// <param name="isHdr">Whether to use HDR rendering format</param>
    /// <param name="size">Texture dimensions</param>
    /// <param name="sampleCount">MSAA sample count</param>
    /// <param name="stage">Swap chain stage index to populate</param>
    /// <returns>True if texture population succeeded</returns>
    protected bool PopulateLayer(int mipLevels, bool isHdr, OVRPlugin.Sizei size, int sampleCount, int stage)
    {
        if (isExternalSurface)
        {
            return true;
        }

        bool ret = false;

        RenderTextureFormat rtFormat = (isHdr) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;

        if (_commandBuffer == null)
        {
            _commandBuffer = new CommandBuffer();
        }
        _commandBuffer.Clear();
        _commandBuffer.name = ToString();

        for (int eyeId = 0; eyeId < texturesPerStage; ++eyeId)
        {
            Texture et = layerTextures[eyeId].swapChain[stage];
            if (et == null)
                continue;

            ret = true;

            // If this platform requries premultiplied Alpha, premultiply it unless its already premultiplied
            bool premultiplyAlpha = !isAlphaPremultiplied && !OVRPlugin.unpremultipliedAlphaLayersSupported;

            // If this platform requires unpremultiplied alpha, and the buffer is already premultiplied, divide it out if possible.
            bool unmultiplyAlpha = isAlphaPremultiplied && !OVRPlugin.premultipliedAlphaLayersSupported;

            // OpenGL does not support copy texture between different format
#pragma warning disable CS0618 // Type or member is obsolete
            bool isOpenGL = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 ||
                            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2;
#pragma warning restore CS0618 // Type or member is obsolete
            // Graphics.CopyTexture only works when textures are same size and same mipmap count
            bool isSameSize = et.width == textures[eyeId].width && et.height == textures[eyeId].height;
            bool sameMipMap = textures[eyeId].mipmapCount == et.mipmapCount;


            bool isCubemap = currentOverlayShape == OverlayShape.Cubemap ||
                             currentOverlayShape == OverlayShape.OffcenterCubemap;

            bool bypassBlit = Application.isMobilePlatform && !isOpenGL && isSameSize && sameMipMap && !unmultiplyAlpha;
            if (bypassBlit)
            {
                _commandBuffer.CopyTexture(textures[eyeId], et);
                continue;
            }

            // Need to run the blit shader for premultiply Alpha
            for (int mip = 0; mip < mipLevels; ++mip)
            {
                int width = size.w >> mip;
                if (width < 1) width = 1;
                int height = size.h >> mip;
                if (height < 1) height = 1;

                int rtWidth = width;
                int rtHeight = height;
                if (overrideTextureRectMatrix && isDynamic)
                {
                    Rect blitRect = GetBlitRect(eyeId, width, height, invertTextureRects);
                    rtWidth = (int)blitRect.width;
                    rtHeight = (int)blitRect.height;
                }

                RenderTextureDescriptor descriptor = new RenderTextureDescriptor(rtWidth, rtHeight, rtFormat, 0);
                descriptor.msaaSamples = sampleCount;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                descriptor.sRGB = true;

                _commandBuffer.GetTemporaryRT(_tempRenderTextureId, descriptor, FilterMode.Point);

                int faceCount = isCubemap ? 6 : 1;
                for (int face = 0; face < faceCount; face++)
                {
                    Material blitMat = isCubemap ? cubeMaterial[face] : tex2DMaterial;

                    blitMat.SetInt("_premultiply", premultiplyAlpha ? 1 : 0);
                    blitMat.SetInt("_unmultiply", unmultiplyAlpha ? 1 : 0);

                    if (!isCubemap)
                        blitMat.SetInt("_flip", OVRPlugin.nativeXrApi == OVRPlugin.XrApi.OpenXR ? 1 : 0);

                    if (!isCubemap && overrideTextureRectMatrix && isDynamic)
                    {
                        Rect blitRect = GetBlitRect(eyeId, width, height, invertTextureRects);
                        BlitSubImage(textures[eyeId], width, height, blitMat, blitRect);
                        _commandBuffer.CopyTexture(
                            _tempRenderTextureId,
                            0,
                            0,
                            0,
                            0,
                            (int)blitRect.width,
                            (int)blitRect.height,
                            et,
                            face,
                            mip,
                            (int)blitRect.x,
                            (int)blitRect.y);
                    }
                    else
                    {
                        _commandBuffer.Blit(textures[eyeId], _tempRenderTextureId, blitMat);
                        _commandBuffer.CopyTexture(_tempRenderTextureId, 0, 0, et, face, mip);
                    }
                }

                _commandBuffer.ReleaseTemporaryRT(_tempRenderTextureId);
            }
        }

        if (ret)
        {
            Graphics.ExecuteCommandBuffer(_commandBuffer);
        }

        return ret;
    }

    /// <summary>
    /// Submits the overlay layer to the VR compositor with configured rendering parameters.
    /// Handles filtering validation, texture matrix updates, and alpha premultiplication settings.
    /// </summary>
    /// <param name="overlay">Whether to render as overlay (true) or underlay (false)</param>
    /// <param name="headLocked">Whether the overlay is locked to head movement</param>
    /// <param name="noDepthBufferTesting">Whether to disable depth buffer testing</param>
    /// <param name="pose">World-space pose of the overlay</param>
    /// <param name="scale">Scale transformation for the overlay</param>
    /// <param name="frameIndex">Frame index for swap chain synchronization</param>
    /// <returns>True if overlay submission succeeded and is visible</returns>
    protected bool SubmitLayer(bool overlay, bool headLocked, bool noDepthBufferTesting, OVRPose pose, Vector3 scale,
        int frameIndex)
    {
        int rightEyeIndex = (texturesPerStage >= 2) ? 1 : 0;
        if (overrideTextureRectMatrix)
        {
            UpdateTextureRectMatrix();
        }

        bool internalUseEfficientSharpen = useEfficientSharpen;
        bool internalUseEfficientSupersample = useEfficientSupersample;
        bool internalIsAlphaPremultiplied = isAlphaPremultiplied && OVRPlugin.premultipliedAlphaLayersSupported;

        // No sharpening or supersampling method was selected, defaulting to efficient supersampling and efficient sharpening.
        if (useAutomaticFiltering && !(useEfficientSharpen || useEfficientSupersample || useExpensiveSharpen || useExpensiveSuperSample))
        {
            internalUseEfficientSharpen = true;
            internalUseEfficientSupersample = true;
        }

        if (!useAutomaticFiltering && ((useEfficientSharpen && useEfficientSupersample)
           || (useExpensiveSharpen && useExpensiveSuperSample)
           || (useEfficientSharpen && useExpensiveSuperSample)
           || (useExpensiveSharpen && useEfficientSupersample)))
        {

            Debug.LogError("Warning-XR sharpening and supersampling cannot be enabled simultaneously, either enable autofiltering or disable one of the options");
            return false;
        }

        bool noTextures = isExternalSurface || !NeedsTexturesForShape(currentOverlayShape);
        bool isOverlayVisible = OVRPlugin.EnqueueSubmitLayer(overlay, headLocked, noDepthBufferTesting,
            noTextures ? System.IntPtr.Zero : layerTextures[0].appTexturePtr,
            noTextures ? System.IntPtr.Zero : layerTextures[rightEyeIndex].appTexturePtr, layerId, frameIndex,
            pose.flipZ().ToPosef_Legacy(), scale.ToVector3f(), layerIndex, (OVRPlugin.OverlayShape)currentOverlayShape,
            overrideTextureRectMatrix, textureRectMatrix, overridePerLayerColorScaleAndOffset, colorScale, colorOffset,
            useExpensiveSuperSample, useBicubicFiltering, internalUseEfficientSupersample,
            internalUseEfficientSharpen, useExpensiveSharpen, hidden, isProtectedContent, useAutomaticFiltering,
            internalIsAlphaPremultiplied
        );
        prevOverlayShape = currentOverlayShape;

        return isOverlayVisible;
    }

    /// <summary>
    /// Creates or destroys the editor preview object based on previewInEditor setting.
    /// Manages Unity editor visualization for overlay positioning and debugging.
    /// </summary>
    protected void SetupEditorPreview()
    {
#if UNITY_EDITOR
        if (previewInEditor)
        {
            if (previewObject == null)
            {
                previewObject = new GameObject();
                previewObject.hideFlags = HideFlags.HideAndDontSave;
                previewObject.transform.SetParent(this.transform, false);
                OVROverlayMeshGenerator generator = previewObject.AddComponent<OVROverlayMeshGenerator>();
                generator.SetOverlay(this);
            }
            previewObject.SetActive(true);
        }
        else if (previewObject != null)
        {
            previewObject.SetActive(false);
            DestroyImmediate(previewObject);
            previewObject = null;
        }
#endif
    }

    /// <summary>
    /// Resets the editor preview by toggling the previewInEditor setting.
    /// Forces recreation of the preview visualization object with current overlay settings.
    /// </summary>
    public void ResetEditorPreview()
    {
        previewInEditor = false;
        previewInEditor = true;
    }

    /// <summary>
    /// Determines whether the specified overlay shape is a passthrough type that displays real-world content.
    /// Passthrough shapes don't require texture content as they render camera or environment data directly.
    /// </summary>
    /// <param name="shape">The overlay shape to check</param>
    /// <returns>True if the shape displays real-world content, false if it requires application textures</returns>
    /// <remarks>
    /// Passthrough shapes include: ReconstructionPassthrough, SurfaceProjectedPassthrough,
    /// KeyboardHandsPassthrough, and KeyboardMaskedHandsPassthrough. These shapes are used
    /// for mixed reality applications where real-world content is integrated with virtual elements.
    /// </remarks>
    public static bool IsPassthroughShape(OverlayShape shape)
    {
        return OVRPlugin.IsPassthroughShape((OVRPlugin.OverlayShape)shape);
    }

    #region Unity Messages

    /// <summary>
    /// Initializes shared materials for texture blitting and sets up OpenVR integration.
    /// </summary>
    void Awake()
    {
        if (Application.isPlaying)
        {
            if (tex2DMaterial == null)
                tex2DMaterial = new Material(Shader.Find("Oculus/Texture2D Blit"));

            Shader cubeShader = null;
            for (int face = 0; face < 6; face++)
            {
                if (cubeMaterial[face] == null)
                {
                    if (cubeShader == null)
                        cubeShader = Shader.Find("Oculus/Cubemap Blit");
                    cubeMaterial[face] = new Material(cubeShader);
                }
                cubeMaterial[face].SetInt("_face", face);
            }
        }

        rend = GetComponent<Renderer>();

        if (textures.Length == 0)
            textures = new Texture[] { null };

        // Backward compatibility
        if (rend != null && textures[0] == null)
            textures[0] = rend.sharedMaterial.mainTexture;

        SetupEditorPreview();
    }

    static public string OpenVROverlayKey
    {
        get { return "unity:" + Application.companyName + "." + Application.productName; }
    }

    private ulong OpenVROverlayHandle = OVR.OpenVR.OpenVR.k_ulOverlayHandleInvalid;

    /// <summary>
    /// Initializes the overlay, sets up editor preview, and registers camera rendering callbacks.
    /// </summary>
    void OnEnable()
    {
        if (OVRManager.OVRManagerinitialized)
            InitOVROverlay();

        // The command above (`InitOVROverlay()`) may sometimes cancel the enabling  process for the current component
        // If this happens, we need to stop here
        if (!enabled)
            return;

        SetupEditorPreview();

        Camera.onPreRender += HandlePreRender;
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    /// <summary>
    /// Initializes the VR overlay system based on the loaded XR device.
    /// Handles setup for both Oculus and OpenVR platforms with proper error checking.
    /// </summary>
    void InitOVROverlay()
    {
#if USING_XR_SDK_OPENXR
        if (!OVRPlugin.UnityOpenXR.Enabled)
        {
#endif
        if (!OVRManager.isHmdPresent)
        {
            enabled = false;
            return;
        }
#if USING_XR_SDK_OPENXR
        }
#endif

        constructedOverlayXRDevice = OVRManager.XRDevice.Unknown;
        if (OVRManager.loadedXRDevice == OVRManager.XRDevice.OpenVR)
        {
            OVR.OpenVR.CVROverlay overlay = OVR.OpenVR.OpenVR.Overlay;
            if (overlay != null)
            {
                OVR.OpenVR.EVROverlayError error = overlay.CreateOverlay(OpenVROverlayKey + transform.name,
                    gameObject.name, ref OpenVROverlayHandle);
                if (error != OVR.OpenVR.EVROverlayError.None)
                {
                    enabled = false;
                    return;
                }
            }
            else
            {
                enabled = false;
                return;
            }
        }

        constructedOverlayXRDevice = OVRManager.loadedXRDevice;
        xrDeviceConstructed = true;
    }

    /// <summary>
    /// Cleans up preview objects, unregisters callbacks, and destroys the overlay layer.
    /// </summary>
    void OnDisable()
    {
        if (gameObject.scene.name == "DontDestroyOnLoad")
        {
            // Because scene loads can trigger OnDisable/OnEnable
            // for objects that shouldn't destroy on load,
            // we will wait for the next render update
            // to disable those objects
            return;
        }

        DisableImmediately();
    }

    /// <summary>
    /// Immediately disables the overlay component and cleans up all associated resources.
    /// Called internally by OnDisable or when the component needs to be shut down immediately.
    /// </summary>
    void DisableImmediately()
    {

#if UNITY_EDITOR
        if (previewObject != null)
        {
            previewObject.SetActive(false);
        }
#endif

        Camera.onPreRender -= HandlePreRender;
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;

        if (!OVRManager.OVRManagerinitialized)
            return;

        if (OVRManager.loadedXRDevice != constructedOverlayXRDevice)
            return;

        if (OVRManager.loadedXRDevice == OVRManager.XRDevice.Oculus)
        {
            DestroyLayerTextures();
            DestroyLayer();
        }
        else if (OVRManager.loadedXRDevice == OVRManager.XRDevice.OpenVR)
        {
            if (OpenVROverlayHandle != OVR.OpenVR.OpenVR.k_ulOverlayHandleInvalid)
            {
                OVR.OpenVR.CVROverlay overlay = OVR.OpenVR.OpenVR.Overlay;
                if (overlay != null)
                {
                    overlay.DestroyOverlay(OpenVROverlayHandle);
                }

                OpenVROverlayHandle = OVR.OpenVR.OpenVR.k_ulOverlayHandleInvalid;
            }
        }

        isOverlayVisible = false;
        constructedOverlayXRDevice = OVRManager.XRDevice.Unknown;
        xrDeviceConstructed = false;
    }

    /// <summary>
    /// Ensures proper cleanup of layer resources and removes from global registry.
    /// </summary>
    void OnDestroy()
    {
        DisableImmediately();
        DestroyLayerTextures();
        DestroyLayer();

#if UNITY_EDITOR
        if (previewObject != null)
        {
            GameObject.DestroyImmediate(previewObject);
        }
#endif

        if (_commandBuffer != null)
        {
            _commandBuffer.Dispose();
        }

        if (_blitMesh != null)
        {
            DestroyImmediate(_blitMesh);
        }

    }

    /// <summary>
    /// Calculates the world-space pose and scale for overlay rendering.
    /// Handles head-locked overlays and cubemap positioning with platform-specific rotation adjustments.
    /// </summary>
    void ComputePoseAndScale(out OVRPose pose, out Vector3 scale, out bool overlay, out bool headLocked)
    {
        Camera headCamera = OVRManager.FindMainCamera();
        overlay = (currentOverlayType == OverlayType.Overlay);
        headLocked = false;
        for (var t = transform; t != null && !headLocked; t = t.parent)
            headLocked |= (t == headCamera.transform);

        pose = (headLocked) ? transform.ToHeadSpacePose(headCamera) : transform.ToTrackingSpacePose(headCamera);
        scale = transform.lossyScale;
        for (int i = 0; i < 3; ++i)
            scale[i] /= headCamera.transform.lossyScale[i];

        if (currentOverlayShape == OverlayShape.Cubemap)
        {
            if (useLegacyCubemapRotation)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                pose.orientation = pose.orientation * Quaternion.AngleAxis(180, Vector3.up);
#endif
            }
            else
            {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR
                pose.orientation = pose.orientation * Quaternion.AngleAxis(180, Vector3.up);
#endif
            }

            pose.position = headCamera.transform.position;
        }
    }

    /// <summary>
    /// Validates overlay parameters and computes final pose/scale for compositor submission.
    /// Performs platform-specific validation and geometry sanity checks before rendering.
    /// </summary>
    bool ComputeSubmit(out OVRPose pose, out Vector3 scale, out bool overlay, out bool headLocked)
    {
        ComputePoseAndScale(out pose, out scale, out overlay, out headLocked);

        // Pack the offsetCenter directly into pose.position for offcenterCubemap
        if (currentOverlayShape == OverlayShape.OffcenterCubemap)
        {
            pose.position = transform.position;
            if (pose.position.magnitude > 1.0f)
            {
                Debug.LogWarning("Your cube map center offset's magnitude is greater than 1, " +
                                 "which will cause some cube map pixel always invisible .");
                return false;
            }
        }

        // Cylinder overlay sanity checking when not using OpenXR
        if (OVRPlugin.nativeXrApi != OVRPlugin.XrApi.OpenXR && currentOverlayShape == OverlayShape.Cylinder)
        {
            float arcAngle = scale.x / scale.z / (float)Math.PI * 180.0f;
            if (arcAngle > 180.0f)
            {
                Debug.LogWarning("Cylinder overlay's arc angle has to be below 180 degree, current arc angle is " +
                                 arcAngle + " degree.");
                return false;
            }
        }

        if (OVRPlugin.nativeXrApi == OVRPlugin.XrApi.OpenXR && currentOverlayShape == OverlayShape.Fisheye)
        {
            Debug.LogWarning("Fisheye overlay shape is not support on OpenXR");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Updates OpenVR overlay display with texture, transforms and texture bounds.
    /// Handles OpenVR-specific overlay configuration and rendering.
    /// </summary>
    bool OpenVROverlayUpdate(Vector3 scale, OVRPose pose)
    {
        OVR.OpenVR.CVROverlay overlayRef = OVR.OpenVR.OpenVR.Overlay;
        if (overlayRef == null)
            return false;

        Texture overlayTex = textures[0];

        if (overlayTex == null)
            return false;

        OVR.OpenVR.EVROverlayError error = overlayRef.ShowOverlay(OpenVROverlayHandle);
        if (error == OVR.OpenVR.EVROverlayError.InvalidHandle || error == OVR.OpenVR.EVROverlayError.UnknownOverlay)
        {
            if (overlayRef.FindOverlay(OpenVROverlayKey + transform.name, ref OpenVROverlayHandle) !=
                OVR.OpenVR.EVROverlayError.None)
                return false;
        }

        OVR.OpenVR.Texture_t tex = new OVR.OpenVR.Texture_t();
        tex.handle = overlayTex.GetNativeTexturePtr();
        tex.eType = SystemInfo.graphicsDeviceVersion.StartsWith("OpenGL")
            ? OVR.OpenVR.ETextureType.OpenGL
            : OVR.OpenVR.ETextureType.DirectX;
        tex.eColorSpace = OVR.OpenVR.EColorSpace.Auto;
        overlayRef.SetOverlayTexture(OpenVROverlayHandle, ref tex);

        OVR.OpenVR.VRTextureBounds_t textureBounds = new OVR.OpenVR.VRTextureBounds_t();
        textureBounds.uMin = (0 + OpenVRUVOffsetAndScale.x) * OpenVRUVOffsetAndScale.z;
        textureBounds.vMin = (1 + OpenVRUVOffsetAndScale.y) * OpenVRUVOffsetAndScale.w;
        textureBounds.uMax = (1 + OpenVRUVOffsetAndScale.x) * OpenVRUVOffsetAndScale.z;
        textureBounds.vMax = (0 + OpenVRUVOffsetAndScale.y) * OpenVRUVOffsetAndScale.w;

        overlayRef.SetOverlayTextureBounds(OpenVROverlayHandle, ref textureBounds);

        OVR.OpenVR.HmdVector2_t vecMouseScale = new OVR.OpenVR.HmdVector2_t();
        vecMouseScale.v0 = OpenVRMouseScale.x;
        vecMouseScale.v1 = OpenVRMouseScale.y;
        overlayRef.SetOverlayMouseScale(OpenVROverlayHandle, ref vecMouseScale);

        overlayRef.SetOverlayWidthInMeters(OpenVROverlayHandle, scale.x);

        Matrix4x4 mat44 = Matrix4x4.TRS(pose.position, pose.orientation, Vector3.one);

        OVR.OpenVR.HmdMatrix34_t pose34 = mat44.ConvertToHMDMatrix34();

        overlayRef.SetOverlayTransformAbsolute(OpenVROverlayHandle,
            OVR.OpenVR.ETrackingUniverseOrigin.TrackingUniverseStanding, ref pose34);

        return true;
    }

    private Vector4 OpenVRUVOffsetAndScale = new Vector4(0, 0, 1.0f, 1.0f);
    private Vector2 OpenVRMouseScale = new Vector2(1, 1);
    private OVRManager.XRDevice constructedOverlayXRDevice;
    private bool xrDeviceConstructed = false;

    /// <summary>
    /// Handles overlay submission before camera rendering.
    /// Called by Unity's camera pre-render callback for legacy rendering pipeline.
    /// </summary>
    void HandlePreRender(Camera camera)
    {
        if (camera == OVRManager.FindMainCamera())
        {
            isOverlayVisible = TrySubmitLayer() && !hidden;
        }
    }

    /// <summary>
    /// Handles overlay submission for Scriptable Render Pipeline cameras.
    /// Called by Unity's SRP rendering callback to submit overlays before camera rendering.
    /// </summary>
    void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == OVRManager.FindMainCamera())
        {
            isOverlayVisible = TrySubmitLayer() && !hidden;
        }
    }

    /// <summary>
    /// Attempts to submit the overlay layer to the VR compositor with validation and error handling.
    /// Main rendering pipeline method that coordinates texture creation, validation, and compositor submission.
    /// </summary>
    /// <returns>True if overlay was successfully submitted and is visible to the user</returns>
    /// <remarks>
    /// <para>
    /// <b>Submission Pipeline:</b> Validates overlay state, creates/updates textures, submits to compositor.
    /// Handles frame timing, swap chain management, and backward compatibility with legacy renderers.
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Returns false for invalid configurations, missing textures, or compositor errors.
    /// Automatically cleans up resources when overlay becomes invalid or disabled.
    /// </para>
    /// </remarks>
    bool TrySubmitLayer()
    {
        if (!this || !enabled)
        {
            DisableImmediately();
            return false;
        }

        if (!OVRManager.OVRManagerinitialized || !OVRPlugin.userPresent)
            return false;

        if (!xrDeviceConstructed)
        {
            InitOVROverlay();
        }

        if (OVRManager.loadedXRDevice != constructedOverlayXRDevice)
        {
            Debug.LogError("Warning-XR Device was switched during runtime with overlays still enabled. " +
                           "When doing so, all overlays constructed with the previous XR device must first be disabled.");
            return false;
        }

        // The overlay must be specified every eye frame, because it is positioned relative to the
        // current head location.  If frames are dropped, it will be time warped appropriately,
        // just like the eye buffers.
        bool requiresTextures = !isExternalSurface && NeedsTexturesForShape(currentOverlayShape);
        if (currentOverlayType == OverlayType.None ||
            (requiresTextures && (textures.Length < texturesPerStage || textures[0] == null)))
        {
            return false;
        }

        if (!ComputeSubmit(out OVRPose pose, out Vector3 scale, out bool overlay, out bool headLocked))
            return false;

        if (OVRManager.loadedXRDevice == OVRManager.XRDevice.OpenVR)
        {
            if (currentOverlayShape == OverlayShape.Quad)
                return OpenVROverlayUpdate(scale, pose);

            //No more Overlay processing is required if we're on OpenVR
            return false;
        }

        OVRPlugin.LayerDesc newDesc = GetCurrentLayerDesc();
        bool isHdr = (newDesc.Format == OVRPlugin.EyeTextureFormat.R16G16B16A16_FP);

        // If the layer and textures are created but sizes differ, force re-creating them.
        // If the layer needed textures but does not anymore (or vice versa), re-create as well.
        bool textureSizesDiffer = !layerDesc.TextureSize.Equals(newDesc.TextureSize) && layerId > 0;
        bool needsTextures = NeedsTexturesForShape(currentOverlayShape);
        bool needsTextureChanged = NeedsTexturesForShape(prevOverlayShape) != needsTextures;
        if (textureSizesDiffer || needsTextureChanged)
        {
            DestroyLayerTextures();
            DestroyLayer();
        }

        bool createdLayer = CreateLayer(newDesc.MipLevels, newDesc.SampleCount, newDesc.Format, newDesc.LayerFlags,
            newDesc.TextureSize, newDesc.Shape);

        if (layerIndex == -1 || layerId <= 0)
        {
            if (createdLayer)
            {
                // Propagate the current shape and avoid the permanent state of "needs texture changed"
                prevOverlayShape = currentOverlayShape;
            }

            return false;
        }

        int submitSwapchainIndex = frameIndex;
        if (needsTextures)
        {
            bool useMipmaps = (newDesc.MipLevels > 1);

            createdLayer |= CreateLayerTextures(useMipmaps, newDesc.TextureSize, isHdr);

            if (!isExternalSurface && (layerTextures[0].appTexture as RenderTexture != null))
                isDynamic = true;

            if (!LatchLayerTextures())
                return false;

            // Don't populate the same frame image twice.
            if (frameIndex > prevFrameIndex)
            {
                int stage = frameIndex % stageCount;
                if (OVRPlugin.IsSuccess(OVRPlugin.AcquireLayerSwapchain(layerId, out int acquiredIndex)))
                {
                    stage = acquiredIndex;
                    submitSwapchainIndex = acquiredIndex;
                }
                if (!PopulateLayer(newDesc.MipLevels, isHdr, newDesc.TextureSize, newDesc.SampleCount, stage))
                    return false;
            }
        }

        bool isOverlayVisible = SubmitLayer(overlay, headLocked, noDepthBufferTesting, pose, scale, submitSwapchainIndex);

        prevFrameIndex = frameIndex;
        if (isDynamic)
            ++frameIndex;

        // Backward compatibility: show regular renderer if overlay isn't visible.
        if (rend)
            rend.enabled = !isOverlayVisible;

        return isOverlayVisible;
    }
    #endregion
}
