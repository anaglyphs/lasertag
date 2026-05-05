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
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Meta.XR.ImmersiveDebugger.Utils
{
    [Serializable]
    internal readonly struct InstanceHandle : IEquatable<InstanceHandle>
    {
        public Object Instance { get; }
        public Type Type { get; }
#if UNITY_6000_5_OR_NEWER
        public EntityId InstanceId { get; }
#else
        public int InstanceId { get; }
#endif

#if UNITY_6000_5_OR_NEWER
        public bool IsStatic => InstanceId == EntityId.None;
#else
        public bool IsStatic => InstanceId == 0;
#endif
        public bool Valid => Type != null && (IsStatic || Instance != null || Type == typeof(Scene));

        public InstanceHandle(Type type, Object instance)
        {
            Type = type;
            Instance = instance;
#if UNITY_6000_5_OR_NEWER
            InstanceId = instance != null ? instance.GetEntityId() : EntityId.None;
#else
            InstanceId = instance != null ? instance.GetInstanceID() : 0;
#endif
        }

        public InstanceHandle(Scene scene)
        {
            Type = typeof(Scene);
            Instance = null;
#if UNITY_6000_5_OR_NEWER
            InstanceId = EntityId.FromULong(scene.handle.GetRawData());
#else
            InstanceId = scene.handle;
#endif
        }

        public static InstanceHandle Static(Type type) => new InstanceHandle(type, null);
        public bool Equals(InstanceHandle other) => InstanceId == other.InstanceId && Type == other.Type;
        public override bool Equals(object obj) => obj is InstanceHandle other && Equals(other);
        public override int GetHashCode() => unchecked(InstanceId.GetHashCode() * 486187739 + Type?.GetHashCode() ?? 0);
    }
}

