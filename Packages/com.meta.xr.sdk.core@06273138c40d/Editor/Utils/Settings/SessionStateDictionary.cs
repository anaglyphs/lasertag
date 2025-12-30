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

using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Meta.XR.Editor.RemoteContent
{
    /// <summary>
    /// A dictionary-like wrapper around Unity's SessionState that persists boolean data across compilation.
    /// Provides standard dictionary operations for boolean values.
    /// </summary>
    internal class SessionStateBoolDictionary
    {
        private readonly string _keyPrefix;
        private readonly string _keysListKey;
        private List<string> _cachedKeys;

        public SessionStateBoolDictionary(string keyPrefix)
        {
            _keyPrefix = keyPrefix;
            _keysListKey = keyPrefix + "_KeysList";
        }

        /// <summary>
        /// Gets or sets the value associated with the specified key.
        /// </summary>
        public bool this[string key]
        {
            get => Get(key);
            set => Add(key, value);
        }

        /// <summary>
        /// Gets the number of key/value pairs stored in the dictionary.
        /// </summary>
        public int Count
        {
            get
            {
                var keysList = GetStoredKeys();
                return keysList.Count;
            }
        }

        /// <summary>
        /// Gets all keys currently stored in the dictionary.
        /// </summary>
        public IEnumerable<string> Keys => GetStoredKeys();

        /// <summary>
        /// Adds or updates a key/value pair in the dictionary.
        /// </summary>
        public void Add(string key, bool value)
        {
            var sessionKey = _keyPrefix + key;
            SessionState.SetBool(sessionKey, value);

            // Update the keys list
            var keysList = GetStoredKeys();
            if (!keysList.Contains(key))
            {
                keysList.Add(key);
                UpdateStoredKeys(keysList);
            }
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified key.
        /// </summary>
        public bool ContainsKey(string key)
        {
            var keysList = GetStoredKeys();
            return keysList.Contains(key);
        }

        /// <summary>
        /// Removes the value with the specified key from the dictionary.
        /// </summary>
        public bool Remove(string key)
        {
            if (!ContainsKey(key))
                return false;

            var sessionKey = _keyPrefix + key;
            SessionState.EraseBool(sessionKey);

            // Update the keys list
            var keysList = GetStoredKeys();
            keysList.Remove(key);
            UpdateStoredKeys(keysList);

            return true;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool Get(string key, bool defaultValue = false)
        {
            if (!ContainsKey(key))
                return defaultValue;

            var sessionKey = _keyPrefix + key;
            return SessionState.GetBool(sessionKey, defaultValue);
        }


        /// <summary>
        /// Removes all keys and values from the dictionary.
        /// </summary>
        public void Clear()
        {
            var keysList = GetStoredKeys();
            foreach (var key in keysList)
            {
                var sessionKey = _keyPrefix + key;
                SessionState.EraseBool(sessionKey);
            }

            SessionState.EraseString(_keysListKey);
            _cachedKeys = null;
        }

        /// <summary>
        /// Returns all key-value pairs as a formatted string for debugging/telemetry.
        /// </summary>
        public string ToFormattedString()
        {
            var keyValuePairs = new List<string>();
            foreach (var key in GetStoredKeys())
            {
                var value = Get(key);
                keyValuePairs.Add($"{key}:{value}");
            }
            return string.Join(",", keyValuePairs);
        }

        private List<string> GetStoredKeys()
        {
            if (_cachedKeys != null)
            {
                return _cachedKeys;
            }

            var keysString = SessionState.GetString(_keysListKey, "");
            _cachedKeys = keysString.Split(',').Where(k => !string.IsNullOrEmpty(k)).ToList();

            return _cachedKeys;
        }

        private void UpdateStoredKeys(IEnumerable<string> keys)
        {
            var keysString = string.Join(",", keys);
            SessionState.SetString(_keysListKey, keysString);
            _cachedKeys = null;
        }
    }
}
