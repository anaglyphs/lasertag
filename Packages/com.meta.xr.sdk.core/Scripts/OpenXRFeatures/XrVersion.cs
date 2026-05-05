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
    public readonly struct XrVersion : IEquatable<XrVersion>, IComparable<XrVersion>, IComparable
    {
        readonly ulong _value;

        public XrVersion(ulong value) => _value = value;

        public XrVersion(ushort major, ushort minor, uint patch) : this(
            ((major & 0xfffful) << 48) |
            ((minor & 0xfffful) << 32) |
            (patch & 0xfffffffful))
        { }

        public XrVersion(Version version) : this((ushort)version.Major, (ushort)version.Minor, (uint)version.Build) { }

        public ushort Major => (ushort)((_value >> 48) & 0xfffful);

        public ushort Minor => (ushort)((_value >> 32) & 0xfffful);

        public uint Patch => (uint)(_value & 0xfffffffful);

        public bool Equals(XrVersion other) => _value == other._value;

        public override bool Equals(object obj) => obj is XrVersion other && Equals(other);

        public override int GetHashCode() => _value.GetHashCode();

        public int CompareTo(XrVersion other) => _value.CompareTo(other._value);

        public int CompareTo(object obj) => obj is XrVersion other ? CompareTo(other) : _value.CompareTo(obj);

        public Version ToVersion() => new(Major, Minor, (int)Patch);

        public override string ToString() => ToVersion().ToString();

        public static explicit operator Version(XrVersion version) => version.ToVersion();

        public static explicit operator XrVersion(Version version) => new(version);

        public static bool operator >(XrVersion left, XrVersion right) => left._value > right._value;

        public static bool operator >=(XrVersion left, XrVersion right) => left._value >= right._value;

        public static bool operator ==(XrVersion left, XrVersion right) => left._value == right._value;

        public static bool operator !=(XrVersion left, XrVersion right) => left._value != right._value;

        public static bool operator <=(XrVersion left, XrVersion right) => left._value <= right._value;

        public static bool operator <(XrVersion left, XrVersion right) => left._value < right._value;
    }
}
