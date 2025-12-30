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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using UnityEditor;
using static Meta.XR.BuildingBlocks.Editor.VariantAttribute;
using static Meta.XR.Editor.UserInterface.Styles;

namespace Meta.XR.BuildingBlocks.Editor
{
    internal abstract class VariantHandle
    {
        protected const BindingFlags BindingFLags = BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public MemberInfo MemberInfo { get; }
        public VariantAttribute Attribute { get; }
        public InstallationRoutine Owner { get; }

        protected abstract Type Type { get; }
        public abstract object RawValue { get; set; }

        private Func<bool> _condition;
        public static readonly Func<bool> DefaultCondition = () => true;
        public Func<bool> OverrideCondition = null;
        public Func<bool> Condition => OverrideCondition ?? (_condition ??= FetchConditionDelegate());

        protected VariantHandle(MemberInfo memberInfo, VariantAttribute attribute, InstallationRoutine owner)
        {
            MemberInfo = memberInfo;
            Attribute = attribute;
            Owner = owner;
        }

        private Func<bool> FetchConditionDelegate()
        {
            var conditionMethodName = Attribute.Condition;
            var conditionMethod = string.IsNullOrEmpty(conditionMethodName) ? null :
                Owner.GetType().GetMethod(conditionMethodName, VariantHandle.BindingFLags);
            return conditionMethod?.CreateDelegate(typeof(Func<bool>), Owner) as Func<bool> ?? DefaultCondition;
        }

        public static VariantHandle CreateFromRoutine(MemberInfo member, VariantAttribute attribute, InstallationRoutine owner)
        {
            var valueType = GetType(member);
            if (valueType == null) return null;

            var variantType = typeof(VariantForRoutine<>).MakeGenericType(valueType);
            var creationMethod = variantType.GetMethod("FromOwner", BindingFLags);
            return creationMethod?.Invoke(null, new object[] { member, attribute, owner }) as VariantHandle;
        }

        private static Type GetType(MemberInfo memberInfo)
        {
            switch (memberInfo?.MemberType)
            {
                case MemberTypes.Field:
                {
                    var field = memberInfo as FieldInfo;
                    return field?.FieldType;
                }

                case MemberTypes.Property:
                {
                    var property = memberInfo as PropertyInfo;
                    return property?.PropertyType;
                }
            }

            return null;
        }

        public bool Matches(VariantHandle variantHandle)
            => variantHandle.MemberInfo.Name == MemberInfo.Name
               && variantHandle.Attribute.Group == Attribute.Group
               && variantHandle.Attribute.Behavior == Attribute.Behavior
               && GetType(variantHandle.MemberInfo) == GetType(MemberInfo);

        public bool Fits(VariantHandle variant)
            => Matches(variant)
               && (!Condition() || !variant.Condition() || // if the condition doesn't meet, we don't evaluate they match or not.
                   variant.Attribute.Behavior == VariantBehavior.Parameter ||
                   Equals(variant.RawValue, RawValue));

        public bool Matches(IEnumerable<VariantHandle> variants) => variants.Any(Matches);

        public abstract string ToJson();
        public abstract void FromJson(string json);
        public abstract VariantHandle ToSelection(bool forceValue = true);
        public abstract void DrawGUI(SerializedObject serializedObject, out bool changed);

        internal static IReadOnlyList<VariantHandle> FetchVariants(InstallationRoutine routine, VariantBehavior behavior)
        {
            var variants = new List<VariantHandle>();
            foreach (var member in routine.GetType().GetMembers(VariantHandle.BindingFLags))
            {
                var attribute = member.GetCustomAttribute<VariantAttribute>();
                if (attribute == null || attribute.Behavior != behavior) continue;

                var variantHandle = VariantHandle.CreateFromRoutine(member, attribute, routine);
                if (variantHandle == null) continue;

                variants.Add(variantHandle);
            }

            // Sort variants by Order property first, then by member name alphabetically
            variants.Sort((a, b) =>
            {
                var orderComparison = a.Attribute.Order.CompareTo(b.Attribute.Order);
                return orderComparison != 0 ? orderComparison : string.Compare(a.MemberInfo.Name, b.MemberInfo.Name, StringComparison.Ordinal);
            });

            return variants;
        }

        internal bool NeedsChoice(VariantsSelection selection, out bool variantChanged)
        {
            variantChanged = false;
            if (Attribute.Behavior != VariantBehavior.Definition)
                return true;

            // Maybe the PossibleRoutines have only one choice for this variant
            foreach (var (block, routines) in selection.PossibleRoutines)

            {
                var fittingRoutines = routines.Where(routine => Matches(routine.DefinitionVariants)).ToList();
                if (fittingRoutines.Count != 1) continue;

                var mandatoryValue = fittingRoutines[0].DefinitionVariants.FirstOrDefault(this.Matches)?.RawValue;
                if (RawValue == mandatoryValue) return false;

                RawValue = mandatoryValue;
                variantChanged = true;
                return false;
            }

            return true;
        }
    }

    internal abstract class VariantHandle<T> : VariantHandle
    {
        public override object RawValue
        {
            get => Value;
            set => Value = (T)value;
        }

        protected abstract T Value { get; set; }
        protected override Type Type => typeof(T);
        protected abstract bool SetValueOnGUI { get; }

        protected VariantHandle(MemberInfo memberInfo, VariantAttribute attribute, InstallationRoutine owner)
            : base(memberInfo, attribute, owner)
        {
        }

        public override string ToJson()
        {
            // Handle string values directly since JsonUtility doesn't serialize primitive strings correctly
            if (typeof(T) == typeof(string))
            {
                return Value?.ToString() ?? string.Empty;
            }

            // For other types, use JsonUtility
            return JsonUtility.ToJson(Value);
        }

        public override void FromJson(string json)
        {
            // Handle string values directly
            if (typeof(T) == typeof(string))
            {
                RawValue = (T)(object)(json ?? string.Empty);
                return;
            }

            // For other types, use JsonUtility
            RawValue = JsonUtility.FromJson<T>(json);
        }
        public override VariantHandle ToSelection(bool forceValue = true) => new VariantForSelection<T>(this, forceValue);

        public override void DrawGUI(SerializedObject serializedObject, out bool changed)
        {
            changed = false;
            using var disabledScope = new EditorGUI.DisabledScope(!Condition());
            EditorGUILayout.BeginVertical(GUIStyles.ContentBox);
            EditorGUILayout.BeginHorizontal();

            if (serializedObject != null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(MemberInfo.Name));
            }
            else
            {
                var name = ObjectNames.NicifyVariableName(MemberInfo.Name);
                EditorGUILayout.LabelField(name, Styles.GUIStyles.LabelStyle, GUILayout.Width(Constants.LabelWidth));

                // Check if we have an options method specified for any type
                if (!string.IsNullOrEmpty(Attribute.OptionsMethod))
                {
                    DrawFieldWithOptions(out changed);
                }
                else
                {
                    // Fall back to default field drawing
                    switch (Value)
                    {
                        case int intValue:
                            ApplyValue((T)(object)EditorGUILayout.IntField(intValue), out changed);
                            break;
                        case string stringValue:
                            ApplyValue((T)(object)EditorGUILayout.TextField(stringValue), out changed);
                            break;
                        case bool boolValue:
                            ApplyValue((T)(object)EditorGUILayout.Toggle(boolValue), out changed);
                            break;
                        case Enum enumValue:
                            ApplyValue((T)(object)EditorGUILayout.EnumPopup(enumValue), out changed);
                            break;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(Attribute.Description))
            {
                EditorGUILayout.LabelField(Attribute.Description, Styles.GUIStyles.InfoStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStringField(string stringValue, out bool changed)
        {
            changed = false;

            // Check if we have an options method specified
            if (!string.IsNullOrEmpty(Attribute.OptionsMethod))
            {
                var options = GetStringOptions();
                if (options is { Length: > 0 })
                {
                    var currentIndex = Array.IndexOf(options, stringValue);
                    if (currentIndex < 0) currentIndex = 0; // Default to first option if current value not found

                    var newIndex = EditorGUILayout.Popup(currentIndex, options);
                    if (newIndex != currentIndex && newIndex >= 0 && newIndex < options.Length)
                    {
                        ApplyValue((T)(object)options[newIndex], out changed);
                    }
                    return;
                }
            }

            // Fall back to regular text field if no options or options method failed
            ApplyValue((T)(object)EditorGUILayout.TextField(stringValue), out changed);
        }

        /// <summary>
        /// Helper method to search for a method in the type hierarchy, including base classes
        /// </summary>
        private static MethodInfo GetMethodFromTypeHierarchy(Type type, string methodName)
        {
            var currentType = type;
            while (currentType != null)
            {
                var method = currentType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (method != null)
                    return method;

                currentType = currentType.BaseType;
            }
            return null;
        }

        private void DrawFieldWithOptions(out bool changed)
        {
            changed = false;

            try
            {
                var optionsMethod = GetMethodFromTypeHierarchy(Owner.GetType(), Attribute.OptionsMethod);
                if (optionsMethod == null)
                {
                    Debug.LogWarning($"Options method '{Attribute.OptionsMethod}' not found on type '{Owner.GetType().Name}' or its base classes");
                    DrawFallbackField(out changed);
                    return;
                }

                var result = optionsMethod.Invoke(Owner, null);
                if (result == null)
                {
                    Debug.LogWarning($"Options method '{Attribute.OptionsMethod}' returned null");
                    DrawFallbackField(out changed);
                    return;
                }

                // Handle IEnumerable<T> where T matches our field type
                if (result is IEnumerable enumerable)
                {
                    var options = enumerable.Cast<T>().ToArray();
                    if (options.Length == 0)
                    {
                        Debug.LogWarning($"Options method '{Attribute.OptionsMethod}' returned empty collection");
                        DrawFallbackField(out changed);
                        return;
                    }

                    // Find current index
                    var currentIndex = Array.IndexOf(options, Value);
                    if (currentIndex < 0)
                    {
                        // Current value is not in available options, reset to first option
                        ApplyValue(options[0], out var resetChanged);
                        currentIndex = 0;
                    }

                    // Create display names for the popup
                    var displayNames = options.Select(option => option?.ToString() ?? "null").ToArray();

                    var newIndex = EditorGUILayout.Popup(currentIndex, displayNames);
                    if (newIndex != currentIndex && newIndex >= 0 && newIndex < options.Length)
                    {
                        ApplyValue(options[newIndex], out changed);
                    }
                    return;
                }

                Debug.LogWarning($"Options method '{Attribute.OptionsMethod}' did not return an IEnumerable");
                DrawFallbackField(out changed);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get options from method '{Attribute.OptionsMethod}': {ex.Message}");
                DrawFallbackField(out changed);
            }
        }

        private void DrawFallbackField(out bool changed)
        {
            // Fall back to default field drawing
            switch (Value)
            {
                case int intValue:
                    ApplyValue((T)(object)EditorGUILayout.IntField(intValue), out changed);
                    break;
                case string stringValue:
                    ApplyValue((T)(object)EditorGUILayout.TextField(stringValue), out changed);
                    break;
                case bool boolValue:
                    ApplyValue((T)(object)EditorGUILayout.Toggle(boolValue), out changed);
                    break;
                case Enum enumValue:
                    ApplyValue((T)(object)EditorGUILayout.EnumPopup(enumValue), out changed);
                    break;
                default:
                    EditorGUILayout.LabelField($"Unsupported type: {typeof(T).Name}");
                    changed = false;
                    break;
            }
        }

        private string[] GetStringOptions()
        {
            try
            {
                var optionsMethod = GetMethodFromTypeHierarchy(Owner.GetType(), Attribute.OptionsMethod);
                if (optionsMethod == null) return null;

                var result = optionsMethod.Invoke(Owner, null);
                switch (result)
                {
                    case IEnumerable<string> stringEnumerable:
                        return stringEnumerable.ToArray();
                    case IEnumerable enumerable:
                        // Handle other IEnumerable types by converting to string
                        return enumerable.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get string options from method '{Attribute.OptionsMethod}': {ex.Message}");
            }

            return null;
        }

        internal void ApplyValue(T value, out bool changed)
        {
            changed = false;

            if (!SetValueOnGUI) return;

            if (Value.Equals(value)) return;

            changed = true;
            Value = value;
        }
    }

    /// <summary>
    /// VariantHandle tightly attached to an InstallationRoutine
    /// Used to store a Value for the Variant
    /// </summary>
    internal class VariantForRoutine<T> : VariantHandle<T>
    {
        protected override bool SetValueOnGUI => false;

        protected override T Value
        {
            get
            {
                switch (MemberInfo?.MemberType)
                {
                    case MemberTypes.Field:
                    {
                        var field = MemberInfo as FieldInfo;
                        return (T)field?.GetValue(Owner);
                    }

                    case MemberTypes.Property:
                    {
                        var property = MemberInfo as PropertyInfo;
                        return (T)property?.GetValue(Owner);
                    }
                }

                return default(T);
            }
            set
            {
                switch (MemberInfo?.MemberType)
                {
                    case MemberTypes.Field:
                    {
                        var field = MemberInfo as FieldInfo;
                        field?.SetValue(Owner, value);
                        break;
                    }

                    case MemberTypes.Property:
                    {
                        var property = MemberInfo as PropertyInfo;
                        property?.SetValue(Owner, value);
                        break;
                    }
                }
            }
        }

        private VariantForRoutine(MemberInfo memberInfo, VariantAttribute attribute, InstallationRoutine owner)
            : base(memberInfo, attribute, owner)
        {
        }

        internal static VariantForRoutine<T> FromOwner(MemberInfo member, VariantAttribute attribute, InstallationRoutine owner)
        {
            return new VariantForRoutine<T>(member, attribute, owner);
        }
    }

    /// <summary>
    /// VariantHandle not specifically attached to an InstallationRoutine
    /// Used to store a Value for the Variant
    /// </summary>
    internal class VariantForSelection<T> : VariantHandle<T>
    {
        protected override bool SetValueOnGUI => true;

        protected sealed override T Value { get; set; }

        public VariantForSelection(VariantHandle source, bool forceValue)
            : base(source.MemberInfo, source.Attribute, source.Owner)
        {
            var copyValue = forceValue || (source.Attribute.Default == null);
            Value = copyValue ? (T)source.RawValue : (T)source.Attribute.Default;
        }
    }
}
