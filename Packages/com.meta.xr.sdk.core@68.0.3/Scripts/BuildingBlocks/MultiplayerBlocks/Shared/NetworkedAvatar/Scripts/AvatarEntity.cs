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
using Meta.XR.BuildingBlocks;
using UnityEngine;

#if META_AVATAR_SDK_DEFINED
using Oculus.Avatar2;
#endif // META_AVATAR_SDK_DEFINED

namespace Meta.XR.MultiplayerBlocks.Shared
{
    public interface IAvatarBehaviour
    {
        // synced to network, can be 0 if user not entitled
        public ulong OculusId { get; }
        // synced to network, indicating which avatar from sample assets is used
        // this should be initialized randomly for each user before entity spawned
        public int LocalAvatarIndex { get; }
        public bool HasInputAuthority { get; }
        public void ReceiveStreamData(byte[] bytes);
    }

    public enum AvatarStreamLOD
    {
        Low,
        Medium,
        High
    }

    public interface IAvatarStreamConfig
    {
        public void SetAvatarStreamLOD(AvatarStreamLOD lod);
        public void SetAvatarUpdateIntervalInS(float interval);
    }

#if META_AVATAR_SDK_DEFINED
    /// <summary>
    /// Avatar Entity implementation for Networked Avatar, loads remote/local avatar according to IAvatarBehaviour
    /// and also provide fallback solution to local zip avatar with a randomized preloaded avatar from sample assets
    /// when the user is not entitled (no Oculus Id) or has no avatar setup
    /// </summary>
    public class AvatarEntity : OvrAvatarEntity, IAvatarStreamConfig
    {
        public static event Action<AvatarEntity> OnSpawned;

        private byte[] _streamedData;
        private float _cycleStartTime;
        private bool _skeletonLoaded;
        private bool _initialAvatarLoaded;
        private IAvatarBehaviour _avatarBehaviour;
        private StreamLOD _streamLevel = StreamLOD.Medium;
        private float _intervalToSendDataInSec = 0.08f;

        /// <summary>
        /// Could be triggered by any changes like oculus id, local avatar index, network connection etc.
        /// </summary>
        public void ReloadAvatarManually()
        {
            if (!_initialAvatarLoaded)
            {
                return;
            }

            _skeletonLoaded = false;
            EntityActive = false;
            Teardown();
            CreateEntity();
            LoadAvatar();
        }

        protected override void Awake()
        {
            _avatarBehaviour = this.GetInterfaceComponent<IAvatarBehaviour>();
            if (_avatarBehaviour == null)
            {
                throw new InvalidOperationException("Using AvatarEntity without an IAvatarBehaviour");
            }

            OnSpawned?.Invoke(this);
        }

        private void Start()
        {
            if (_avatarBehaviour == null)
            {
                return;
            }

            ConfigureAvatar();
            base.Awake(); // creating avatar entity here

            if (!_avatarBehaviour.HasInputAuthority)
            {
                SetActiveView(CAPI.ovrAvatar2EntityViewFlags.ThirdPerson);
            }

            LoadAvatar();
            _initialAvatarLoaded = true;
        }

        private void ConfigureAvatar()
        {
            if (_avatarBehaviour.HasInputAuthority)
            {
                SetIsLocal(true);
                _creationInfo.features = CAPI.ovrAvatar2EntityFeatures.Preset_Default;
                var entityInputManager = OvrAvatarManager.Instance.gameObject.GetComponent<EntityInputManager>();
                SetBodyTracking(entityInputManager);
                var lipSyncInput = FindObjectOfType<OvrAvatarLipSyncContext>();
                SetLipSync(lipSyncInput);
                gameObject.name = "LocalAvatar";
            }
            else
            {
                SetIsLocal(false);
                _creationInfo.features = CAPI.ovrAvatar2EntityFeatures.Preset_Remote;
                gameObject.name = "RemoteAvatar";
            }
        }

        private void LoadAvatar()
        {
            if (_avatarBehaviour.OculusId == 0)
            {
                LoadLocalAvatar();
            }
            else
            {
                StartCoroutine(TryToLoadUserAvatar());
            }
        }

        private IEnumerator TryToLoadUserAvatar()
        {
            while (!OvrAvatarEntitlement.AccessTokenIsValid())
            {
                yield return null;
            }
            _userId = _avatarBehaviour.OculusId;
            var hasAvatarRequest = OvrAvatarManager.Instance.UserHasAvatarAsync(_userId);
            while (hasAvatarRequest.IsCompleted == false)
            {
                yield return null;
            }
            if (hasAvatarRequest.Result == OvrAvatarManager.HasAvatarRequestResultCode.HasAvatar)
            {
                LoadUser();
            }
            else // fallback to local avatar
            {
                LoadLocalAvatar();
            }
        }

        private void LoadLocalAvatar()
        {
#if META_AVATAR_SAMPLE_ASSETS_DEFINED
            // we only load local avatar from zip after Avatar Sample Assets is installed
            var assetPath = $"{_avatarBehaviour.LocalAvatarIndex}{GetAssetPostfix()}";
            LoadAssets(new[] { assetPath }, AssetSource.Zip);
#else
            Debug.LogWarning("Meta Avatar Sample Assets package not installed, local avatar cannot be loaded from zip");
#endif // META_AVATAR_SAMPLE_ASSETS_DEFINED
        }

        private string GetAssetPostfix(bool isFromZip = true) {
            return "_" + OvrAvatarManager.Instance.GetPlatformGLBPostfix(_creationInfo.renderFilters.quality, isFromZip)
                       + OvrAvatarManager.Instance.GetPlatformGLBVersion(_creationInfo.renderFilters.quality, isFromZip)
                       + OvrAvatarManager.Instance.GetPlatformGLBExtension(isFromZip);
        }

        protected override void OnSkeletonLoaded()
        {
            base.OnSkeletonLoaded();
            _skeletonLoaded = true;
        }

        private void Update()
        {
            if (!_skeletonLoaded || _streamedData == null || IsLocal)
            {
                return;
            }

            if (_streamedData == null)
            {
                return;
            }

            //Apply the remote avatar state and smooth the animation
            ApplyStreamData(_streamedData);
            SetPlaybackTimeDelay(_intervalToSendDataInSec / 2);
            _streamedData = null;
        }

        private void LateUpdate()
        {
            if (!_skeletonLoaded)
            {
                return;
            }

            var elapsedTime = Time.time - _cycleStartTime;
            if (elapsedTime < _intervalToSendDataInSec)
            {
                return;
            }

            RecordAndSendStreamDataIfHasAuthority();
            _cycleStartTime = Time.time;
        }

        private void RecordAndSendStreamDataIfHasAuthority()
        {
            if (!IsLocal || _avatarBehaviour == null)
            {
                return;
            }

            var bytes = RecordStreamData(_streamLevel);
            _avatarBehaviour.ReceiveStreamData(bytes);
        }

        public void SetStreamData(byte[] bytes)
        {
            _streamedData = bytes;
        }

#region IAvatarStreamLOD

        public void SetAvatarStreamLOD(AvatarStreamLOD lod)
        {
            _streamLevel = lod switch
            {
                AvatarStreamLOD.Low => StreamLOD.Low,
                AvatarStreamLOD.Medium => StreamLOD.Medium,
                AvatarStreamLOD.High => StreamLOD.High,
                _ => StreamLOD.Medium
            };
        }

        public void SetAvatarUpdateIntervalInS(float interval)
        {
            _intervalToSendDataInSec = interval;
        }

#endregion
    }
#endif // META_AVATAR_SDK_DEFINED
}
