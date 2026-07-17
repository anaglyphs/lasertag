using Unity.Mathematics;
using Plane = Unity.Mathematics.Geometry.Plane;
namespace Meshia.MeshSimplification
{
    struct ErrorQuadric
    {
        public ErrorQuadric(Plane plane)
        {
            float4 normalAndDistance = plane.NormalAndDistance;
            m0 = normalAndDistance.x * normalAndDistance.x;
            m1 = normalAndDistance.x * normalAndDistance.y;
            m2 = normalAndDistance.x * normalAndDistance.z;
            m3 = normalAndDistance.x * normalAndDistance.w;

            m4 = normalAndDistance.y * normalAndDistance.y;
            m5 = normalAndDistance.y * normalAndDistance.z;
            m6 = normalAndDistance.y * normalAndDistance.w;

            m7 = normalAndDistance.z * normalAndDistance.z;
            m8 = normalAndDistance.z * normalAndDistance.w;

            m9 = normalAndDistance.w * normalAndDistance.w;

        }
        float m0;
        float m1;
        float m2;
        float m3;

        float m4;
        float m5;
        float m6;

        float m7;
        float m8;

        float m9;

        public static ErrorQuadric operator +(ErrorQuadric left, ErrorQuadric right) => new()
        {
            m0 = left.m0 + right.m0,
            m1 = left.m1 + right.m1,
            m2 = left.m2 + right.m2,
            m3 = left.m3 + right.m3,
            m4 = left.m4 + right.m4,
            m5 = left.m5 + right.m5,
            m6 = left.m6 + right.m6,
            m7 = left.m7 + right.m7,
            m8 = left.m8 + right.m8,
            m9 = left.m9 + right.m9
        };


        /// <summary>
        /// Determinant(0, 1, 2, 1, 4, 5, 2, 5, 7)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant1()
        {
            var det =
                m0 * m4 * m7 +
                m2 * m1 * m5 +
                m1 * m5 * m2 -
                m2 * m4 * m2 -
                m0 * m5 * m5 -
                m1 * m1 * m7;
            return det;
        }

        /// <summary>
        /// Determinant(1, 2, 3, 4, 5, 6, 5, 7, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant2()
        {
            var det =
                m1 * m5 * m8 +
                m3 * m4 * m7 +
                m2 * m6 * m5 -
                m3 * m5 * m5 -
                m1 * m6 * m7 -
                m2 * m4 * m8;
            return det;
        }

        /// <summary>
        /// Determinant(0, 2, 3, 1, 5, 6, 2, 7, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant3()
        {
            var det =
                m0 * m5 * m8 +
                m3 * m1 * m7 +
                m2 * m6 * m2 -
                m3 * m5 * m2 -
                m0 * m6 * m7 -
                m2 * m1 * m8;
            return det;
        }

        /// <summary>
        /// Determinant(0, 1, 3, 1, 4, 6, 2, 5, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant4()
        {
            var det =
                m0 * m4 * m8 +
                m3 * m1 * m5 +
                m1 * m6 * m2 -
                m3 * m4 * m2 -
                m0 * m6 * m5 -
                m1 * m1 * m8;
            return det;
        }

        public readonly float ComputeError(float3 position)
        {
            var x = position.x;
            var y = position.y;
            var z = position.z;

            return m0 * x * x
                + 2 * m1 * x * y
                + 2 * m2 * x * z
                + 2 * m3 * x
                + m4 * y * y
                + 2 * m5 * y * z
                + 2 * m6 * y
                + m7 * z * z
                + 2 * m8 * z + m9;
        }
    }
}

