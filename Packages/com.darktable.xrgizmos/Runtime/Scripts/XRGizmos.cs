using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Utilities.XR
{
    public static partial class XRGizmos
    {
        // This must be defined in player settings for your platform is you want gizmos to render
        private const string k_XRGizmosDefine = "XR_GIZMOS";

        // Unity docs say you can only submit 1023 matrices per RenderMeshInstanced call
        private const int k_MaxInstances = 1023;

        // Make sure the selected shader is referenced in your project and supports instancing
        // If gizmos render in editor, but not on device it may be because the
        // instancing variants were stripped from the build.
        // In "graphics settings" you can add the shader to "always included shaders" and
        // set "instancing variants" from "strip unused" to "keep all" to avoid this.
        private const string k_ShaderName = "Unlit/XRGizmos";

        // Make sure this property matches the name of the Color/Tint property in the shader
        private static readonly int k_ColorID = Shader.PropertyToID("_Color");

        // All bets are off if this number isn't even.
        private const int k_CircleSegments = 24;
        private const int k_SphereSegments = k_CircleSegments * 3;
        private const int k_HemisphereSegments = (k_CircleSegments * 2) + 2;
        private const float k_LineThickness = 0.003f;

        private static Mesh s_SphereMesh;
        private static Mesh s_CubeMesh;
        private static Mesh s_QuadMesh;

        private static Material s_GizmoMaterial;
        private static MaterialPropertyBlock s_GizmoProperties;
        private static RenderParams s_RenderParams;

        private static readonly Vector3[] k_TRSPoints = new Vector3[k_MaxInstances];

        private static NativeArray<Matrix4x4> s_Matrices;

        private static readonly Vector3[] k_UnitCirclePoints = new Vector3[k_CircleSegments];
        private static readonly Vector3[] k_UnitSpherePoints = new Vector3[k_SphereSegments];
        private static readonly Vector3[] k_UnitHemiSpherePoints = new Vector3[k_HemisphereSegments];

        private static readonly Vector3[] k_UnitCubePoints = new Vector3[8]
        {
            new Vector3(0.50f, 0.50f, 0.50f),
            new Vector3(-0.50f, 0.50f, 0.50f),
            new Vector3(-0.50f, -0.50f, 0.50f),
            new Vector3(0.50f, -0.50f, 0.50f),

            new Vector3(0.50f, 0.50f, -0.50f),
            new Vector3(-0.50f, 0.50f, -0.50f),
            new Vector3(-0.50f, -0.50f, -0.50f),
            new Vector3(0.50f, -0.50f, -0.50f),
        };

        private static readonly Vector3[] k_UnitRectanglePoints = new Vector3[4]
        {
            new Vector3(-0.50f, 0.00f, -0.50f),
            new Vector3(-0.50f, 0.00f, 0.50f),
            new Vector3(0.50f, 0.00f, 0.50f),
            new Vector3(0.50f, 0.00f, -0.50f),
        };

        private static readonly Vector3[] k_UnitArrowPoints = new Vector3[4]
        {
            new Vector3(0.0f, 0.0f, 0.50f),
            new Vector3(0.25f, 0.0f, -0.25f),
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(-0.25f, 0.0f, -0.25f),
        };

        private static readonly Vector3[] k_UnitPointerPoints = new Vector3[8]
        {
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.25f, 0.0f, -0.5f),
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(-0.25f, 0.0f, -0.5f),
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.25f, -0.5f),
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.0f, -0.25f, -0.5f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        [Conditional(k_XRGizmosDefine)]
        private static void Initialize()
        {
            if (s_Matrices.IsCreated)
            {
                s_Matrices.Dispose();
            }

            s_Matrices = new NativeArray<Matrix4x4>(k_MaxInstances, Allocator.Persistent);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var meshFilter = cube.GetComponent<MeshFilter>();
            s_CubeMesh = meshFilter.sharedMesh;
            Object.Destroy(cube);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            meshFilter = sphere.GetComponent<MeshFilter>();
            s_SphereMesh = meshFilter.sharedMesh;
            Object.Destroy(sphere);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            meshFilter = quad.GetComponent<MeshFilter>();
            s_QuadMesh = meshFilter.sharedMesh;
            Object.Destroy(quad);

            s_GizmoProperties = new MaterialPropertyBlock();

            var shader = Shader.Find(k_ShaderName);

            s_GizmoMaterial = new Material(shader)
            {
                enableInstancing = true,
            };

            s_RenderParams = new RenderParams(s_GizmoMaterial)
            {
                matProps = s_GizmoProperties,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                lightProbeUsage = LightProbeUsage.Off,
                shadowCastingMode = ShadowCastingMode.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };

            BuildCircleData();

            Application.quitting += OnApplicationQuitting;
        }

        /// <summary>
        ///   <para>Draws a line starting at from towards to.</para>
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawLine(Vector3 from, Vector3 to, Color color, float lineThickness = k_LineThickness)
        {
            if (!TryGetLineMatrix(from, to, lineThickness, out var matrix))
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            Graphics.RenderMesh(in s_RenderParams, s_CubeMesh, 0, matrix);
        }

        /// <summary>
        ///   <para>Draws a ray starting at from along direction for length.</para>
        /// </summary>
        /// <param name="from"></param>
        /// <param name="direction"></param>
        /// <param name="color"></param>
        /// <param name="length"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawRay(Vector3 from, Vector3 direction, Color color, float length = 1f, float lineThickness = k_LineThickness)
        {
            if (direction == Vector3.zero)
                return;

            var ray = new Ray(from, direction);
            var end = ray.GetPoint(length);

            DrawLine(from, end, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a ray of length length.</para>
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="color"></param>
        /// <param name="length"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawRay(Ray ray, Color color, float length = 1f, float lineThickness = k_LineThickness)
        {
            if (ray.direction == Vector3.zero)
            {
                return;
            }

            var end = ray.GetPoint(length);

            DrawLine(ray.origin, end, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a 3d plus marking a point.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="color"></param>
        /// <param name="size"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawPoint(Vector3 center, Color color, float size = 0.1f, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);
            var lines = 0;

            var offset = new Vector3(size / 2.0f, 0, 0);
            var start = center - offset;
            var end = center + offset;

            TryGetLineMatrix(start, end, lineThickness, out var matrix);
            s_Matrices[lines++] = matrix;

            offset.Set(0, size / 2.0f, 0);
            start = center - offset;
            end = center + offset;

            TryGetLineMatrix(start, end, lineThickness, out matrix);
            s_Matrices[lines++] = matrix;

            offset.Set(0, 0, size / 2.0f);
            start = center - offset;
            end = center + offset;

            TryGetLineMatrix(start, end, lineThickness, out matrix);
            s_Matrices[lines++] = matrix;

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draws a solid sphere with center and radius.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawSphere(Vector3 center, float radius, Color color)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var sphereMatrix = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius);
            Graphics.RenderMesh(in s_RenderParams, s_SphereMesh, 0, sphereMatrix);
        }

        /// <summary>
        ///   <para>Draw a solid box at center with size.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="size"></param>
        /// <param name="color"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCube(Vector3 center, Quaternion rotation, Vector3 size, Color color)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var cubeMatrix = Matrix4x4.TRS(center, rotation, size);
            Graphics.RenderMesh(in s_RenderParams, s_CubeMesh, 0, cubeMatrix);
        }

        /// <summary>
        ///   <para>Draws a circle with center and radius.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCircle(Vector3 center, Quaternion rotation, float radius, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, new Vector3(radius, radius, radius));

            for (var i = 0; i < k_CircleSegments; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitCirclePoints[i]);
            }

            for (var i = 0; i < k_CircleSegments; i++)
            {
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[(i + 1) % k_CircleSegments], lineThickness, out var matrix);
                s_Matrices[i] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, k_CircleSegments);
        }

        /// <summary>
        ///   <para>Draws a rectangle with position, rotation and scale.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="scale"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawRectangle(Vector3 center, Quaternion rotation, Vector2 scale, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, new Vector3(scale.x, 1, scale.y));

            for (var i = 0; i < 4; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitRectanglePoints[i]);
            }

            for (var i = 0; i < 4; i++)
            {
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[(i + 1) % 4], lineThickness, out var matrix);
                s_Matrices[i] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, 4);
        }

        /// <summary>
        ///   <para>Draws a wireframe sphere with center and radius.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawWireSphere(Vector3 center, float radius, Color color, float lineThickness = k_LineThickness)
        {
            DrawWireSphere(center, Quaternion.identity, radius, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a wireframe sphere with center, rotation and radius.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawWireSphere(Vector3 center, Quaternion rotation, float radius, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, new Vector3(radius, radius, radius));

            for (var i = 0; i < k_SphereSegments; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitSpherePoints[i]);
            }

            Matrix4x4 matrix;
            var lines = 0;
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < k_CircleSegments; j++)
                {
                    int a = j + (i * k_CircleSegments);
                    int b = a + 1;

                    TryGetLineMatrix(k_TRSPoints[a], k_TRSPoints[b % (k_CircleSegments * (i + 1))], lineThickness, out matrix);
                    s_Matrices[lines++] = matrix;
                }
            }

            // FIXME: This is a bit of a hack to stitch up the last line in the sphere.
            TryGetLineMatrix(k_TRSPoints[k_SphereSegments - 1], k_TRSPoints[k_CircleSegments * 2], lineThickness, out matrix);
            s_Matrices[lines - 1] = matrix;

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draws a wireframe hemisphere with center, rotation and radius.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawWireHemisphere(Vector3 center, Quaternion rotation, float radius, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, new Vector3(radius, radius, radius));

            for (var i = 0; i < k_HemisphereSegments; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitHemiSpherePoints[i]);
            }

            var lines = 0;
            for (var i = 0; i < k_HemisphereSegments - 1; i++)
            {
                if (i == (int)(k_CircleSegments * 1.5f))
                {
                    // skip the line between end of first semicircle and start of second.
                    continue;
                }

                var a = k_TRSPoints[i];
                var b = k_TRSPoints[i + 1];

                TryGetLineMatrix(a, b, lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        public static void DrawWireCapsule(Vector3 center, Quaternion rotation, float radius, float height, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var offset = new Vector3(0, Mathf.Max(0, height * 0.5f - radius), 0);

            // top of capsule
            var index = 0;
            var trs = Matrix4x4.TRS(offset, Quaternion.identity,new Vector3(radius, radius, radius));
            for (var i = 0; i < k_HemisphereSegments; i++)
            {
                k_TRSPoints[index++] = trs.MultiplyPoint3x4(k_UnitHemiSpherePoints[i]);
            }

            // bottom of capsule
            trs = Matrix4x4.TRS(-offset,Quaternion.Euler(180, 0, 0), new Vector3(radius, radius, radius));
            for (var i = 0; i < k_HemisphereSegments; i++)
            {
                k_TRSPoints[index++] = trs.MultiplyPoint3x4(k_UnitHemiSpherePoints[i]);
            }

            // lines down the side
            var top = new Vector3(radius, offset.y, 0);
            var bottom = new Vector3(radius, -offset.y, 0);

            var rotateLine = Quaternion.AngleAxis(90, Vector3.up);

            for (var i = 0; i < 4; i++)
            {
                top = rotateLine * top;
                bottom = rotateLine * bottom;

                k_TRSPoints[index++] = top;
                k_TRSPoints[index++] = bottom;
            }

            trs = Matrix4x4.TRS(center, rotation, Vector3.one);

            // now apply the transforms from the center + rotation.
            for (int i = 0; i < index; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_TRSPoints[i]);
            }

            Matrix4x4 matrix;
            var lines = 0;

            // generate lines for the caps
            for (var i = 0; i < (k_HemisphereSegments * 2) - 1; i++)
            {
                // skip lines
                if (i == (int)(k_CircleSegments * 1.5f) // first semicircle to second
                    || i == (k_CircleSegments * 2) + 1 // first hemisphere to second
                    || i == (int)(k_CircleSegments * 3.5f) + 2) // third semicircle to fourth
                {
                    continue;
                }

                var a = k_TRSPoints[i];
                var b = k_TRSPoints[i + 1];

                TryGetLineMatrix(a, b, lineThickness, out matrix);
                s_Matrices[lines++] = matrix;
            }

            // draw the lines on the side
            const int shift = k_HemisphereSegments * 2;
            for (var i = 0; i < 8; i += 2)
            {
                var a = k_TRSPoints[i + shift];
                var b = k_TRSPoints[i + 1 + shift];

                TryGetLineMatrix(a, b, lineThickness, out matrix);
                s_Matrices[lines++] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draw a wireframe box with center and size.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="size"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawWireCube(Vector3 center, Quaternion rotation, Vector3 size, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, size);

            for (var i = 0; i < 8; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitCubePoints[i]);
            }

            var lines = 0;

            for (var i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;

                // "top" of the cube
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[j], lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;

                // "bottom" of the cube
                TryGetLineMatrix(k_TRSPoints[i + 4], k_TRSPoints[j + 4], lineThickness, out matrix);
                s_Matrices[lines++] = matrix;

                // "sides" of the cube
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[i + 4], lineThickness, out matrix);
                s_Matrices[lines++] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draw a 2d arrow.</para>
        /// </summary>
        /// <param name="center"></param>
        /// <param name="rotation"></param>
        /// <param name="scale"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawArrow(Vector3 center, Quaternion rotation, Color color, float scale = 1.0f, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            var trs = Matrix4x4.TRS(center, rotation, new Vector3(scale, scale, scale));

            for (var i = 0; i < 4; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitArrowPoints[i]);
            }

            for (var i = 0; i < 4; i++)
            {
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[(i + 1) % 4], lineThickness, out var matrix);
                s_Matrices[i] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, 4);
        }

        /// <summary>
        ///   <para>Draw a ray with an arrow at the end.</para>
        /// </summary>
        /// <param name="from"></param>
        /// <param name="direction"></param>
        /// <param name="color"></param>
        /// <param name="scale"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawPointer(Vector3 from, Vector3 direction, Color color, float scale = 1.0f, float lineThickness = k_LineThickness)
        {
            if (direction == Vector3.zero)
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            var ray = new Ray(from, direction);
            var end = ray.GetPoint(scale);

            var lines = 0;

            TryGetLineMatrix(from, end, lineThickness, out var matrix);
            s_Matrices[lines++] = matrix;

            var rotation = Quaternion.LookRotation(direction);
            var pointerScale = Vector3.one * (scale * 0.25f);
            var trs = Matrix4x4.TRS(end, rotation, pointerScale);

            for (var i = 0; i < 8; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitPointerPoints[i]);
            }

            for (var i = 0; i < 8; i += 2)
            {
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[i + 1], lineThickness, out matrix);
                s_Matrices[lines++] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draw an axis representing a transform.</para>
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="length"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawAxis(Transform transform, float length = 0.25f, float lineThickness = k_LineThickness)
        {
            var pos = transform.position;

            var ray = new Ray(pos, transform.up);
            DrawRay(ray, Color.green, length, lineThickness);

            ray.direction = transform.right;
            DrawRay(ray, Color.red, length, lineThickness);

            ray.direction = transform.forward;
            DrawRay(ray, Color.blue, length, lineThickness);
        }

        /// <summary>
        ///   <para>Draw lines connecting a set of points.</para>
        /// </summary>
        /// <param name="points"></param>
        /// <param name="color"></param>
        /// <param name="closeLoop"></param>
        /// <param name="lineCount"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawLineList(IReadOnlyList<Vector3> points, Color color, bool closeLoop = false, int lineCount = -1, float lineThickness = k_LineThickness)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            var lines = 0;
            int count = lineCount <= 0 ? points.Count : lineCount;

            for (var i = 1; i < count; i++)
            {
                TryGetLineMatrix(points[i - 1], points[i], lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;

                if (lines + 2 >= k_MaxInstances)
                {
                    Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
                    lines = 0;
                }
            }

            if (closeLoop)
            {
                TryGetLineMatrix(points[count - 1], points[0], lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;
            }

            if (lines == 0)
            {
                return;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draw set of points all with same size.</para>
        /// </summary>
        /// <param name="points"></param>
        /// <param name="color"></param>
        /// <param name="size"></param>
        /// <param name="pointCount"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawPointSet(IReadOnlyList<Vector3> points, Color color, float size = 0.1f, int pointCount = -1, float lineThickness = k_LineThickness)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            int count = pointCount <= 0 ? points.Count : pointCount;
            var lines = 0;
            var offsetX = Vector3.right * (size * 0.5f);
            var offsetY = Vector3.up * (size * 0.5f);
            var offsetZ = Vector3.forward * (size * 0.5f);

            for (var i = 0; i < count; i++)
            {
                var center = points[i];

                var start = center - offsetX;
                var end = center + offsetX;

                TryGetLineMatrix(start, end, lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;

                start = center - offsetY;
                end = center + offsetY;

                TryGetLineMatrix(start, end, lineThickness, out matrix);
                s_Matrices[lines++] = matrix;

                start = center - offsetZ;
                end = center + offsetZ;

                TryGetLineMatrix(start, end, lineThickness, out matrix);
                s_Matrices[lines++] = matrix;

                if (lines + 3 >= k_MaxInstances)
                {
                    Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
                    lines = 0;
                }
            }

            if (lines == 0)
            {
                return;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        /// <summary>
        ///   <para>Draw set of cubes all with same size and rotation.</para>
        /// </summary>
        /// <param name="points"></param>
        /// <param name="rotation"></param>
        /// <param name="size"></param>
        /// <param name="color"></param>
        /// <param name="pointCount"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawWireCubes(IReadOnlyList<Vector3> points, Quaternion rotation, Vector3 size, Color color, int pointCount = -1, float lineThickness = k_LineThickness)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            int count = pointCount <= 0 ? points.Count : pointCount;
            var lines = 0;

            for (var p = 0; p < count; p++)
            {
                var center = points[p];

                var trs = Matrix4x4.TRS(center, rotation, size);

                for (var i = 0; i < 8; i++)
                {
                    k_TRSPoints[i] = trs.MultiplyPoint3x4(k_UnitCubePoints[i]);
                }

                for (var i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;

                    // "top" of the cube
                    TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[j], lineThickness, out var matrix);
                    s_Matrices[lines++] = matrix;

                    // "bottom" of the cube
                    TryGetLineMatrix(k_TRSPoints[i + 4], k_TRSPoints[j + 4], lineThickness, out matrix);
                    s_Matrices[lines++] = matrix;

                    // "sides" of the cube
                    TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[i + 4], lineThickness, out matrix);
                    s_Matrices[lines++] = matrix;
                }

                if (lines + 12 >= k_MaxInstances)
                {
                    Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
                    lines = 0;
                }
            }

            if (lines == 0)
            {
                return;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        private static bool TryGetLineMatrix(Vector3 from, Vector3 to, float thickness, out Matrix4x4 matrix)
        {
            var segment = to - from;
            float magnitude = segment.magnitude;

            if (magnitude == 0)
            {
                matrix = default;
                return false;
            }

            segment.Normalize();

            var rayPosition = from + segment * (magnitude * 0.5f);
            var rayRotation = Quaternion.LookRotation(segment);
            var rayScale = new Vector3(thickness, thickness, magnitude);

            matrix = Matrix4x4.TRS(rayPosition, rayRotation, rayScale);
            return true;
        }

        private static void BuildCircleData()
        {
            var vector = Vector3.forward;
            var rotation = Quaternion.AngleAxis(360.0f / k_CircleSegments, Vector3.up);

            for (var i = 0; i < k_CircleSegments; i++)
            {
                k_UnitCirclePoints[i] = vector;
                vector = rotation * vector;
            }

            var sphereRotations = new Quaternion[]
            {
                Quaternion.AngleAxis(360.0f / k_CircleSegments, Vector3.up),
                Quaternion.AngleAxis(360.0f / k_CircleSegments, -Vector3.right),
                Quaternion.AngleAxis(360.0f / k_CircleSegments, Vector3.forward),
            };

            var index = 0;
            for (var i = 0; i < 3; i++)
            {
                rotation = sphereRotations[i];
                vector = i > 1 ? Vector3.right : Vector3.forward;

                for (var j = 0; j < k_CircleSegments; j++)
                {
                    k_UnitSpherePoints[index++] = vector;
                    vector = rotation * vector;
                }
            }

            index = 0;
            for (var i = 0; i < 3; i++)
            {
                rotation = sphereRotations[i];
                vector = i > 1 ? Vector3.right : Vector3.forward;

                int count = i > 0 ? (k_CircleSegments / 2) + 1 : k_CircleSegments;

                for (var j = 0; j < count; j++)
                {
                    k_UnitHemiSpherePoints[index++] = vector;
                    vector = rotation * vector;
                }
            }
        }

        private static void OnApplicationQuitting()
        {
            Application.quitting -= OnApplicationQuitting;

            if (s_Matrices.IsCreated)
            {
                s_Matrices.Dispose();
            }
        }
    }
}
