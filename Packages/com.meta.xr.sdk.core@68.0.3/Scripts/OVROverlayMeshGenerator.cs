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
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
///     When attached to a GameObject with an OVROverlay component, OVROverlayMeshGenerator will use a mesh renderer
///     to preview the appearance of the OVROverlay as it would appear as a TimeWarp overlay on a headset.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteAlways]
[HelpURL("https://developer.oculus.com/documentation/unity/unity-sf-stereo180video/")]
public class OVROverlayMeshGenerator : MonoBehaviour
{

    private readonly List<int> _Tris = new List<int>();
    private readonly List<Vector2> _UV = new List<Vector2>();
    private readonly List<Vector4> _CubeUV = new List<Vector4>();
    private readonly List<Vector3> _Verts = new List<Vector3>();
    private Transform _CameraRoot;
    private Rect _LastDestRectLeft;
    private Vector3 _LastPosition;
    private Quaternion _LastRotation;
    private Vector3 _LastScale;
    private TextureDimension _LastTextureDimension = TextureDimension.Tex2D;

    private OVROverlay.OverlayShape _LastShape;
    private Rect _LastSrcRectLeft;

    private Mesh _Mesh;
    private MeshCollider _MeshCollider;
    private MeshFilter _MeshFilter;
    private MeshRenderer _MeshRenderer;
    private OVROverlay _Overlay;
    private Transform _Transform;
    private Material _PreviewMaterial;

    protected void OnEnable()
    {
        Initialize();
    }

    protected void OnDestroy()
    {
        if (_Mesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_Mesh);
            }
            else
            {
                DestroyImmediate(_Mesh);
            }
        }

        if (_PreviewMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_PreviewMaterial);
            }
            else
            {
                DestroyImmediate(_PreviewMaterial);
            }
        }
    }

#if UNITY_EDITOR
    protected void Update()
    {
        TryUpdateMesh();
    }
#endif

    private void Initialize()
    {
        _MeshFilter = GetComponent<MeshFilter>();
        _MeshRenderer = GetComponent<MeshRenderer>();
        _MeshCollider = GetComponent<MeshCollider>();

        _Transform = transform;

        if (Camera.main && Camera.main.transform.parent)
        {
            _CameraRoot = Camera.main.transform.parent;
        }

        TryUpdateMesh();
    }

    public void SetOverlay(OVROverlay overlay)
    {
        _Overlay = overlay;
        Initialize();
    }

    private void TryUpdateMesh()
    {
        if (_Overlay == null)
        {
            return;
        }

        if (_Mesh == null)
        {
            _Mesh = new Mesh { name = "Overlay" };
            _Mesh.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        }

        if (_Transform == null)
        {
            _Transform = transform;
        }

        OVROverlay.OverlayShape shape = _Overlay.currentOverlayShape;
        Vector3 position = _CameraRoot ? _Transform.position - _CameraRoot.position : _Transform.position;
        Quaternion rotation = _Transform.rotation;
        Vector3 scale = _Transform.lossyScale;
        Rect destRectLeft = _Overlay.overrideTextureRectMatrix ? _Overlay.destRectLeft : new Rect(0, 0, 1, 1);
        Rect srcRectLeft = _Overlay.overrideTextureRectMatrix ? _Overlay.srcRectLeft : new Rect(0, 0, 1, 1);
        Texture texture = _Overlay.textures[0];
        TextureDimension dimension = texture != null ? texture.dimension : TextureDimension.Tex2D;

        // Re-generate the mesh if necessary
        if (_LastShape != shape ||
            _LastPosition != position ||
            _LastRotation != rotation ||
            _LastScale != scale ||
            _LastDestRectLeft != destRectLeft ||
            _LastTextureDimension != dimension)
        {
            UpdateMesh(shape, position, rotation, scale, destRectLeft, dimension == TextureDimension.Cube);
        }

        // Generate the material and update textures if necessary
        if (_MeshRenderer)
        {
            if (_MeshRenderer.sharedMaterial == null || dimension != _LastTextureDimension)
            {
                if (_PreviewMaterial != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(_PreviewMaterial);
                    }
                    else
                    {
                        DestroyImmediate(_PreviewMaterial);
                    }
                }

                _PreviewMaterial = null;
                switch (dimension)
                {
                    case TextureDimension.Tex2D:
                        _PreviewMaterial = new Material(Shader.Find("Unlit/Transparent"));

                        break;
                    case TextureDimension.Cube:
                        _PreviewMaterial = new Material(Shader.Find("Hidden/CubeCopy"));

                        break;
                }

                if (_PreviewMaterial != null)
                {
                    _PreviewMaterial.mainTexture = texture;
                }
                _MeshRenderer.sharedMaterial = _PreviewMaterial;
            }

            if (_LastSrcRectLeft != srcRectLeft)
            {
                _MeshRenderer.sharedMaterial.mainTextureOffset = srcRectLeft.position;
                _MeshRenderer.sharedMaterial.mainTextureScale = srcRectLeft.size;
            }

            if (_MeshRenderer.sharedMaterial.mainTexture != texture)
            {
                _MeshRenderer.sharedMaterial.mainTexture = texture;
            }
        }

        if (_MeshFilter)
        {
            _MeshFilter.sharedMesh = _Mesh;
        }

        if (_MeshCollider)
        {
            _MeshCollider.sharedMesh = _Mesh;
        }

        _LastShape = shape;
        _LastPosition = position;
        _LastRotation = rotation;
        _LastScale = scale;
        _LastDestRectLeft = destRectLeft;
        _LastSrcRectLeft = srcRectLeft;
        _LastTextureDimension = dimension;
    }

    private void UpdateMesh(OVROverlay.OverlayShape shape, Vector3 position, Quaternion rotation, Vector3 scale,
        Rect rect, bool cubemap = false)
    {
        _Verts.Clear();
        _UV.Clear();
        _CubeUV.Clear();
        _Tris.Clear();

        GenerateMesh(_Verts, _UV, _CubeUV, _Tris, shape, position, rotation, scale, rect);

        _Mesh.Clear(false);
        _Mesh.SetVertices(_Verts);

        if (cubemap)
        {
            _Mesh.SetUVs(0, _CubeUV);
        }
        else
        {
            _Mesh.SetUVs(0, _UV);
        }
        _Mesh.SetTriangles(_Tris, 0);
        _Mesh.UploadMeshData(false);
    }

    public static void GenerateMesh(List<Vector3> verts, List<Vector2> uvs, List<Vector4> cubeUVs, List<int> tris,
        OVROverlay.OverlayShape shape, Vector3 position, Quaternion rotation, Vector3 scale, Rect rect)
    {
        switch (shape)
        {
            case OVROverlay.OverlayShape.Equirect:
                BuildSphere(verts, uvs, tris, position, rotation, scale, rect);

                break;
            case OVROverlay.OverlayShape.Cubemap:
            case OVROverlay.OverlayShape.OffcenterCubemap:
                BuildCube(verts, uvs, cubeUVs, tris, position, rotation, scale);

                break;
            case OVROverlay.OverlayShape.Quad:
                BuildQuad(verts, uvs, tris, rect);

                break;
            case OVROverlay.OverlayShape.Cylinder:
                BuildHemicylinder(verts, uvs, tris, scale, rect);

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 InverseTransformVert(in Vector3 vert, in Vector3 position, in Vector3 scale, float worldScale)
    {
        return new Vector3(
            (worldScale * vert.x - position.x) / scale.x,
            (worldScale * vert.y - position.y) / scale.y,
            (worldScale * vert.z - position.z) / scale.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 GetSphereUV(float theta, float phi, float expandScale)
    {
        float thetaU = expandScale * (theta / (2 * Mathf.PI) - 0.5f) + 0.5f;
        float phiV = expandScale * phi / Mathf.PI + 0.5f;

        return new Vector2(thetaU, phiV);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 GetSphereVert(float theta, float phi)
    {
        return new Vector3(-Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(phi), -Mathf.Cos(theta) * Mathf.Cos(phi));
    }

    public static void BuildSphere(List<Vector3> verts, List<Vector2> uv, List<int> triangles, Vector3 position,
        Quaternion rotation, Vector3 scale, Rect rect, float worldScale = 800, int latitudes = 128,
        int longitudes = 128, float expandCoefficient = 1.0f)
    {
        position = Quaternion.Inverse(rotation) * position;

        latitudes = Mathf.CeilToInt(latitudes * rect.height);
        longitudes = Mathf.CeilToInt(longitudes * rect.width);

        float minTheta = Mathf.PI * 2.0f * rect.x;
        float minPhi = Mathf.PI * (0.5f - rect.y - rect.height);

        float thetaScale = Mathf.PI * 2.0f * rect.width / longitudes;
        float phiScale = Mathf.PI * rect.height / latitudes;

        float expandScale = 1.0f / expandCoefficient;

        for (int j = 0; j < latitudes + 1; j += 1)
        {
            for (int k = 0; k < longitudes + 1; k++)
            {
                float theta = minTheta + k * thetaScale;
                float phi = minPhi + j * phiScale;

                Vector2 suv = GetSphereUV(theta, phi, expandScale);
                uv.Add(new Vector2((suv.x - rect.x) / rect.width, (suv.y - rect.y) / rect.height));
                Vector3 vert = GetSphereVert(theta, phi);
                verts.Add(InverseTransformVert(in vert, in position, in scale, worldScale));
            }
        }

        for (int j = 0; j < latitudes; j++)
        {
            for (int k = 0; k < longitudes; k++)
            {
                triangles.Add(j * (longitudes + 1) + k);
                triangles.Add((j + 1) * (longitudes + 1) + k);
                triangles.Add((j + 1) * (longitudes + 1) + k + 1);
                triangles.Add((j + 1) * (longitudes + 1) + k + 1);
                triangles.Add(j * (longitudes + 1) + k + 1);
                triangles.Add(j * (longitudes + 1) + k);
            }
        }
    }

    private enum CubeFace
    {
        Bottom,
        Front,
        Back,
        Right,
        Left,
        Top,
        COUNT
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 GetCubeUV(CubeFace face, float sideU, float sideV, float expandScale, float expandOffset)
    {
        sideU = sideU * expandScale + expandOffset;
        sideV = sideV * expandScale + expandOffset;

        switch (face)
        {
            case CubeFace.Bottom:
                return new Vector2(sideU / 3.0f, sideV / 2.0f);
            case CubeFace.Front:
                return new Vector2((1.0f + sideU) / 3.0f, sideV / 2.0f);
            case CubeFace.Back:
                return new Vector2((2.0f + sideU) / 3.0f, sideV / 2.0f);
            case CubeFace.Right:
                return new Vector2(sideU / 3.0f, (1.0f + sideV) / 2.0f);
            case CubeFace.Left:
                return new Vector2((1.0f + sideU) / 3.0f, (1.0f + sideV) / 2.0f);
            case CubeFace.Top:
                return new Vector2((2.0f + sideU) / 3.0f, (1.0f + sideV) / 2.0f);
            default:
                return Vector2.zero;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 GetCubeVert(CubeFace face, float sideU, float sideV)
    {
        switch (face)
        {
            case CubeFace.Bottom:
                return new Vector3(0.5f - sideU, -0.5f, 0.5f - sideV);
            case CubeFace.Front:
                return new Vector3(0.5f - sideU, -0.5f + sideV, -0.5f);
            case CubeFace.Back:
                return new Vector3(-0.5f + sideU, -0.5f + sideV, 0.5f);
            case CubeFace.Right:
                return new Vector3(-0.5f, -0.5f + sideV, -0.5f + sideU);
            case CubeFace.Left:
                return new Vector3(0.5f, -0.5f + sideV, 0.5f - sideU);
            case CubeFace.Top:
                return new Vector3(0.5f - sideU, 0.5f, -0.5f + sideV);
            default:
                return Vector3.zero;
        }
    }

    public static void BuildCube(List<Vector3> verts, List<Vector2> uv, List<Vector4> cubeUV, List<int> triangles, Vector3 position,
        Quaternion rotation, Vector3 scale, float worldScale = 800, int subQuads = 1, float expandCoefficient = 1.01f)
    {
        position = Quaternion.Inverse(rotation) * position;

        int vertsPerSide = (subQuads + 1) * (subQuads + 1);

        float expandScale = 1.0f / expandCoefficient;
        float expandOffset = 0.5f - 0.5f / expandCoefficient;

        for (int i = 0; i < (int)CubeFace.COUNT; i++)
        {
            for (int j = 0; j < subQuads + 1; j++)
            {
                for (int k = 0; k < subQuads + 1; k++)
                {
                    float sideU = j / (float)subQuads;
                    float sideV = k / (float)subQuads;

                    uv.Add(GetCubeUV((CubeFace)i, sideU, sideV, expandScale, expandOffset));
                    Vector3 vert = GetCubeVert((CubeFace)i, sideU, sideV);
                    verts.Add(InverseTransformVert(in vert, in position, in scale, worldScale));
                    cubeUV.Add(vert.normalized);
                }
            }

            for (int j = 0; j < subQuads; j++)
            {
                for (int k = 0; k < subQuads; k++)
                {
                    triangles.Add(vertsPerSide * i + (j + 1) * (subQuads + 1) + k);
                    triangles.Add(vertsPerSide * i + j * (subQuads + 1) + k);
                    triangles.Add(vertsPerSide * i + (j + 1) * (subQuads + 1) + k + 1);
                    triangles.Add(vertsPerSide * i + (j + 1) * (subQuads + 1) + k + 1);
                    triangles.Add(vertsPerSide * i + j * (subQuads + 1) + k);
                    triangles.Add(vertsPerSide * i + j * (subQuads + 1) + k + 1);
                }
            }
        }
    }


    public static void BuildQuad(List<Vector3> verts, List<Vector2> uv, List<int> triangles, Rect rect)
    {
        verts.Add(new Vector3(rect.x - 0.5f, 1.0f - rect.y - rect.height - 0.5f, 0.0f));
        verts.Add(new Vector3(rect.x - 0.5f, 1.0f - rect.y - 0.5f, 0.0f));
        verts.Add(new Vector3(rect.x + rect.width - 0.5f, 1.0f - rect.y - 0.5f, 0.0f));
        verts.Add(new Vector3(rect.x + rect.width - 0.5f, 1.0f - rect.y - rect.height - 0.5f, 0.0f));

        uv.Add(new Vector2(0.0f, 0.0f));
        uv.Add(new Vector2(0.0f, 1.0f));
        uv.Add(new Vector2(1.0f, 1.0f));
        uv.Add(new Vector2(1.0f, 0.0f));

        triangles.Add(0);
        triangles.Add(1);
        triangles.Add(2);
        triangles.Add(2);
        triangles.Add(3);
        triangles.Add(0);
    }

    public static void BuildHemicylinder(List<Vector3> verts, List<Vector2> uv, List<int> triangles, Vector3 scale,
        Rect rect, int longitudes = 128)
    {
        float height = Mathf.Abs(scale.y) * rect.height;
        float radius = scale.z;
        float arcLength = scale.x * rect.width;

        float arcAngle = arcLength / radius;
        float minAngle = scale.x * (-0.5f + rect.x) / radius;

        int columns = Mathf.CeilToInt(longitudes * arcAngle / (2.0f * Mathf.PI));

        // we don't want super tall skinny triangles because that can lead to artifacting.
        // make triangles no more than 2x taller than wide

        float triangleWidth = arcLength / columns;
        float ratio = height / triangleWidth;

        int rows = Mathf.CeilToInt(ratio / 2.0f);

        for (int j = 0; j < rows + 1; j++)
        {
            for (int k = 0; k < columns + 1; k++)
            {
                uv.Add(new Vector2(k / (float)columns, 1.0f - j / (float)rows));

                verts.Add(new Vector3(
                    // because the scale is used to control the parameters, we need
                    // to reverse multiply by scale to appear correctly
                    Mathf.Sin(minAngle + k * arcAngle / columns) * radius / scale.x,
                    0.5f - rect.y - rect.height + rect.height * (1.0f - j / (float)rows),
                    Mathf.Cos(minAngle + k * arcAngle / columns) * radius / scale.z));
            }
        }

        for (int j = 0; j < rows; j++)
        {
            for (int k = 0; k < columns; k++)
            {
                triangles.Add(j * (columns + 1) + k);
                triangles.Add((j + 1) * (columns + 1) + k + 1);
                triangles.Add((j + 1) * (columns + 1) + k);
                triangles.Add((j + 1) * (columns + 1) + k + 1);
                triangles.Add(j * (columns + 1) + k);
                triangles.Add(j * (columns + 1) + k + 1);
            }
        }
    }

}
