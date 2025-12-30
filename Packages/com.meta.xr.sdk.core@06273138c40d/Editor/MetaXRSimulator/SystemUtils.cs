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
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Meta.XR.Simulator.Editor
{
    internal class SystemUtils
    {
        public virtual bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public virtual bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public virtual List<string> ListSubdirectories(string path)
        {
            var directories = Directory.EnumerateDirectories(path);
            return directories.Select(Path.GetFileName).ToList();
        }

        public virtual string GetEnvironmentVariable(string variable)
        {
            return Environment.GetEnvironmentVariable(variable);
        }

        public virtual void SetEnvironmentVariable(string variable, string value)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
        internal virtual void OpenDirectory(string xrSimDataPath)
        {
            if (!Directory.Exists(xrSimDataPath))
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = xrSimDataPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }
}
