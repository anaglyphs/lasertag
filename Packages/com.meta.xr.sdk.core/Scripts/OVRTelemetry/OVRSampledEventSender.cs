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

internal class OVRSampledEventSender
{
    private bool _shouldSend;
    private readonly float _recordChance;
    private readonly string _falcoEventName;
    private readonly bool _isEssential;
    private readonly OVRPlugin.ProductType _productType;
    private readonly Func<OVRPlugin.UnifiedEventData, OVRPlugin.UnifiedEventData> _addMetadataFunc;
    private OVRPlugin.UnifiedEventData _unifiedEventData;

    public OVRSampledEventSender(float recordRecordChance,
        string falcoEventName = null,
        bool isEssential = false,
        OVRPlugin.ProductType productType = OVRPlugin.ProductType.None,
        Func<OVRPlugin.UnifiedEventData, OVRPlugin.UnifiedEventData> addMetadataFunc = null)
    {
        _recordChance = recordRecordChance;
        _falcoEventName = falcoEventName;
        _isEssential = isEssential;
        _productType = productType;
        _addMetadataFunc = addMetadataFunc;
    }

    public void Send()
    {
        if (!_shouldSend)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_falcoEventName))
        {
            _unifiedEventData.Send();
        }
        _shouldSend = false;
    }

    public void Start()
    {
        if (!ShouldSendEvent(_recordChance))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_falcoEventName))
        {
            _unifiedEventData = new OVRPlugin.UnifiedEventData(_falcoEventName)
            {
                isEssential = _isEssential ? OVRPlugin.Bool.True : OVRPlugin.Bool.False,
                productType = _productType
            };

            if (_addMetadataFunc != null)
            {
                _unifiedEventData = _addMetadataFunc.Invoke(_unifiedEventData);
            }
        }

        _shouldSend = true;
    }

    private static bool ShouldSendEvent(float chance) => UnityEngine.Random.value < chance;
}
