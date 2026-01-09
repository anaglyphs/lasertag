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
using System.IO;
using System.Threading.Tasks;

namespace Meta.XR.Editor.RemoteContent
{
    internal class ProgressReportingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private readonly IScopedProgressDisplayer _scopedProgressDisplayer;
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private long _totalBytesWritten;
        private long _lastProgressUpdate;
        private long _lastProgressBytes;
        private const long ProgressUpdateIntervalMs = 100;
        private const long MinBytesForUpdate = 1048576; // 1MB

        public ProgressReportingStream(Stream baseStream, long totalBytes, IScopedProgressDisplayer scopedProgressDisplayer)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _scopedProgressDisplayer = scopedProgressDisplayer;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
            UpdateProgress(count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count,
            System.Threading.CancellationToken cancellationToken)
        {
            await _baseStream.WriteAsync(buffer, offset, count, cancellationToken);
            UpdateProgress(count);
        }

        private void UpdateProgress(int bytesWritten)
        {
            _totalBytesWritten += bytesWritten;

            if (_totalBytes <= 0) return;

            var currentTime = _stopwatch.ElapsedMilliseconds;
            var timeSinceLastUpdate = currentTime - _lastProgressUpdate;
            var bytesSinceLastUpdate = _totalBytesWritten - _lastProgressBytes;

            if (timeSinceLastUpdate < ProgressUpdateIntervalMs && bytesSinceLastUpdate < MinBytesForUpdate)
            {
                return;
            }

            var progress = (float)_totalBytesWritten / _totalBytes;
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

            if (elapsedSeconds > 0)
            {
                var downloadSpeed = _totalBytesWritten / elapsedSeconds;
                var remainingBytes = _totalBytes - _totalBytesWritten;
                var estimatedSecondsRemaining = Convert.ToInt64(remainingBytes / downloadSpeed);
                _scopedProgressDisplayer.Update(progress, estimatedSecondsRemaining);
            }
            else
            {
                _scopedProgressDisplayer.Update(progress, 0);
            }

            _lastProgressUpdate = currentTime;
            _lastProgressBytes = _totalBytesWritten;
        }

        public override void Flush() => _baseStream.Flush();

        public override Task FlushAsync(System.Threading.CancellationToken cancellationToken) =>
            _baseStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _scopedProgressDisplayer.Update(1.0f, 0);

            if (disposing)
            {
                _stopwatch?.Stop();
            }

            base.Dispose(disposing);
        }
    }
}
