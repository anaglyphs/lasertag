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
    public readonly struct XrTime : IEquatable<XrTime>, IComparable<XrTime>, IComparable
    {
        readonly long _value;

        public static XrTime FromSeconds(double seconds) => new((long)(seconds * 1e9));

        /// <summary>
        /// This <see cref="XrTime"/> represented as a number of seconds.
        /// </summary>
        /// <returns>Returns the number of seconds since the epoch used by this <see cref="XrTime"/>.</returns>
        public double ToSeconds() => _value * 1e-9;

        public XrTime(long value) => _value = value;

        /// <summary>
        /// The raw value for this <see cref="XrTime"/>.
        /// </summary>
        /// <remarks>
        /// This represents the number of nanoseconds since an epoch used by the OpenXR runtime.
        /// </remarks>
        public long Value => _value;

        public bool Equals(XrTime other) => _value == other._value;

        public override bool Equals(object obj) => obj is XrTime other && Equals(other);

        public override int GetHashCode() => _value.GetHashCode();

        public override string ToString() => _value.ToString();

        public int CompareTo(object obj) => obj switch
        {
            XrTime other => CompareTo(other),
            null => 1,
            _ => throw new ArgumentException($"Argument must be an {nameof(XrTime)}.", nameof(obj)),
        };

        public int CompareTo(XrTime other) => _value.CompareTo(other._value);

        public static bool operator >(XrTime left, XrTime right) => left._value > right._value;

        public static bool operator >=(XrTime left, XrTime right) => left._value >= right._value;

        public static bool operator ==(XrTime left, XrTime right) => left._value == right._value;

        public static bool operator !=(XrTime left, XrTime right) => left._value != right._value;

        public static bool operator <=(XrTime left, XrTime right) => left._value <= right._value;

        public static bool operator <(XrTime left, XrTime right) => left._value < right._value;

        public static XrTime operator +(XrTime left, XrDuration right) => new(left._value + right.Nanoseconds);

        public static XrTime operator -(XrTime left, XrDuration right) => new(left._value - right.Nanoseconds);

        public static XrDuration operator -(XrTime left, XrTime right) => new(left._value - right._value);

        /// <summary>
        /// The predicted display time.
        /// </summary>
        /// <remarks>
        /// The predicted display time is the time at which the runtime predicts the current frame will be displayed to
        /// the user. This is often used as a parameter to functions that provide a pose or other data at a given time.
        /// Often, rather than use the current time, you should specify the predicted display time.
        /// </remarks>
        public static XrTime PredictedDisplayTime => FromSeconds(OVRPlugin.GetPredictedDisplayTime());
    }
}
