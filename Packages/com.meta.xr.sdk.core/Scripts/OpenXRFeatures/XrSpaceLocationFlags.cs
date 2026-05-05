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

namespace Meta.XR
{
    [Flags]
    public enum XrSpaceLocationFlags : ulong
    {
        OrientationValid = 0x00000001,
        PositionValid = 0x00000002,
        OrientationTracked = 0x00000004,
        PositionTracked = 0x00000008,
    }

    partial class Extensions
    {
        public static bool IsOrientationValid(this XrSpaceLocationFlags value)
            => (value & XrSpaceLocationFlags.OrientationValid) != 0;

        public static bool IsPositionValid(this XrSpaceLocationFlags value)
            => (value & XrSpaceLocationFlags.PositionValid) != 0;

        public static bool IsOrientationTracked(this XrSpaceLocationFlags value)
            => (value & XrSpaceLocationFlags.OrientationTracked) != 0;

        public static bool IsPositionTracked(this XrSpaceLocationFlags value)
            => (value & XrSpaceLocationFlags.PositionTracked) != 0;

        public static bool AreAllBitsSet(this XrSpaceLocationFlags value) => ((uint)value & 0xf) == 0xf;
    }
}
