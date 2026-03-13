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
using System.Diagnostics;
using System.Reflection;
using Debug = UnityEngine.Debug;

namespace Meta.XR.Editor.Reflection
{
    /// <summary>
    /// Base class for all reflection handles that provide lazy access to reflection members (fields, properties, methods).
    /// Uses assembly-capture at construction time to enable proper reflection lookups across different assemblies.
    /// </summary>
    /// <remarks>
    /// The Handle system solves the problem of accessing internal members across assembly boundaries in Unity Editor.
    /// It captures the creating assembly at construction time (via stack trace analysis), which allows the Registry
    /// to perform targeted reflection scanning rather than relying on problematic AppDomain enumeration.
    ///
    /// Key features:
    /// - Assembly capture at construction time for accurate reflection targeting
    /// - Lazy processing only when the handle is first accessed
    /// - Integration with ReflectionAttribute for declarative member access
    /// - Support for cross-assembly member access via InternalsVisibleToAttribute
    /// </remarks>
    internal abstract class Handle
    {
        /// <summary>
        /// Handle to the target type containing the reflection member
        /// </summary>
        public TypeHandle Type;

        /// <summary>
        /// Flag indicating whether this handle has been processed (reflection lookup performed)
        /// </summary>
        protected bool Processed;

        /// <summary>
        /// The ReflectionAttribute associated with this handle, retrieved from Registry
        /// </summary>
        protected ReflectionAttribute Attribute;

        /// <summary>
        /// The assembly that created this Handle instance, captured at construction time.
        /// This is critical for proper reflection targeting as it identifies which assembly
        /// contains the ReflectionAttribute declarations.
        /// </summary>
        /// <remarks>
        /// This assembly is captured via stack trace analysis during Handle construction,
        /// ensuring we know the exact assembly context even when the handle is processed
        /// later in a different call stack context.
        /// </remarks>
        public readonly Assembly CreatingAssembly;

        /// <summary>
        /// Gets the target member name from the associated ReflectionAttribute
        /// </summary>
        protected string TargetName => Attribute?.Name;

        /// <summary>
        /// Binding flags used for reflection lookups to find both public and internal members
        /// </summary>
        public const BindingFlags TargetFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic |
                                                BindingFlags.Public | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Gets a value indicating whether this handle points to a valid reflection target
        /// </summary>
        public abstract bool Valid { get; }

        /// <summary>
        /// Initializes a new Handle instance and captures the creating assembly from the call stack
        /// </summary>
        protected Handle()
        {
            // Capture the assembly that created this Handle instance from the call stack
            // This is essential for the Registry to know which assembly to scan for ReflectionAttributes
            CreatingAssembly = GetCreatingAssemblyFromStack();
        }

        /// <summary>
        /// Analyzes the call stack to determine which assembly created this Handle instance.
        /// This enables accurate reflection targeting by identifying the assembly that contains
        /// the ReflectionAttribute declarations.
        /// </summary>
        /// <returns>The assembly that instantiated this Handle</returns>
        /// <remarks>
        /// The method walks up the call stack, skipping Handle-related frames and internal reflection
        /// namespace frames to find the first "user" assembly frame. This assembly is then used by
        /// the Registry for targeted reflection scanning.
        ///
        /// For example, if Utils.cs creates a StaticMethodInfoHandle, this method will return
        /// the assembly containing Utils.cs, not the Handle/Registry assembly.
        /// </remarks>
        private static Assembly GetCreatingAssemblyFromStack()
        {
            var stackTrace = new StackTrace();
            var frames = stackTrace.GetFrames();

            // Walk up the call stack to find the first frame outside the Handle/Reflection system
            for (int i = 0; i < frames.Length; i++)
            {
                var method = frames[i].GetMethod();
                var declaringType = method?.DeclaringType;

                // Skip Handle hierarchy and internal reflection namespace frames
                if (declaringType != null &&
                    !typeof(Handle).IsAssignableFrom(declaringType) &&
                    declaringType.Namespace != "Meta.XR.Editor.Reflection")
                {
                    var creatingAssembly = declaringType.Assembly;
                    return creatingAssembly;
                }
            }

            // Fallback to calling assembly if stack trace analysis fails
            var callingAssembly = Assembly.GetCallingAssembly();
            return callingAssembly;
        }
    }

    /// <summary>
    /// Generic base class for strongly-typed reflection handles that provide lazy access to reflection members.
    /// </summary>
    /// <typeparam name="T">The type of reflection member this handle represents (PropertyInfo, FieldInfo, MethodInfo, etc.)</typeparam>
    internal abstract class Handle<T> : Handle
    {
        /// <summary>
        /// The cached reflection member instance, populated during processing
        /// </summary>
        protected T _target;

        /// <summary>
        /// Gets the target type from the associated TypeHandle
        /// </summary>
        protected Type TargetType => Type.Target;

        /// <summary>
        /// Gets a value indicating whether this handle points to a valid reflection target
        /// </summary>
        public override bool Valid => Target != null;

        /// <summary>
        /// Gets the reflection member, triggering lazy processing if not yet processed
        /// </summary>
        public T Target
        {
            get
            {
                // Lazy processing: only perform reflection lookup when first accessed
                if (!Processed) Process();

                return _target;
            }
        }

        /// <summary>
        /// Processes this handle by retrieving the associated ReflectionAttribute from Registry
        /// and performing the actual reflection lookup to populate the Target member.
        /// </summary>
        /// <remarks>
        /// This method is called lazily when the Target property is first accessed.
        /// It coordinates with the Registry using the captured CreatingAssembly to perform
        /// targeted reflection scanning rather than broad AppDomain enumeration.
        /// </remarks>
        protected virtual void Process()
        {
            Processed = true;

            // Request the Registry to find our ReflectionAttribute using our captured creating assembly
            Attribute ??= Registry.FindAttribute(this);


            if (Attribute == null) return;

            // Create TypeHandle for the target type
            Type = new TypeHandle(Attribute);


            if (!HasValidAttribute) return;

            // Perform the actual reflection lookup
            _target = Fetch();

        }

        /// <summary>
        /// Abstract method that derived classes must implement to perform the specific reflection lookup
        /// (e.g., GetProperty, GetField, GetMethod)
        /// </summary>
        /// <returns>The reflection member of type T</returns>
        protected abstract T Fetch();

        /// <summary>
        /// Gets a value indicating whether the ReflectionAttribute contains valid information
        /// </summary>
        protected virtual bool HasValidAttribute => !string.IsNullOrEmpty(TargetName) && TargetType != null;

        /// <summary>
        /// Builds a descriptive error message for debugging reflection issues
        /// </summary>
        /// <param name="errorMessage">The specific error that occurred</param>
        /// <returns>A formatted error message with handle and attribute context</returns>
        public string BuildError(string errorMessage)
            => $"This reflection usage {{{GetType().Name} {Attribute?.ToString()}}} is invalid : {errorMessage}.";
    }

    /// <summary>
    /// Handle for accessing PropertyInfo objects via reflection
    /// </summary>
    internal class PropertyInfoHandle : Handle<PropertyInfo>
    {
        /// <summary>
        /// Fetches the PropertyInfo using reflection based on the target type and name
        /// </summary>
        protected override PropertyInfo Fetch() => TargetType.GetProperty(TargetName, TargetFlags);
    }

    /// <summary>
    /// Strongly-typed handle for accessing properties with specific property types
    /// </summary>
    /// <typeparam name="TPropertyType">The type of the property value</typeparam>
    internal class PropertyInfoHandle<TPropertyType> : PropertyInfoHandle
    {
        /// <summary>
        /// Gets the property value from the specified owner object
        /// </summary>
        /// <param name="owner">The object instance to get the property value from</param>
        /// <returns>The property value cast to TPropertyType</returns>
        public TPropertyType Get(object owner) => (TPropertyType)(Target?.GetValue(owner));

        /// <summary>
        /// Sets the property value on the specified owner object
        /// </summary>
        /// <param name="owner">The object instance to set the property value on</param>
        /// <param name="value">The value to set</param>
        public void Set(object owner, TPropertyType value) => Target?.SetValue(owner, value);
    }

    /// <summary>
    /// Handle for accessing FieldInfo objects via reflection
    /// </summary>
    internal class FieldInfoHandle : Handle<FieldInfo>
    {
        /// <summary>
        /// Fetches the FieldInfo using reflection based on the target type and name
        /// </summary>
        protected override FieldInfo Fetch() => TargetType.GetField(TargetName, TargetFlags);
    }

    /// <summary>
    /// Strongly-typed handle for accessing fields with specific field types
    /// </summary>
    /// <typeparam name="TFieldType">The type of the field value</typeparam>
    internal class FieldInfoHandle<TFieldType> : FieldInfoHandle
    {
        /// <summary>
        /// Gets the field value from the specified owner object
        /// </summary>
        /// <param name="owner">The object instance to get the field value from</param>
        /// <returns>The field value cast to TFieldType</returns>
        public TFieldType Get(object owner) => (TFieldType)(Target?.GetValue(owner));

        /// <summary>
        /// Sets the field value on the specified owner object
        /// </summary>
        /// <param name="owner">The object instance to set the field value on</param>
        /// <param name="value">The value to set</param>
        public void Set(object owner, TFieldType value) => Target?.SetValue(owner, value);
    }

    /// <summary>
    /// Handle for accessing MethodInfo objects via reflection
    /// </summary>
    internal class MethodInfoHandle : Handle<MethodInfo>
    {
        /// <summary>
        /// Fetches the MethodInfo using reflection based on the target type and name
        /// </summary>
        protected override MethodInfo Fetch() => TargetType.GetMethod(TargetName, TargetFlags);
    }

    /// <summary>
    /// Handle for accessing static methods and creating strongly-typed delegates for efficient invocation
    /// </summary>
    /// <typeparam name="TDelegateType">The delegate type that matches the method signature</typeparam>
    internal class StaticMethodInfoHandle<TDelegateType> : MethodInfoHandle
        where TDelegateType : System.Delegate
    {
        /// <summary>
        /// The cached delegate instance for efficient method invocation
        /// </summary>
        private TDelegateType _staticDelegate;

        /// <summary>
        /// Gets the delegate for invoking the static method, triggering processing if necessary
        /// </summary>
        public TDelegateType Invoke
        {
            get
            {
                if (!Processed) Process();

                return _staticDelegate;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this handle is valid (both method exists and delegate created)
        /// </summary>
        public override bool Valid => base.Valid && Invoke != null;

        /// <summary>
        /// Creates a delegate from the MethodInfo for efficient invocation
        /// </summary>
        /// <returns>A delegate of type TDelegateType</returns>
        protected virtual TDelegateType CreateDelegate() =>
            (TDelegateType)Target?.CreateDelegate(typeof(TDelegateType));

        /// <summary>
        /// Processes the handle by calling base processing and then creating the delegate
        /// </summary>
        protected override void Process()
        {
            base.Process();

            if (Target == null) return;

            try
            {
                _staticDelegate = CreateDelegate();
            }
            catch (Exception)
            {
                // Silently handle delegate creation failures in release builds
            }
        }
    }

    /// <summary>
    /// Handle for static methods that return a value and take no parameters
    /// </summary>
    /// <typeparam name="TOut">The return type of the method</typeparam>
    internal class StaticMethodInfoHandleWithWrapper<TOut> : StaticMethodInfoHandle<Func<TOut>>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with no parameters
        /// </summary>
        protected override Func<TOut> CreateDelegate() => () => (TOut)Target?.Invoke(null, new object[] { });
    }

    /// <summary>
    /// Handle for static methods that return a value and take one parameter
    /// </summary>
    /// <typeparam name="T1">The type of the first parameter</typeparam>
    /// <typeparam name="TOut">The return type of the method</typeparam>
    internal class StaticMethodInfoHandleWithWrapper<T1, TOut> : StaticMethodInfoHandle<Func<T1, TOut>>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with one parameter
        /// </summary>
        protected override Func<T1, TOut> CreateDelegate() => (_in) => (TOut)Target?.Invoke(null, new object[] { _in });
    }

    /// <summary>
    /// Handle for static methods that return void and take one parameter
    /// </summary>
    /// <typeparam name="T1">The type of the first parameter</typeparam>
    internal class StaticMethodInfoHandleWithWrapperAction<T1> : StaticMethodInfoHandle<Action<T1>>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with one parameter
        /// </summary>
        protected override Action<T1> CreateDelegate() => (_in) => Target?.Invoke(null, new object[] { _in });
    }


    /// <summary>
    /// Handle for static methods that return void and take three parameters
    /// </summary>
    /// <typeparam name="T1">The type of the first parameter</typeparam>
    /// <typeparam name="T2">The type of the second parameter</typeparam>
    internal class StaticMethodInfoHandleWithWrapperAction<T1, T2> : StaticMethodInfoHandle<Action<T1, T2>>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with three parameters
        /// </summary>
        protected override Action<T1, T2> CreateDelegate() =>
            (T1, T2) => Target?.Invoke(null, new object[] { T1, T2 });
    }

    /// <summary>
    /// Handle for static methods that return void and take three parameters
    /// </summary>
    /// <typeparam name="T1">The type of the first parameter</typeparam>
    /// <typeparam name="T2">The type of the second parameter</typeparam>
    /// <typeparam name="T3">The type of the third parameter</typeparam>
    internal class StaticMethodInfoHandleWithWrapperAction<T1, T2, T3> : StaticMethodInfoHandle<Action<T1, T2, T3>>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with three parameters
        /// </summary>
        protected override Action<T1, T2, T3> CreateDelegate() =>
            (T1, T2, T3) => Target?.Invoke(null, new object[] { T1, T2, T3 });
    }

    /// <summary>
    /// Handle for static methods that return void and take no parameters
    /// </summary>
    internal class StaticMethodInfoHandleWithWrapperAction : StaticMethodInfoHandle<Action>
    {
        /// <summary>
        /// Creates a wrapper delegate that invokes the static method with no parameters
        /// </summary>
        protected override Action CreateDelegate() => () => Target?.Invoke(null, new object[] { });
    }
}
