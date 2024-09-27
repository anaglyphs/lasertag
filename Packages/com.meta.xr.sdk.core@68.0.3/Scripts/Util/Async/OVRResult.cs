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
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// Represents an attribute for marking enums to indicate their association with internal results.
/// </summary>
/// <remarks>
/// This attribute is used internally and is necessary to enforce conversions from internal results to the enum this attribute is applied to.
/// </remarks>
[AttributeUsage(AttributeTargets.Enum)]
internal class OVRResultStatus : Attribute { }

static class OVRResult
{
    public static OVRResult<TStatus> From<TStatus>(TStatus status) where TStatus : struct, Enum, IConvertible
        => OVRResult<TStatus>.From(status);

    public static OVRResult<TResult, TStatus> From<TResult, TStatus>(TResult result, TStatus status)
        where TStatus : struct, Enum, IConvertible
        => OVRResult<TResult, TStatus>.From(result, status);
}

/// <summary>
/// Represents a result with a status code of type <typeparamref name="TStatus"/>.
/// </summary>
/// <typeparam name="TStatus">The type of the status code. It must have the OVRResultStatus attribute</typeparam>
/// <remarks>
/// The <see cref="OVRResult{TStatus}"/> struct provides a wrapper around a status code and exposes properties for success and status retrieval.
/// </remarks>
public struct OVRResult<TStatus> : IEquatable<OVRResult<TStatus>>
    where TStatus : struct, Enum, IConvertible
{
    // Please note that there is an assumption that "TStatus" maps with a subset of "OVRPlugin.Result"
    // including "OVRPlugin.Result.Success", "OVRPlugin.Result.Failure", "OVRPlugin.Result.Failure_DataIsInvalid".
    // This is guaranteed and unit tested with the usage of the attribute [OVRResultStatus] over the enum TStatus.
    private readonly bool _initialized;
    private readonly int _statusCode;
    private readonly TStatus _status;

    /// <summary>
    /// Gets a bool indicating whether the result is a success.
    /// </summary>
    public bool Success => _initialized && ((OVRPlugin.Result)_statusCode).IsSuccess();

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public TStatus Status
    {
        get
        {
            if (_initialized)
            {
                return _status;
            }

            var invalid = OVRPlugin.Result.Failure_DataIsInvalid;
            return UnsafeUtility.As<OVRPlugin.Result, TStatus>(ref invalid);
        }
    }

    private OVRResult(TStatus status)
    {
        if (UnsafeUtility.SizeOf<TStatus>() != sizeof(int))
            throw new InvalidOperationException($"{nameof(TStatus)} must have a 4 byte underlying storage type.");

        _initialized = true;
        _status = status;
        _statusCode = UnsafeUtility.EnumToInt(_status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified status.</returns>
    /// <seealso cref="FromSuccess"/>
    /// <seealso cref="FromFailure"/>
    public static OVRResult<TStatus> From(TStatus status)
    {
        return new OVRResult<TStatus>(status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified success status.
    /// </summary>
    /// <param name="status">The status (must be a valid success status).</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified status.</returns>
    /// <exception cref="ArgumentException">Thrown when status is not a valid success status.</exception>
    /// <seealso cref="From"/>
    /// <seealso cref="FromFailure"/>
    public static OVRResult<TStatus> FromSuccess(TStatus status)
    {
        var result = UnsafeUtility.As<TStatus, OVRPlugin.Result>(ref status);
        if (!result.IsSuccess())
            throw new ArgumentException("Not of a valid success status", nameof(status));

        return new OVRResult<TStatus>(status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified failure status.
    /// </summary>
    /// <param name="status">The status (must be a valid failure status).</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified status.</returns>
    /// <exception cref="ArgumentException">Thrown when status is not a valid failure status.</exception>
    /// <seealso cref="From"/>
    /// <seealso cref="FromSuccess"/>
    public static OVRResult<TStatus> FromFailure(TStatus status)
    {
        var result = UnsafeUtility.As<TStatus, OVRPlugin.Result>(ref status);
        if (result.IsSuccess())
            throw new ArgumentException("Not of a valid failure status", nameof(status));

        return new OVRResult<TStatus>(status);
    }

    /// <summary>
    /// Determines whether the current <see cref="OVRResult{TStatus}"/> is equal to another <see cref="OVRResult{TStatus}"/>.
    /// </summary>
    /// <param name="other">The <see cref="OVRResult{TStatus}"/> to compare with the current one.</param>
    /// <returns>
    /// <c>true</c> if the specified <see cref="OVRResult{TStatus}"/> is equal to the current <see cref="OVRResult{TStatus}"/>; otherwise, <c>false</c>.
    /// </returns>
    public bool Equals(OVRResult<TStatus> other) => _initialized == other._initialized && _statusCode == other._statusCode;

    /// <summary>
    /// Determines whether the current <see cref="OVRResult{TStatus}"/> is equal to another <see cref="object"/>.
    /// </summary>
    /// <param name="obj">The <see cref="object"/> to compare with the current <see cref="OVRResult{TStatus}"/>.</param>
    /// <returns>
    /// <c>true</c> if the specified <see cref="object"/> is equal to the current <see cref="OVRResult{TStatus}"/>; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object obj) => obj is OVRResult<TStatus> other && Equals(other);

    /// <summary>
    /// Gets a hashcode suitable for use in a Dictionary or HashSet.
    /// </summary>
    /// <returns>A hashcode for this result.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            const int primeBase = 17; // Starting prime number
            const int primeMultiplier = 31; // Multiplier prime number

            var hash = primeBase;
            hash = hash * primeMultiplier + _initialized.GetHashCode();
            hash = hash * primeMultiplier + _statusCode.GetHashCode();

            return hash;
        }
    }

    /// <summary>
    /// Generates a string representation of this result object.
    /// </summary>
    /// <remarks>
    /// The string representation is the stringification of <see cref="Status"/>, or "(invalid result)" if this result
    /// object has not been initialized.
    /// </remarks>
    /// <returns>A string representation of this <see cref="OVRResult{TStatus}"/></returns>
    public override string ToString() => _initialized ? _status.ToString() : "(invalid result)";
}

/// <summary>
/// Represents a result with a value of type <typeparamref name="TValue"/> and a status code of type <typeparamref name="TStatus"/>.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
/// <typeparam name="TStatus">The type of the status code.</typeparam>
/// <remarks>
/// An <see cref="OVRResult{TValue,TStatus}"/> represents the result of an operation which may fail. If the operation
/// succeeds (<see cref="Success"/> is `True`), then you can access the value using the <see cref="Value"/> property.
///
/// If it fails (<see cref="Success"/> is `False`), then the `OVRResult` does not have a value, and it is an error to
/// access the <see cref="Value"/> property. In this case, the <see cref="Status"/> property will contain an error code.
/// </remarks>
public struct OVRResult<TValue, TStatus> : IEquatable<OVRResult<TValue, TStatus>>
    where TStatus : struct, Enum, IConvertible
{
    // Please note that there is an assumption that "TStatus" maps with a subset of "OVRPlugin.Result"
    // including "OVRPlugin.Result.Success", "OVRPlugin.Result.Failure", "OVRPlugin.Result.Failure_DataIsInvalid".
    private readonly bool _initialized;
    private readonly TValue _value;
    private readonly int _statusCode;
    private readonly TStatus _status;

    /// <summary>
    /// Gets a bool indicating whether the result is a success.
    /// </summary>
    public bool Success => _initialized && ((OVRPlugin.Result)_statusCode).IsSuccess();

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public TStatus Status
    {
        get
        {
            if (_initialized)
            {
                return _status;
            }

            var invalid = OVRPlugin.Result.Failure_DataIsInvalid;
            return UnsafeUtility.As<OVRPlugin.Result, TStatus>(ref invalid);
        }
    }

    /// <summary>
    /// Gets a bool indicating whether the result has a value.
    /// </summary>
    public bool HasValue => Success;

    /// <summary>
    /// The value of the result.
    /// </summary>
    /// <remarks>
    /// It is an error to access this property unless <see cref="Success"/> is `True`. See also
    /// <see cref="TryGetValue"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Success"/> is `False`.</exception>
    /// <seealso cref="TryGetValue"/>
    /// <seealso cref="HasValue"/>
    public TValue Value
    {
        get
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"The {nameof(OVRResult)} object is not valid.");
            }

            if (_statusCode < 0)
            {
                throw new InvalidOperationException($"The {nameof(OVRResult)} does not have a value because the " +
                                                    $"operation failed with {_status}.");
            }

            return _value;
        }
    }

    /// <summary>
    /// Tries to retrieve the value of the result.
    /// </summary>
    /// <param name="value">When this method returns, contains the value of the result if the result was successful; otherwise, the default value.</param>
    /// <returns><c>true</c> if the result was successful and the value is retrieved; otherwise, <c>false</c>.</returns>
    /// <seealso cref="Value"/>
    /// <seealso cref="HasValue"/>
    public bool TryGetValue(out TValue value)
    {
        if (HasValue)
        {
            value = _value;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    private OVRResult(TValue value, TStatus status)
    {
        if (UnsafeUtility.SizeOf<TStatus>() != sizeof(int))
            throw new InvalidOperationException($"{nameof(TStatus)} must have a 4 byte underlying storage type.");

        _initialized = true;
        _value = value;
        _status = status;
        _statusCode = UnsafeUtility.EnumToInt(_status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified value and status.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="status">The status.</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified value and status.</returns>
    /// <seealso cref="FromSuccess"/>
    /// <seealso cref="FromFailure"/>
    public static OVRResult<TValue, TStatus> From(TValue value, TStatus status)
    {
        return new OVRResult<TValue, TStatus>(value, status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified value and a success status.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="status">The status (must be a valid success status).</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified value and status.</returns>
    /// <exception cref="ArgumentException">Thrown when status is not a valid success status.</exception>
    /// <seealso cref="From"/>
    /// <seealso cref="FromFailure"/>
    public static OVRResult<TValue, TStatus> FromSuccess(TValue value, TStatus status)
    {
        var result = UnsafeUtility.As<TStatus, OVRPlugin.Result>(ref status);
        if (!result.IsSuccess())
            throw new ArgumentException("Not of a valid success status", nameof(status));

        return new OVRResult<TValue, TStatus>(value, status);
    }

    /// <summary>
    /// Creates a new <see cref="OVRResult{TValue, TStatus}"/> with the specified failure status.
    /// </summary>
    /// <param name="status">The status (must be a valid failure status).</param>
    /// <returns>A new <see cref="OVRResult{TValue, TStatus}"/> with the specified status.</returns>
    /// <exception cref="ArgumentException">Thrown when status is not a valid failure status.</exception>
    /// <seealso cref="From"/>
    /// <seealso cref="FromSuccess"/>
    public static OVRResult<TValue, TStatus> FromFailure(TStatus status)
    {
        var result = UnsafeUtility.As<TStatus, OVRPlugin.Result>(ref status);
        if (result.IsSuccess())
            throw new ArgumentException("Not of a valid failure status", nameof(status));

        return new OVRResult<TValue, TStatus>(default, status);
    }

    /// <summary>
    /// Determines whether the current <see cref="OVRResult{TValue, TStatus}"/> is equal to another <see cref="OVRResult{TValue, TStatus}"/>.
    /// </summary>
    /// <param name="other">The <see cref="OVRResult{TValue, TStatus}"/> to compare with the current one.</param>
    /// <returns>
    /// <c>true</c> if the specified <see cref="OVRResult{TValue, TStatus}"/> is equal to the current <see cref="OVRResult{TValue, TStatus}"/>; otherwise, <c>false</c>.
    /// </returns>
    public bool Equals(OVRResult<TValue, TStatus> other) => _initialized == other._initialized
        && EqualityComparer<TValue>.Default.Equals(_value, other._value) && _statusCode == other._statusCode;

    /// <summary>
    /// Determines whether the current <see cref="OVRResult{TValue, TStatus}"/> is equal to another <see cref="object"/>.
    /// </summary>
    /// <param name="obj">The <see cref="object"/> to compare with the current <see cref="OVRResult{TValue, TStatus}"/>.</param>
    /// <returns>
    /// <c>true</c> if the specified <see cref="object"/> is equal to the current <see cref="OVRResult{TValue, TStatus}"/>; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object obj) => obj is OVRResult<TValue, TStatus> other && Equals(other);

    /// <summary>
    /// Gets a hashcode suitable for use in a Dictionary or HashSet.
    /// </summary>
    /// <returns>A hashcode for this result.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            const int primeBase = 17; // Starting prime number
            const int primeMultiplier = 31; // Multiplier prime number

            var hash = primeBase;
            hash = hash * primeMultiplier + _initialized.GetHashCode();
            hash = hash * primeMultiplier + _statusCode.GetHashCode();
            hash = hash * primeMultiplier + (_value?.GetHashCode() ?? 0);

            return hash;
        }
    }

    /// <summary>
    /// Generates a string representation of this result object.
    /// </summary>
    /// <remarks>
    /// If this result object has not been initialized, the string is "(invalid result)". Otherwise, if
    /// <see cref="Success"/> is `True`, then the string is the stringification of the <see cref="Status"/> and
    /// <see cref="Value"/>. If <see cref="Success"/> is `False`, then it is just the stringification of
    /// <see cref="Status"/>.
    /// </remarks>
    /// <returns>A string representation of this <see cref="OVRResult{TStatus}"/></returns>
    public override string ToString() => _initialized
        ? HasValue
            ? $"(Value={_value}, Status={_status})"
            : _status.ToString()
        : "(invalid result)";
}
