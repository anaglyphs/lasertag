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
using UnityEditor;

namespace Meta.XR.Editor.RemoteContent
{
    internal interface IScopedProgressDisplayer : IDisposable
    {
        public void Update(float progress, long seconds);
        public void SetDescription(string description);
    }

    internal class NullScopedScopedProgressDisplayer : IScopedProgressDisplayer
    {
        public static readonly NullScopedScopedProgressDisplayer Instance = new();

        private NullScopedScopedProgressDisplayer() { }

        public void Dispose()
        {
        }

        public void Update(float progress, long seconds)
        {
        }

        public void SetDescription(string description)
        {
        }
    }

    internal class ScopedScopedProgressDisplayer : IScopedProgressDisplayer
    {
        private readonly int _progressId;

        public ScopedScopedProgressDisplayer(string msg)
        {
            _progressId = Progress.Start(msg);
            Progress.SetTimeDisplayMode(_progressId, Progress.TimeDisplayMode.ShowRemainingTime);
        }

        public void Dispose()
        {
            Progress.Remove(_progressId);
        }

        public void Update(float progress, long seconds)
        {
            Progress.Report(_progressId, progress);
            Progress.SetRemainingTime(_progressId, seconds);
        }

        public void SetDescription(string description)
        {
            Progress.SetDescription(_progressId, description);
        }
    }
}
