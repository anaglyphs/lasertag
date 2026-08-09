using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Utilities.XR
{
    public static partial class XRGizmos
    {
        /// <summary>
        /// Each pair of digits describes a point i.e. "00" is the point (0, 0)
        /// Points are separated by a space for readability
        /// Strokes within a glyph are separated by ":"
        /// Font is defined on an 9x9 grid with x and y coords in the range [0,8]
        /// source: https://github.com/coolbutuseless/arcadefont/blob/master/data-raw/create-arcade-font.R
        /// </summary>
        private static readonly IReadOnlyDictionary<char, string> k_ArcadeFont = new Dictionary<char, string>
        {
            ['A'] = "00 06 48 86 80:03 83",
            ['B'] = "00 08 58 76 54 04:64 82 60 00",
            ['C'] = "88 08 00 80",
            ['D'] = "00 08 58 85 83 50 00",
            ['E'] = "88 08 00 80:04 64",
            ['F'] = "88 08 00:04 64",
            ['G'] = "88 08 00 80 83 43",
            ['H'] = "00 08:80 88:04 84",
            ['I'] = "00 80:08 88:40 48",
            ['J'] = "88 80 40 03",
            ['K'] = "00 08:88 04 80",
            ['L'] = "08 00 80",
            ['M'] = "00 08 45 88 80",
            ['N'] = "00 08 80 88",
            ['O'] = "00 80 88 08 00",
            ['P'] = "00 08 88 84 04",
            ['Q'] = "00 08 88 83 40 00:43 80",
            ['R'] = "00 08 88 84 04 80",
            ['S'] = "00 80 84 04 08 88",
            ['T'] = "08 88:40 48",
            ['U'] = "08 00 80 88",
            ['V'] = "08 40 88",
            ['W'] = "08 00 43 80 88",
            ['X'] = "00 88:08 80",
            ['Y'] = "08 45 88:45 40",
            ['Z'] = "08 88 00 80",
            ['0'] = "00 06 28 88 82 60 00",
            ['1'] = "00 80:40 48 26",
            ['2'] = "08 88 84 04 00 80",
            ['3'] = "08 88 80 00:04 84",
            ['4'] = "08 04 84:88 80",
            ['5'] = "00 80 84 04 08 88",
            ['6'] = "08 00 80 84 04",
            ['7'] = "08 88 86 54 50",
            ['8'] = "00 08 88 80 00:04 84",
            ['9'] = "80 88 08 04 84",
            ['.'] = "00 01 11 10 00",
            [','] = "01 02 12 11 01:11 00",
            ['-'] = "14 74",
            ['='] = "13 73:15 75",
            ['!'] = "00 01 11 10 00:03 08 18 13 03",
            ['?'] = "06 08 88 84 44 40",
            [':'] = "02 03 13 12 02:05 06 16 15 05",
            [';'] = "02 03 13 12 02:05 06 16 15 05:12 00",
            ['#'] = "03 83:05 85:30 38:50 58",
            ['\''] = "07 08 18 17 07:17 05",
            ['\"'] = "07 08 18 17 07:17 05:27 28 38 37 27:37 25",
            ['['] = "28 08 00 20",
            [']'] = "08 28 20 00",
            ['('] = "28 04 20",
            [')'] = "08 24 00",
            ['{'] = "28 04 20",
            ['}'] = "08 24 00",
            ['$'] = "01 81 84 04 07 87:40 48",
            ['+'] = "41 47:14 74",
            ['\\'] = "08 80",
            ['/'] = "00 88",
            ['*'] = "41 47:14 74:22 66:26 62",
            ['%'] = "00 88:18 28 27 17 18:70 71 61 60 70",
            ['^'] = "26 48 66",
            ['|'] = "40 48",
            ['_'] = "00 80",
            ['<'] = "87 04 81",
            ['>'] = "07 84 01",
            ['&'] = "80 47 58 67 21 30 60 82",
            ['@'] = "71 60 20 02 06 28 68 86 84 62 22 24 36 66 62",
        };

        private static readonly Dictionary<char, IReadOnlyList<Vector3>> k_VectorFont = new Dictionary<char, IReadOnlyList<Vector3>>(k_ArcadeFont.Count);
        private static int s_LongestChar = 0;

        private static readonly Vector2 k_CursorShift = new Vector2(1.15f, 1.15f);

        [RuntimeInitializeOnLoadMethod]
        [Conditional(k_XRGizmosDefine)]
        private static void InitializeText()
        {
            const char zero = '0';

            s_LongestChar = 0;
            k_VectorFont.Clear();

            foreach ((char key, string data) in k_ArcadeFont)
            {
                int length = data.Length;
                var vectors = new List<Vector3>();
                var count = 0;

                Vector3? previousPoint = null;

                for (int i = 0; i < length; i += 3)
                {
                    (char c1, char c2) = (data[i], data[i + 1]);
                    int x = c1 - zero;
                    int y = c2 - zero;

                    if (previousPoint.HasValue)
                    {
                        vectors.Add(previousPoint.Value);
                    }

                    var point = new Vector3(x / 8.0f, y / 8.0f, 0f);
                    vectors.Add(point);

                    // end of character or end of stroke
                    if (i + 2 >= length || data[i + 2] == ':')
                    {
                        count = 0;
                        previousPoint = null;
                    }
                    else if (count++ != 0)
                    {
                        previousPoint = point;
                    }
                }

                if (count > s_LongestChar)
                {
                    s_LongestChar = count;
                }

                k_VectorFont.Add(key, vectors);
            }

            // lowercase
            const char upperToLower = (char)('a' - 'A');

            for (char c = 'A'; c <= 'Z'; c++)
            {
                k_VectorFont.Add((char)(c + upperToLower), k_VectorFont[c]);
            }
        }

        [Conditional(k_XRGizmosDefine)]
        public static void DrawChar(char c, Vector3 bottomLeft, Quaternion rotation, Color color, float xScale = 0.1f, float yScale = 0.1f, float lineThickness = k_LineThickness)
        {
            if (!k_VectorFont.TryGetValue(c, out var points))
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);
            int count = points.Count;

            var trs = Matrix4x4.TRS(bottomLeft, rotation, new Vector3(xScale, yScale, 1.0f));

            for (var i = 0; i < count; i++)
            {
                k_TRSPoints[i] = trs.MultiplyPoint3x4(points[i]);
            }

            var lines = 0;

            for (var i = 0; i < count; i += 2)
            {
                TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[i + 1], lineThickness, out var matrix);
                s_Matrices[lines++] = matrix;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }

        [Conditional(k_XRGizmosDefine)]
        public static void DrawString(string s, Vector3 bottomLeft, Quaternion rotation, Color color, float xScale = 0.1f, float yScale = 0.1f, float lineThickness = k_LineThickness)
        {
            var cursor = bottomLeft;
            float xShift = xScale * k_CursorShift.x;
            float yShift = yScale * k_CursorShift.y;

            if (string.IsNullOrWhiteSpace(s))
            {
                return;
            }

            s_GizmoProperties.SetColor(k_ColorID, color);

            var lines = 0;

            var rotatedXShift = (rotation * Vector3.right) * xShift;
            var rotatedYShift = (rotation * Vector3.up) * yShift;

            var lineStart = bottomLeft;

            foreach (char c in s)
            {
                if (c == ' ')
                {
                    cursor += rotatedXShift;
                    continue;
                }

                if (c == '\n')
                {
                    lineStart -= rotatedYShift;
                    cursor = lineStart;
                }

                if (!k_VectorFont.TryGetValue(c, out var points))
                {
                    continue;
                }

                int count = points.Count;

                var trs = Matrix4x4.TRS(cursor, rotation, new Vector3(xScale, yScale, 1.0f));

                for (var i = 0; i < count; i++)
                {
                    k_TRSPoints[i] = trs.MultiplyPoint3x4(points[i]);
                }

                for (var i = 0; i < count; i += 2)
                {
                    TryGetLineMatrix(k_TRSPoints[i], k_TRSPoints[i + 1], lineThickness, out var matrix);
                    s_Matrices[lines++] = matrix;

                    if (lines + s_LongestChar >= k_MaxInstances)
                    {
                        Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
                        lines = 0;
                    }
                }

                cursor += rotatedXShift;
            }

            if (lines == 0)
            {
                return;
            }

            Graphics.RenderMeshInstanced(s_RenderParams, s_CubeMesh, 0, s_Matrices, lines);
        }
    }
}
