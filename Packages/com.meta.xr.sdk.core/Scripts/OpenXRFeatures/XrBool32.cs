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
    public readonly struct XrBool32 : IEquatable<XrBool32>, IEquatable<bool>
    {
        readonly uint _value;

        public XrBool32(bool value) => _value = value ? 1u : 0;

        public bool Equals(bool other) => (bool)this == other;

        public bool Equals(XrBool32 other) => Equals((bool)other);

        public override bool Equals(object obj) => obj switch
        {
            XrBool32 value => Equals(value),
            bool value => Equals(value),
            _ => false,
        };

        public override int GetHashCode() => ((bool)this).GetHashCode();

        public override string ToString() => ((bool)this).ToString();

        public static bool operator ==(XrBool32 left, XrBool32 right) => left.Equals(right);

        public static bool operator !=(XrBool32 left, XrBool32 right) => !left.Equals(right);

        public static implicit operator bool(XrBool32 value) => value._value != 0;

        public static implicit operator XrBool32(bool value) => new(value);

        public static implicit operator XrBool32(OVRPlugin.Bool value) => new(value == OVRPlugin.Bool.True);

        public static implicit operator OVRPlugin.Bool(XrBool32 value) => (bool)value ? OVRPlugin.Bool.True : OVRPlugin.Bool.False;
    }
}
