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
using System.Collections.Generic;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Opt-in marker for providers that use a centrally managed API key.
    /// </summary>
    public interface IUsesCredential
    {
        /// <summary>
        /// Unique identifier for this provider type (e.g., "OpenAI", "LlamaApi").
        /// Used to store and retrieve credentials from the central CredentialStorage.
        /// </summary>
        string ProviderId { get; }

        /// <summary>
        /// When true, this provider asset uses its own API key instead of the central storage.
        /// </summary>
        bool OverrideApiKey { get; set; }

        /// <summary>
        /// Returns the test configuration for this provider, including endpoint, model, and provider type.
        /// Used by both CredentialStorage and provider-specific editors to test connections.
        /// </summary>
        ProviderTestConfig GetTestConfig();
    }

    /// <summary>
    /// Central, project-wide vault for credentials used by provider assets. Avoids duplicated API keys,
    /// enables autofilling in editors, and reduces accidental key loss when refactoring provider assets.
    /// </summary>
    /// <remarks>
    /// See also CredentialStorageEditor for editor UX that auto-registers providers.
    /// </remarks>
    [CreateAssetMenu(menuName = "Meta/AI/Credential Storage")]
    public sealed class CredentialStorage : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string providerId;
            public string apiKey;
        }

        [SerializeField] private List<Entry> entries = new();

        /// <summary>
        /// Attempts to resolve a credential by logical id (for example, "openai.apiKey").
        /// Returns true and sets <paramref name="entry"/> when found; otherwise returns false.
        /// </summary>
        /// <param name="providerId">Logical identifier used by provider editors and runtime code.</param>
        /// <param name="entry">Resolved secret value, if present and non-empty.</param>
        /// <returns>True if an entry exists with a non-empty value; otherwise false.</returns>
        public bool TryGetEntry(string providerId, out Entry entry)
        {
            entry = entries.Find(x => string.Equals(x.providerId, providerId, StringComparison.OrdinalIgnoreCase));
            return entry != null;
        }

        /// <summary>
        /// Ensures a credential entry exists for the provider. If missing, creates an empty slot so
        /// users can paste the value once. Safe to call repeatedly; the operation is idempotent.
        /// </summary>
        /// <param name="providerId">Logical provider id, for example "openai.apiKey".</param>
        public void AddProviderIfMissing(string providerId)
        {
            if (entries.Exists(x => string.Equals(x.providerId, providerId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            entries.Add(new Entry { providerId = providerId, apiKey = "" });
        }
    }
}
