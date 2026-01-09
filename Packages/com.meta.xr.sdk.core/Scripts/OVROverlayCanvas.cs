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

#if USING_XR_MANAGEMENT && (USING_XR_SDK_OCULUS || USING_XR_SDK_OPENXR)
#define USING_XR_SDK
#endif

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.XR;

[RequireComponent(typeof(RectTransform))]
[ExecuteAlways]
public class OVROverlayCanvas : OVRRayTransformer
{
    public enum DrawMode
    {
        Opaque = 0,
        OpaqueWithClip = 1,
        Transparent = 2,

#if UNITY_2020_1_OR_NEWER
        [Obsolete("Deprecated. Use Transparent", false)]
#endif
        TransparentDefaultAlpha = 2,
#if UNITY_2020_1_OR_NEWER
        [Obsolete("Deprecated. Use Transparent", false)]
#endif
        TransparentCorrectAlpha = 3,
        AlphaToMask = 4,
    }

    public enum CanvasShape
    {
        Flat,
        Curved
    }

    // The optimal resolution for the display is approximately 2x the initial eye texture resolution
    private const float kOptimalResolutionScale = 2.0f;
    private int CanvasRenderLayer => OVROverlayCanvasSettings.Instance.CanvasRenderLayer;

    private Camera _camera;
    private OVROverlay _overlay;
    private MeshRenderer _meshRenderer;
    private OVROverlayMeshGenerator _meshGenerator;

    private RenderTexture _renderTexture;

    private Material _imposterMaterial;

    private bool _optimalResolutionInitialized;
    private float _optimalResolutionWidth;
    private float _optimalResolutionHeight;

    private int _lastPixelWidth;
    private int _lastPixelHeight;

    private Vector2 _imposterTextureOffset;
    private Vector2 _imposterTextureScale;

    private bool _frameIsReady;
    private bool _useTempRT;

    [SerializeField] internal bool _enableMipmapping = false;
    [SerializeField] internal bool _dynamicResolution = true;
    [SerializeField] internal int _redrawResolutionThreshold = int.MaxValue;
    private bool ShouldScaleViewport => _dynamicResolution;

    public RectTransform rectTransform;
    [FormerlySerializedAs("MaxTextureSize")] public int maxTextureSize = 2048;
    public bool manualRedraw = false;
    [FormerlySerializedAs("DrawRate")] public int renderInterval = 1;
    [FormerlySerializedAs("DrawFrameOffset")] public int renderIntervalFrameOffset = 0;
    [FormerlySerializedAs("Expensive")] public bool expensive = false;
    [FormerlySerializedAs("Layer")] public int layer = 5;
    [FormerlySerializedAs("Opacity")] public DrawMode opacity = DrawMode.Transparent;
    public CanvasShape shape = CanvasShape.Flat;
    public float curveRadius = 1.0f;
    public bool overlapMask = false;
    public OVROverlay.OverlayType overlayType = OVROverlay.OverlayType.Underlay;

    [SerializeField]
    internal bool _overlayEnabled = true;

    private static readonly Plane[] _FrustumPlanes = new Plane[6];
    private static readonly Vector3[] _Corners = new Vector3[4];

    private bool _nonUniformScaleWarningShown;
    private (int frameCount, float? score) _lastViewPriorityScore = (-1, null);

    public bool IsCanvasPriority => OVROverlayCanvasManager.Instance?.IsCanvasPriority(this) is true;
    public bool ShouldShowImposter => !IsCanvasPriority || !overlayEnabled || overlayType is OVROverlay.OverlayType.Underlay;

    public bool overlayEnabled
    {
        get { return _overlayEnabled; }
        set
        {
            if (_overlay && Application.isPlaying)
            {
                _overlay.enabled = value;
                // Update our impostor color to switch between visible and punch-a-hole
                _imposterMaterial.color = value ? Color.black : Color.white;
            }
            _overlayEnabled = value;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        Debug.Assert(
            rectTransform.gameObject == gameObject,
            $"{nameof(rectTransform)} must be the same GameObject as the {nameof(OVROverlayCanvas)}");

        HideFlags hideFlags = HideFlags.DontSave | HideFlags.NotEditable | HideFlags.HideInHierarchy;

        GameObject overlayCamera = new GameObject(name + " Overlay Camera") { hideFlags = hideFlags };
        overlayCamera.transform.SetParent(transform, false);

        _camera = overlayCamera.AddComponent<Camera>();
        _camera.stereoTargetEye = StereoTargetEyeMask.None;
        _camera.transform.position = transform.position - _camera.transform.forward;
        _camera.orthographic = true;
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.clear;
        _camera.nearClipPlane = 0.99f;
        _camera.farClipPlane = 1.01f;

        GameObject imposter = new GameObject(name + " Imposter") { hideFlags = hideFlags };

        imposter.transform.SetParent(transform, false);
        imposter.AddComponent<MeshFilter>();
        _meshRenderer = imposter.AddComponent<MeshRenderer>();
        _meshGenerator = imposter.AddComponent<OVROverlayMeshGenerator>();

        GameObject overlay = new GameObject(name + " Overlay") { hideFlags = hideFlags };
        overlay.transform.SetParent(transform, false);
        _overlay = overlay.AddComponent<OVROverlay>();
        _overlay.enabled = false;
        _overlay.isDynamic = true;
        UpdateOverlaySettings();

        // On mobile we need to use a temporary copy texture for best performance
        // on versions without the viewport flag
        _useTempRT = Application.isMobilePlatform;

        InitializeRenderTexture();

#if UNITY_EDITOR
        if (Application.IsPlaying(this))
        {
            OVRPlugin.SendEvent("canvas_initialized", ToSimpleJson(new
            {
                manualRedraw,
                renderInterval,
                expensive,
                opacity,
                shape,
                overlapMask,
                overlayType,
                maxTextureSize,
                enableMipmapping = _enableMipmapping,
                dynamicResolution = _dynamicResolution,
                redrawResolutionThreshold = _redrawResolutionThreshold,
            }));
        }
#endif
    }

    private static string ToSimpleJson<T>(T value)
    {
        var type = value?.GetType();
        if (type?.IsValueType ?? true)
        {
            return value switch
            {
                bool b => b ? "true" : "false",
                Enum or string => $"\"{value}\"",
                _ => value?.ToString(),
            };
        }

        var props = value.GetType().GetProperties();
        if (props.Length == 0)
        {
            return "{}";
        }

        var members = props.Select(p => $"\"{p.Name}\":{ToSimpleJson(p.GetValue(value))}");
        return $"{{{string.Join(",", members)}}}";
    }

    public void UpdateOverlaySettings()
    {
        InitializeRenderTexture();
        _meshRenderer.enabled = ShouldShowImposter;
        _overlay.noDepthBufferTesting = ShouldShowImposter;
        _overlay.isAlphaPremultiplied = true;
        _overlay.currentOverlayType = overlayType;
        _overlay.enabled = overlayEnabled;
    }

    private void InitializeRenderTexture()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        float rectWidth = rectTransform.rect.width;
        float rectHeight = rectTransform.rect.height;

        float aspectX = rectWidth >= rectHeight ? 1 : rectWidth / rectHeight;
        float aspectY = rectHeight >= rectWidth ? 1 : rectHeight / rectWidth;

        // if we are scaling the viewport we don't need to add a border
        int pixelBorder = ShouldScaleViewport ? 0 : 8;
        int innerWidth = Mathf.CeilToInt(aspectX * (maxTextureSize - pixelBorder * 2));
        int innerHeight = Mathf.CeilToInt(aspectY * (maxTextureSize - pixelBorder * 2));
        int width = innerWidth + pixelBorder * 2;
        int height = innerHeight + pixelBorder * 2;

        float paddedWidth = rectWidth * (width / (float)innerWidth);
        float paddedHeight = rectHeight * (height / (float)innerHeight);

        if (_renderTexture == null || _renderTexture.width != width || _renderTexture.height != height)
        {
            if (_renderTexture != null)
            {
                DestroyImmediate(_renderTexture);
            }

            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height,
                GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt);
            // if we can't scale the viewport, generate mipmaps instead
            descriptor.autoGenerateMips = descriptor.useMipMap = _enableMipmapping;
            _renderTexture = new RenderTexture(descriptor);
            _renderTexture.filterMode = FilterMode.Trilinear;
            _renderTexture.name = name;
        }

        _camera.orthographicSize = 0.5f * paddedHeight * GetRectTransformScale().y;
        _camera.targetTexture = _renderTexture;
        _camera.cullingMask = 1 << CanvasRenderLayer;

        Shader shader = OVROverlayCanvasSettings.Instance.GetShader(opacity);

        if (_imposterMaterial == null)
        {
            _imposterMaterial = new Material(shader);
        }
        else
        {
            _imposterMaterial.shader = shader;
        }

        if (opacity == DrawMode.OpaqueWithClip)
        {
            _imposterMaterial.EnableKeyword("WITH_CLIP");
        }
        else
        {
            _imposterMaterial.DisableKeyword("WITH_CLIP");
        }

#if !UNITY_2020_1_OR_NEWER
        if (opacity == DrawMode.TransparentDefaultAlpha)
        {
            _imposterMaterial.EnableKeyword("ALPHA_SQUARED");
        }
        else
        {
            _imposterMaterial.DisableKeyword("ALPHA_SQUARED");
        }
#endif

        if (expensive)
        {
            _imposterMaterial.EnableKeyword("EXPENSIVE");
        }
        else
        {
            _imposterMaterial.DisableKeyword("EXPENSIVE");
        }

        if (opacity == DrawMode.AlphaToMask)
        {
            _imposterMaterial.EnableKeyword("ALPHA_TO_MASK");
            _imposterMaterial.SetInt("_AlphaToMask", 1);
        }
        else
        {
            _imposterMaterial.DisableKeyword("ALPHA_TO_MASK");
            _imposterMaterial.SetInt("_AlphaToMask", 0);
        }

        if (overlayEnabled && overlapMask)
        {
            _imposterMaterial.EnableKeyword("OVERLAP_MASK");
        }
        else
        {
            _imposterMaterial.DisableKeyword("OVERLAP_MASK");
        }

        _imposterMaterial.mainTexture = _renderTexture;
        _imposterMaterial.color = CalcImposterColor();
        _imposterMaterial.mainTextureOffset = _imposterTextureOffset;
        _imposterMaterial.mainTextureScale = _imposterTextureScale;

        _meshRenderer.sharedMaterial = _imposterMaterial;
        _meshRenderer.gameObject.layer = layer;

        if (shape == CanvasShape.Flat)
        {
            _meshRenderer.transform.localPosition = _overlay.transform.localPosition = Vector3.zero;
            _meshRenderer.transform.localScale = new Vector3(rectWidth, rectHeight, 1);
            _overlay.transform.localScale = new Vector3(paddedWidth, paddedHeight, 1);
        }
        else
        {
            _meshRenderer.transform.localPosition = _overlay.transform.localPosition = new Vector3(0, 0, -curveRadius / transform.lossyScale.z);
            _meshRenderer.transform.localScale = new Vector3(rectWidth, rectHeight, curveRadius / transform.lossyScale.z);
            _overlay.transform.localScale = new Vector3(paddedWidth, paddedHeight, curveRadius / transform.lossyScale.z);
        }

        _overlay.textures[0] = _renderTexture;
        _overlay.currentOverlayShape = shape == CanvasShape.Flat
            ? OVROverlay.OverlayShape.Quad
            : OVROverlay.OverlayShape.Cylinder;
        _overlay.hidden = !IsCanvasPriority;

        _overlay.useExpensiveSuperSample = expensive;
        _overlay.enabled = Application.isPlaying && _overlayEnabled;
        // always turn on autofiltering
        _overlay.useAutomaticFiltering = true;

        _meshGenerator.SetOverlay(_overlay);

        OVROverlayCanvasSettings.Instance.ApplyGlobalSettings();
    }

    private Color CalcImposterColor()
    {
        return overlayEnabled && IsCanvasPriority && overlayType is OVROverlay.OverlayType.Underlay ? Color.black : Color.white;
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            Destroy(_imposterMaterial);
            Destroy(_renderTexture);
        }
        else
        {
            DestroyImmediate(_imposterMaterial);
            DestroyImmediate(_renderTexture);
        }
    }

    private void OnEnable()
    {
        OVROverlayCanvasManager.AddCanvas(this);

        if (_overlay)
        {
            _meshRenderer.enabled = ShouldShowImposter;
            _overlay.enabled = Application.isPlaying && _overlayEnabled;
        }
    }

    private void OnDisable()
    {
        OVROverlayCanvasManager.RemoveCanvas(this);

        if (_overlay)
        {
            _overlay.enabled = false;
            _meshRenderer.enabled = false;
        }
    }

    protected virtual bool ShouldRender()
    {
        if (manualRedraw && _frameIsReady)
        {
            // Check if the resolution has changed enough to trigger a redraw
            if (_dynamicResolution && _redrawResolutionThreshold != int.MaxValue && CalculateScaledResolution() is var (width, height))
            {
                return width - _lastPixelWidth >= _redrawResolutionThreshold ||
                    height - _lastPixelHeight >= _redrawResolutionThreshold;
            }
            else
            {
                return false;
            }
        }

        if (renderInterval > 1)
        {
            if (Time.frameCount % renderInterval != renderIntervalFrameOffset % renderInterval && _frameIsReady)
            {
                return false;
            }
        }

        // Always render in the editor
        if (Application.isEditor)
        {
            return true;
        }

        return IsInFrustum();
    }

    private bool IsInFrustum()
    {
        var mainCamera = OVRManager.FindMainCamera();
        if (mainCamera != null)
        {
            // Perform Frustum culling
#if USING_XR_SDK
            XRDisplaySubsystem currentDisplaySubsystem = OVRManager.GetCurrentDisplaySubsystem();
            if (currentDisplaySubsystem != null && currentDisplaySubsystem.GetRenderPassCount() > 0)
            {
                for (int i = 0; i < currentDisplaySubsystem.GetRenderPassCount(); i++)
                {
                    currentDisplaySubsystem.GetRenderPass(i, out var renderPass);
                    currentDisplaySubsystem.GetCullingParameters(mainCamera, renderPass.cullingPassIndex, out var cullingParameters);

                    var mat = cullingParameters.stereoProjectionMatrix * cullingParameters.stereoViewMatrix;
                    GeometryUtility.CalculateFrustumPlanes(mat, _FrustumPlanes);
                    if (GeometryUtility.TestPlanesAABB(_FrustumPlanes, _meshRenderer.bounds))
                    {
                        return true;
                    }
                }
            }
            else
#endif
            if (mainCamera.stereoEnabled)
            {
                for (int i = 0; i < 2; i++)
                {
                    var eye = (Camera.StereoscopicEye)i;
                    var mat = mainCamera.GetStereoProjectionMatrix(eye) * mainCamera.GetStereoViewMatrix(eye);
                    GeometryUtility.CalculateFrustumPlanes(mat, _FrustumPlanes);
                    if (GeometryUtility.TestPlanesAABB(_FrustumPlanes, _meshRenderer.bounds))
                    {
                        return true;
                    }
                }
            }
            else
            {
                var mat = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                GeometryUtility.CalculateFrustumPlanes(mat, _FrustumPlanes);
                if (GeometryUtility.TestPlanesAABB(_FrustumPlanes, _meshRenderer.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private void Update()
    {
        UpdateOverlaySettings();

        var shouldRender = ShouldRender();
        _overlay.isDynamic = shouldRender;
        if (!shouldRender)
            return;

        ApplyViewportScale();
        _frameIsReady = true;

        RenderCamera();
    }

    private void LateUpdate()
    {
        // Update our impostor color to switch between visible and punch-a-hole
        _imposterMaterial.color = CalcImposterColor();
        // Update the scale and offset each frame to avoid a bug where Unity likes to reset them for some reason
        _imposterMaterial.mainTextureScale = _imposterTextureScale;
        _imposterMaterial.mainTextureOffset = _imposterTextureOffset;
    }

    public float? GetViewPriorityScore()
    {
        var frameCount = Time.renderedFrameCount;
        if (_lastViewPriorityScore.frameCount != frameCount)
        {
            _lastViewPriorityScore = (frameCount, score: GetViewPriorityScoreImpl());
        }
        return _lastViewPriorityScore.score;
    }

    private float? GetViewPriorityScoreImpl()
    {
        var mainCamera = OVRManager.FindMainCamera();
        if (mainCamera == null)
            return null;

        if (!_overlayEnabled)
            return null;

        rectTransform.GetWorldCorners(_Corners);
        for (var i = 0; i != 4; ++i)
        {
            var anchor = mainCamera.WorldToViewportPoint(_Corners[i]);
            anchor.x = Mathf.Clamp01(anchor.x) - 0.5f;
            anchor.y = Mathf.Clamp01(anchor.y) - 0.5f;

            // if it's behind the camera, use NaN to flag it
            // otherwise, ignore Z
            anchor.z = anchor.z < 0 ? float.NaN : 0;
            _Corners[i] = anchor;
        }

        var area = TriangleArea(_Corners[0], _Corners[1], _Corners[2]) + TriangleArea(_Corners[1], _Corners[2], _Corners[3]);
        var midpoint = (_Corners[0] + _Corners[1] + _Corners[2] + _Corners[3]) * 0.25f;
        var score = area / Mathf.Max(midpoint.magnitude, 0.01f); // divide by distance from center to prioritize center
        return float.IsNaN(area) ? null : score;
    }

    private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5f;

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (Meta.XR.Editor.Callbacks.InitializeOnLoad.EditorReady)
        {
            UnityEngine.Assertions.Assert.IsNotNull(OVROverlayCanvasSettings.Instance);
        }
#endif
    }

    private Vector3 GetRectTransformScale()
    {
        // Allow the rect transform to scale non-uniformly (often z scale may be different than x and y)
        Vector3 localScale = rectTransform.localScale;

        Vector3 parentScale = rectTransform.parent != null ? rectTransform.parent.lossyScale : Vector3.one;
        // Check that the parent scale is uniform (otherwise our lossy scale might produce unexpected results)
        if (!Mathf.Approximately(parentScale.x, parentScale.y) || !Mathf.Approximately(parentScale.y, parentScale.z))
        {
            if (!_nonUniformScaleWarningShown)
            {
                Debug.LogWarning($"[OVROverlayCanvas][{name}] Non Uniform Parent Scale. This will result in unexpected behavior!", this);
                _nonUniformScaleWarningShown = true;
            }
        }
        return new Vector3(parentScale.x * localScale.x, parentScale.y * localScale.y, parentScale.z * localScale.z);
    }

    private Matrix4x4 GetWorldToViewportMatrix(Camera mainCamera)
    {
#if USING_XR_SDK
        XRDisplaySubsystem currentDisplaySubsystem = OVRManager.GetCurrentDisplaySubsystem();
        if (currentDisplaySubsystem != null && currentDisplaySubsystem.GetRenderPassCount() > 0)
        {
            currentDisplaySubsystem.GetRenderPass(0, out var renderPass);
            renderPass.GetRenderParameter(mainCamera, 0, out var renderParameter);
            return renderParameter.projection * mainCamera.worldToCameraMatrix;
        }
        else
#endif
        {
            return mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
        }
    }

    private (int pixelWidth, int pixelHeight)? CalculateScaledResolution()
    {
#if UNITY_EDITOR
        if (!ShouldScaleViewport || !Application.isPlaying)
#else
        if (!ShouldScaleViewport)
#endif
        {
            return (_renderTexture.width, _renderTexture.height);
        }

        if (!IsInFrustum())
        {
            return (32, 32);
        }

        var mainCamera = OVRManager.FindMainCamera();
        if (mainCamera == null)
            return null;

        if (!_optimalResolutionInitialized && UnityEngine.XR.XRSettings.isDeviceActive)
        {
            // Calculate Optimal resolution relative to the default resolution
            _optimalResolutionWidth = UnityEngine.XR.XRSettings.eyeTextureWidth
                * kOptimalResolutionScale / UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
            _optimalResolutionHeight = UnityEngine.XR.XRSettings.eyeTextureHeight
                 * kOptimalResolutionScale / UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
            // Don't consider the resolution initialized until the resolution is greater than zero
            _optimalResolutionInitialized = _optimalResolutionWidth > 0 && _optimalResolutionHeight > 0;
        }

        rectTransform.GetLocalCorners(_Corners);

        var localToWorldMatrix = rectTransform.localToWorldMatrix;
        if (shape == CanvasShape.Curved)
        {
            // for curve, the world corners aren't a great way to determine texture scale.
            // To get more accurate results, apply billboard rotation to the rect based on the curve
            // so that our corners better approximate the resolution needed.
            localToWorldMatrix *= CalculateCurveViewBillboardMatrix(mainCamera);
        }

        var worldToViewport = GetWorldToViewportMatrix(mainCamera);
        var viewportToTexture =
            Matrix4x4.Scale(new Vector3(0.5f * _optimalResolutionWidth, 0.5f * _optimalResolutionHeight, 0.0f));

        var rectToTexture = viewportToTexture * worldToViewport * localToWorldMatrix;
        // Calculate Clip Pos for our quad
        for (int i = 0; i < 4; i++)
        {
            _Corners[i] = rectToTexture.MultiplyPoint(_Corners[i]);
        }

        // Because our quad might be rotated, we find the raw max pixel length of each quad side
        int height = Mathf.RoundToInt(Mathf.Max((_Corners[1] - _Corners[0]).magnitude, (_Corners[3] - _Corners[2]).magnitude));
        int width = Mathf.RoundToInt(Mathf.Max((_Corners[2] - _Corners[1]).magnitude, (_Corners[3] - _Corners[0]).magnitude));

        // round to the nearest even pixel size, with 2 pixels of padding on all sides
        int pixelHeight = ((height + 1) / 2) * 2 * (expensive ? 2 : 1) + 4;
        int pixelWidth = ((width + 1) / 2) * 2 * (expensive ? 2 : 1) + 4;

        // clamp our viewport to the texture size
        pixelHeight = Mathf.Clamp(pixelHeight, 32, _renderTexture.height);
        pixelWidth = Mathf.Clamp(pixelWidth, 32, _renderTexture.width);
        return (pixelWidth, pixelHeight);
    }

    private void ApplyViewportScale()
    {
        if (CalculateScaledResolution() is not var (pixelWidth, pixelHeight))
            return;

        // Don't change texture sizes unless our image would change more than four pixels to avoid judder
        if (Math.Abs(pixelHeight - _lastPixelHeight) < 4 && Math.Abs(pixelWidth - _lastPixelWidth) < 4)
        {
            pixelWidth = _lastPixelWidth;
            pixelHeight = _lastPixelHeight;
        }
        else
        {
            _lastPixelHeight = pixelHeight;
            _lastPixelWidth = pixelWidth;
        }

        // subtract the two pixels from all sides
        int innerPixelHeight = pixelHeight - 4;
        int innerPixelWidth = pixelWidth - 4;

        var rectTransformScale = GetRectTransformScale();
        float orthoHeight = rectTransform.rect.height * rectTransformScale.y *
            pixelHeight / (float)innerPixelHeight;
        float orthoWidth = rectTransform.rect.width * rectTransformScale.x *
            pixelWidth / (float)innerPixelWidth;

        _camera.orthographicSize = (0.5f * orthoHeight);
        _camera.aspect = (orthoWidth / orthoHeight);

        float sizeX = pixelWidth / (float)_renderTexture.width;
        float sizeY = pixelHeight / (float)_renderTexture.height;

        float innerSizeX = innerPixelWidth / (float)_renderTexture.width;
        float innerSizeY = innerPixelHeight / (float)_renderTexture.height;

        // scale the camera rect
        _camera.rect = new Rect((1 - sizeX) / 2, ((1 - sizeY) / 2), sizeX, sizeY);

        Rect src = new Rect(0.5f - (0.5f * innerSizeX), (0.5f - (0.5f * innerSizeY)), innerSizeX, innerSizeY);
        Rect dst = new Rect(0, 0, 1, 1);

        // update the overlay to use this same size
        _overlay.overrideTextureRectMatrix = true;
        _overlay.SetSrcDestRects(src, src, dst, dst);

        // Update our material offset and scale
        var pixelBorder = ShouldScaleViewport ? 0 : 8;
        var res = new Vector2(pixelWidth, pixelHeight);
        var pixelSize = new Vector2(1.0f / pixelWidth, 1.0f / pixelHeight);
        _imposterTextureOffset = (src.min * res + Vector2.one * pixelBorder) * pixelSize;
        _imposterTextureScale = (src.size * res - Vector2.one * pixelBorder * 2) * pixelSize;
    }

    public struct ScopedCallback : IDisposable
    {
        public event Action OnDispose;
        void IDisposable.Dispose() => OnDispose?.Invoke();
    }

    private void RenderCamera()
    {
        _camera.transform.position = transform.position - _camera.transform.forward;

        var rect = _camera.rect;
        int pixWidth = (int)(rect.width * _renderTexture.width);
        int pixHeight = (int)(rect.height * _renderTexture.height);

        using var scopedCallback = new ScopedCallback();

        if (GraphicsSettings.defaultRenderPipeline == null)
        {
            // switch all targeted renderers to another layer, so we don't render things outside of this object
            _camera.cullingMask = 1 << CanvasRenderLayer;

            var targetLayer = gameObject.layer;
            var transforms = GetComponentsInChildren<Transform>();
            foreach (var tf in transforms)
                if (tf.gameObject.layer == targetLayer)
                    tf.gameObject.layer = CanvasRenderLayer;

            scopedCallback.OnDispose += () =>
            {
                // revert to the original layers
                foreach (var tf in transforms)
                    if (tf.gameObject.layer == CanvasRenderLayer)
                        tf.gameObject.layer = targetLayer;
            };
        }
        else
        {
            _camera.cullingMask = 1 << gameObject.layer;
        }

        if (_useTempRT && (pixWidth < _renderTexture.width || pixHeight < _renderTexture.height))
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(pixWidth, pixHeight,
                GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt, 0);
            var tempRT = RenderTexture.GetTemporary(descriptor);
            tempRT.Create();

            // override render texture with the temporary one
            _camera.targetTexture = tempRT;
            _camera.rect = new Rect(0, 0, 1, 1);
            _camera.Render();

            // Copy to our original render texture, then release the temporary texture
            Graphics.CopyTexture(tempRT, 0, 0, 0, 0, pixWidth, pixHeight, _renderTexture, 0, 0, (int)(rect.x * _renderTexture.width), (int)(rect.y * _renderTexture.height));
            RenderTexture.ReleaseTemporary(tempRT);

            // restore original target rect and texture
            _camera.rect = rect;
            _camera.targetTexture = _renderTexture;
        }
        else
        {
            _camera.Render();
        }
    }

    // Calculate a billboard rotation of our rect based on the curve parameters and the current camera position
    private Matrix4x4 CalculateCurveViewBillboardMatrix(Camera mainCamera)
    {
        var relativeViewPos = Quaternion.Inverse(rectTransform.rotation) *
                              (mainCamera.transform.position - rectTransform.position);

        // calculate the billboard angle based on the x and z positions
        float angle = Mathf.Atan2(-relativeViewPos.x, -relativeViewPos.z);

        Vector3 lossyScale = GetRectTransformScale();
        float fullAngleWidth = rectTransform.rect.width * lossyScale.x / curveRadius;

        // clamp angle to the maximum curvature
        angle = Mathf.Clamp(angle, -0.5f * fullAngleWidth, 0.5f * fullAngleWidth);

        // calculate the tangent point to keep our billboard touching the curve surface
        var tangentPoint = new Vector3(angle * curveRadius, 0, 0);
        // Set the pivot point to the curve center
        var pivotPoint = new Vector3(0, 0, curveRadius);

        // calculate our offset rotation matrix, which consists of first applying x and y scale,
        // offsetting to the pivot location minus the tangent point, rotating, reversing the pivot offset, then
        // reversing the scale multiplication. Note that z scale is removed entirely.
        return Matrix4x4.Scale(new Vector3(1.0f / lossyScale.x, 1.0f / lossyScale.y, 1.0f / lossyScale.z)) *
               Matrix4x4.Translate(-pivotPoint) *
               Matrix4x4.Rotate(Quaternion.AngleAxis(Mathf.Rad2Deg * angle, Vector3.up)) *
               Matrix4x4.Translate(pivotPoint - tangentPoint) *
               Matrix4x4.Scale(new Vector3(lossyScale.x, lossyScale.y, 1.0f));
    }


    public override Ray TransformRay(Ray ray)
    {
        if (shape != CanvasShape.Curved)
        {
            return ray;
        }

        // Transform from 3D space to Curved UI Space
        var localPoint = transform.InverseTransformPoint(ray.origin);
        var localDirection = transform.InverseTransformDirection(ray.direction);

        var localRadius = curveRadius / transform.lossyScale.z;
        var localCenter = new Vector3(0, 0, -localRadius);

        // find xz intersect with circle

        if (!LineCircleIntersection(new Vector2(localPoint.x, localPoint.z),
                new Vector2(localDirection.x, localDirection.z), new Vector2(localCenter.x, localCenter.z), localRadius,
                out float distance))
        {
            // the ray misses, return a ray parallel to the canvas
            return new Ray(ray.origin, transform.right);
        }

        Vector3 localIntersection = localPoint + localDirection * distance;

        // convert circle coordinates to plane coordinates by getting angle
        float angle = Mathf.Atan2(localIntersection.x, localIntersection.z + localRadius);
        float xPos = angle * localRadius;
        float yPos = localIntersection.y;

        // return a ray going directly into our calculated intersection point
        return new Ray(transform.TransformPoint(new Vector3(xPos, yPos, -1)), transform.forward);
    }

    private static bool LineCircleIntersection(Vector2 p1, Vector2 dp, Vector2 center, float radius, out float distance)
    {
        //  Find intersection with circle using quadratic equation
        var a = dp.sqrMagnitude;
        var b = 2 * Vector2.Dot(dp, p1 - center);
        var c = center.sqrMagnitude;
        c += p1.sqrMagnitude;
        c -= 2 * Vector2.Dot(center, p1);
        c -= radius * radius;
        var bb4ac = b * b - 4 * a * c;
        if (Mathf.Abs(a) < float.Epsilon || bb4ac < 0)
        {
            //  line does not intersect
            distance = default;
            return false;
        }
        var mu1 = (-b - Mathf.Sqrt(bb4ac)) / (2 * a);
        var mu2 = (-b + Mathf.Sqrt(bb4ac)) / (2 * a);

        distance = mu1 >= 0 ? mu1 : mu2;
        return true;
    }

    public OVROverlay Overlay => _overlay;

    public void SetFrameDirty() => _frameIsReady = false;

    public void SetCanvasLayer(int layer, bool forceUpdate)
    {
        SetLayerRecursive(gameObject, layer, gameObject.layer, forceUpdate);
    }

    private static void SetLayerRecursive(GameObject gameObject, int layer, int previousLayer, bool forceUpdate)
    {
        if (gameObject.layer == previousLayer || forceUpdate)
        {
            gameObject.layer = layer;
        }

        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            var c = gameObject.transform.GetChild(i).gameObject;
            if ((c.hideFlags &= HideFlags.DontSave) != 0)
            {
                continue;
            }
            SetLayerRecursive(c, layer, previousLayer, forceUpdate);
        }
    }
}
