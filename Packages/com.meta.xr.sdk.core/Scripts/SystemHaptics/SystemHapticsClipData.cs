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

[Serializable]
public class SystemHapticsClipData : ScriptableObject
{
    [Serializable]
    public class Version
    {
        public int major;
        public int minor;
        public int patch;
    }

    [Serializable]
    public class Metadata
    {
        public string editor;
        public string source;
        public string project;
        public string[] tags;
        public string description;
    }

    [Serializable]
    public class AmplitudeEnvelope
    {
        public float time;
        public float amplitude;
        public Emphasis emphasis;
    }

    [Serializable]
    public class FrequencyEnvelope
    {
        public float time;
        public float frequency;
    }

    [Serializable]
    public class Emphasis
    {
        public float amplitude;
        public float frequency;
    }

    [Serializable]
    public class Envelopes
    {
        public AmplitudeEnvelope[] amplitude;
        public FrequencyEnvelope[] frequency;
    }

    [Serializable]
    public class Continuous
    {
        public Envelopes envelopes;
    }

    [Serializable]
    public class Signals
    {
        public Continuous continuous;
    }

    public Version version;
    public Metadata metadata;
    public Signals signals;
}
