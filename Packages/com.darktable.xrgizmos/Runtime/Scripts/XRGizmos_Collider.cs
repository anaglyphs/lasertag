using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Utilities.XR
{
    public static partial class XRGizmos
    {
        /// <summary>
        ///   <para>Draws collider based on type.</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCollider(Collider collider, Color color, float lineThickness = k_LineThickness)
        {
            switch (collider)
            {
                case BoxCollider boxCollider:
                    DrawCollider(boxCollider, color, lineThickness);
                    break;
                case SphereCollider sphereCollider:
                    DrawCollider(sphereCollider, color, lineThickness);
                    break;
                case MeshCollider meshCollider:
                    DrawCollider(meshCollider, color, lineThickness);
                    break;
                case CapsuleCollider capsuleCollider:
                    DrawCollider(capsuleCollider, color, lineThickness);
                    break;
                default:
                    DrawColliderBounds(collider, color, lineThickness);
                    break;
            }
        }

        /// <summary>
        ///   <para>Draws a wire frame box collider.</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCollider(BoxCollider collider, Color color, float lineThickness = k_LineThickness)
        {
            var transform = collider.transform;
            var scale = transform.lossyScale;
            var size = ComponentMultiply(collider.size, scale);
            var center = transform.TransformPoint(collider.center);

            DrawWireCube(center, transform.rotation, size, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a wire frame sphere collider.</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCollider(SphereCollider collider, Color color, float lineThickness = k_LineThickness)
        {
            var transform = collider.transform;
            var scale = transform.lossyScale;
            float largestScale = LargetComponent(scale);
            float radius = collider.radius;
            var center = transform.TransformPoint(collider.center);

            DrawWireSphere(center, transform.rotation, radius * largestScale, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a wire frame axis-aligned bounding box around mesh collider</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCollider(MeshCollider collider, Color color, float lineThickness = k_LineThickness)
        {
            DrawColliderBounds(collider, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a wire frame axis-aligned bounding box around a collider</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawColliderBounds(Collider collider, Color color, float lineThickness = k_LineThickness)
        {
            if (!collider.enabled)
            {
                return;
            }

            var bounds = collider.bounds;

            DrawWireCube(bounds.center, Quaternion.identity, bounds.size, color, lineThickness);
        }

        /// <summary>
        ///   <para>Draws a wire frame capsule collider</para>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="color"></param>
        /// <param name="lineThickness"></param>
        [Conditional(k_XRGizmosDefine)]
        public static void DrawCollider(CapsuleCollider collider, Color color, float lineThickness = k_LineThickness)
        {
            s_GizmoProperties.SetColor(k_ColorID, color);

            const int xDirection = 0;
            const int yDirection = 1;
            const int zDirection = 2;

            var transform = collider.transform;
            var scale = transform.lossyScale;
            float radius = collider.radius;
            var center = transform.TransformPoint(collider.center);
            float height = Mathf.Max(collider.height, 0);

            Quaternion direction;
            switch (collider.direction)
            {
                case xDirection:
                    height *= Mathf.Abs(scale.x);
                    radius *= Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    direction = Quaternion.FromToRotation(Vector3.up, Vector3.right);
                    break;
                case yDirection:
                default:
                    height *= Mathf.Abs(scale.y);
                    radius *= Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    direction = Quaternion.identity;
                    break;
                case zDirection:
                    height *= Mathf.Abs(scale.z);
                    radius *= Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                    direction = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                    break;
            }

            DrawWireCapsule(center, transform.rotation * direction, radius, height, color, lineThickness);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LargetComponent(Vector3 v)
        {
            return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComponentMultiply(Vector3 a, Vector3 b)
        {
            a.Set(a.x * b.x, a.y * b.y, a.z * b.z);
            return a;
        }
    }
}
