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
    /// <summary>
    /// Represents a time duration.
    /// </summary>
    /// <remarks>
    /// This is used by many OpenXR functions that require the caller to specify a time duration. An
    /// <see cref="XrDuration"/> has nanosecond precision.
    /// </remarks>
    /// <seealso cref="XrTime"/>
    public readonly struct XrDuration : IEquatable<XrDuration>, IComparable<XrDuration>, IComparable
    {
        readonly long _value;

        /// <summary>
        /// No time duration.
        /// </summary>
        public static readonly XrDuration Zero = new(0);

        /// <summary>
        /// An infinite time duration.
        /// </summary>
        public static readonly XrDuration Infinity = new(0x7fffffffffffffffL);

        /// <summary>
        /// The raw value of the <see cref="XrDuration"/>, represented as a number of nanoseconds.
        /// </summary>
        public long Nanoseconds => _value;

        /// <summary>
        /// Converts seconds to a <see cref="XrDuration"/>.
        /// </summary>
        /// <param name="seconds">The number of seconds to convert to an <see cref="XrDuration"/></param>
        /// <returns>Returns a <see cref="XrDuration"/> that represents <paramref name="seconds"/>.</returns>
        public static XrDuration FromSeconds(double seconds) => new((long)(seconds * 1e9));

        /// <summary>
        /// Converts this <see cref="XrDuration"/> to seconds.
        /// </summary>
        /// <returns>Returns the number of seconds represented by this <see cref="XrDuration"/>.</returns>
        public double ToSeconds() => _value * 1e-9;

        /// <summary>
        /// Constructs a new <see cref="XrDuration"/> from a number of nanoseconds.
        /// </summary>
        /// <param name="nanoseconds">The number of nanoseconds the <see cref="XrDuration"/> should represent.</param>
        public static XrDuration FromNanoseconds(long nanoseconds) => new(nanoseconds);

        internal XrDuration(long value) => _value = value;

        /// <summary>
        /// Constructs an <see cref="XrDuration"/> from a <see cref="TimeSpan"/>.
        /// </summary>
        /// <param name="value">The <see cref="TimeSpan"/> to convert to a <see cref="XrDuration"/>.</param>
        public XrDuration(TimeSpan value) => _value = value.Ticks * 100;

        /// <summary>
        /// Converts this <see cref="XrDuration"/> to a <see cref="TimeSpan"/>.
        /// </summary>
        /// <remarks>
        /// A <see cref="TimeSpan"/> does not have the same level of precision as an <see cref="XrDuration"/>.
        /// A <see cref="TimeSpan"/> represents time as a number of "ticks" where each tick is 100
        /// nanoseconds. However, a <see cref="XrDuration"/> has nanosecond precision, so converting from an
        /// <see cref="XrDuration"/> to a <see cref="TimeSpan"/> may lose precision.
        /// </remarks>
        /// <returns>Returns this <see cref="XrDuration"/> as a <see cref="TimeSpan"/>.</returns>
        public TimeSpan ToTimeSpan() => new(_value / 100);

        /// <summary>
        /// Tests two <see cref="XrDuration"/> instances for equality.
        /// </summary>
        /// <param name="other">The <see cref="XrDuration"/> to compare with this one.</param>
        /// <returns>Returns true if both <paramref name="other"/> is equal to this one, otherwise false.</returns>
        public bool Equals(XrDuration other) => _value == other._value;

        /// <summary>
        /// Tests an `object` for equality with this <see cref="XrDuration"/>.
        /// </summary>
        /// <param name="obj">The `object` to test.</param>
        /// <returns>Returns true if <paramref name="obj"/> is an <see cref="XrDuration"/> or <see cref="TimeSpan"/>, and its value is equal to this <see cref="XrDuration"/>.</returns>
        public override bool Equals(object obj) => obj switch
        {
            XrDuration other => Equals(other),
            TimeSpan other => Equals(new XrDuration(other)),
            _ => false,
        };

        /// <summary>
        /// Computes a hash code of this <see cref="XrDuration"/>.
        /// </summary>
        /// <returns>Returns a hash code of this <see cref="XrDuration"/>.</returns>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>
        /// Compares an `object` with this <see cref="XrDuration"/> instance.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>Returns a positive value if <paramref name="obj"/> is an <see cref="XrDuration"/>,
        /// <see cref="TimeSpan"/>, or `null` and is less than this instance, otherwise returns a negative number.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="obj"/> is not an <see cref="XrDuration"/>,
        /// a <see cref="TimeSpan"/>, or `null`.</exception>
        public int CompareTo(object obj) => obj switch
        {
            XrDuration other => CompareTo(other),
            TimeSpan other => CompareTo(new XrDuration(other)),
            null => 1, // null is "less than" any instance
            _ => throw new ArgumentException(message: $"Argument must be an {nameof(XrDuration)} or {nameof(TimeSpan)}", nameof(obj))
        };

        public int CompareTo(XrDuration other) => _value.CompareTo(other._value);

        public static explicit operator TimeSpan(XrDuration value) => value.ToTimeSpan();

        public static explicit operator XrDuration(TimeSpan value) => new(value);

        public static bool operator >(XrDuration left, XrDuration right) => left._value > right._value;

        public static bool operator >=(XrDuration left, XrDuration right) => left._value >= right._value;

        public static bool operator ==(XrDuration left, XrDuration right) => left._value == right._value;

        public static bool operator !=(XrDuration left, XrDuration right) => left._value != right._value;

        public static bool operator <=(XrDuration left, XrDuration right) => left._value <= right._value;

        public static bool operator <(XrDuration left, XrDuration right) => left._value < right._value;

        public static XrDuration operator +(XrDuration left, XrDuration right) => new(left._value + right._value);

        public static XrDuration operator -(XrDuration left, XrDuration right) => new(left._value - right._value);

        public static XrDuration operator *(XrDuration left, long right) => new(left._value * right);

        public static XrDuration operator /(XrDuration left, long right) => new(left._value / right);
    }
}
