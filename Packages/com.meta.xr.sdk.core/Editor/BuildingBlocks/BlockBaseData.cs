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
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.Tags;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using Meta.XR.Editor.UserInterface;

namespace Meta.XR.BuildingBlocks.Editor
{
    public abstract class BlockBaseData : ScriptableObject, ITaggable, IIdentified, IComparable<BlockBaseData>
    {
        internal static readonly CachedIdDictionary<BlockBaseData> Registry = new();

        [SerializeField, OVRReadOnly] internal string id = Guid.NewGuid().ToString();
        public string Id => id;

        [SerializeField, OVRReadOnly] internal int version = 1;
        public int Version => version;

        private static readonly TextureContent DefaultThumbnailTexture = TextureContent.CreateContent("bb_thumb_default.jpg",
            Utils.BuildingBlocksThumbnails);

        internal static readonly TextureContent DefaultInternalThumbnailTexture = TextureContent.CreateContent("bb_thumb_internal.jpg",
            Utils.BuildingBlocksThumbnails);

        [SerializeField] internal string blockName;
        public Overridable<string> BlockName { get; private set; } = new("");

        [SerializeField] internal string description;
        public Overridable<string> Description { get; private set; } = new("");

        #region Tags

        [SerializeField] internal TagArray tags;

        private TagArray SerializedTags => tags ??= new TagArray();
        private Overridable<TagArray> _overridableTags;
        public Overridable<TagArray> OverridableTags => _overridableTags ??= new Overridable<TagArray>(SerializedTags);
        public TagArray Tags => OverridableTags.Value;

        internal virtual void OnEnable()
        {
            Description = new Overridable<string>(description);
            BlockName = new Overridable<string>(blockName);
        }

        public void OnAwake()
        {
            ValidateTags();
        }

        public void OnValidate()
        {
            ValidateTags();
        }

        private void ValidateTags()
        {
            {
                Tags.Remove(Utils.InternalTag);
            }

            if (IsNew())
            {
                Tags.Add(Utils.NewTag);
            }
            else
            {
                Tags.Remove(Utils.NewTag);
            }

            Tags.OnValidate();
        }

        private OVRProjectSetupSettingBool _hasSeenBefore;

        private bool IsNew()
        {
            _hasSeenBefore ??= new OVRProjectSetupUserSettingBool($"HasSeenBeforeKey_{Id}", false);
            return !_hasSeenBefore.Value;
        }

        internal void MarkAsSeen()
        {
            if (_hasSeenBefore == null || _hasSeenBefore.Value)
            {
                return;
            }

            _hasSeenBefore.Value = true;
            ValidateTags();
        }

        internal void ResetSeen()
        {
            _hasSeenBefore.Value = false;
            ValidateTags();
        }
        #endregion

        [SerializeField] internal Texture2D thumbnail;

        public Texture2D Thumbnail
        {
            get
            {
                if (thumbnail != null)
                {
                    return thumbnail;
                }

                if (!Hidden)
                {
                    return DefaultThumbnailTexture.Image as Texture2D;
                }

                return DefaultInternalThumbnailTexture.Image as Texture2D;
            }
        }

        public virtual bool Hidden => Tags.Any(tag => tag.Behavior.Visibility == false);

        public bool Experimental => Tags.Contains(Utils.ExperimentalTag);

        [SerializeField] internal int order;
        public int Order => order;



        internal virtual bool CanBeAdded => !Utils.IsApplicationPlaying.Invoke();

        internal abstract Task AddToProject(GameObject selectedGameObject = null, Action onInstall = null);

        internal virtual async Task AddToObjects(List<GameObject> selectedGameObjects)
        {
            foreach (var obj in selectedGameObjects.DefaultIfEmpty())
            {
                await AddToProject(obj);
            }
        }

        internal virtual bool RequireListRefreshAfterInstall => false;

        internal virtual bool OverridesInstallRoutine => false;
        public int CompareTo(BlockBaseData other)
        {
            return other == null ? 0 : string.Compare(other.BlockName.Value, BlockName.Value, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
