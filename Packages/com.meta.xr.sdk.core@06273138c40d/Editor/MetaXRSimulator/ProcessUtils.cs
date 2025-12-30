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
using System.Text.RegularExpressions;

namespace Meta.XR.Simulator.Editor
{
    internal class ProcessPort
    {
        public override string ToString()
        {
            return $"{ProcessName}({ProcessId}) ({Protocol} port {PortNumber})";
        }
        public string ProcessName { get; set; }
        public int ProcessId { get; set; }
        public string PortNumber { get; set; }
        public string Protocol { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is ProcessPort other)
            {
                return ProcessId == other.ProcessId;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return ProcessId.GetHashCode();
        }
    }
    internal class ProcessUtils
    {
        public virtual string GetProcessByPid(int pid)
        {
            string procName;
            try
            {
                procName = Process.GetProcessById(pid).ProcessName;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(ex);
                procName = "-";
            }

            return procName;
        }

        public virtual List<ProcessPort> GetProcessesByPort(string targetPort)
        {
            var ports = new HashSet<ProcessPort>();
#if UNITY_EDITOR_OSX
            var path = "lsof";
            var args = $"-t -n -P -iTCP:{targetPort} -sTCP:LISTEN";
#elif UNITY_EDITOR_WIN
            var path = "netstat.exe";
            var args = "-a -n -o";
#else
            var path = "INVALID PATH: REPLACE ME!";
            var args = "INVALID ARGS: REPLACE ME!";
            return new List<ProcessPort>();
#endif
            var (retCode, content) = ExecuteProcess(path, args);
#if UNITY_EDITOR_OSX
            if(retCode == 1){
                // According lsof man page, lsof will return 1 if there is no processes on specified port.
                return new List<ProcessPort>();
            }
#endif
            if (retCode != 0)
            {
                UnityEngine.Debug.LogError(
                                $"{path} {args} call failed, exitCode={retCode}, content={content}");
                return new List<ProcessPort>();
            }

            string[] rows = content.Split(Environment.NewLine);
            foreach (string row in rows)
            {
                int processId = 0;
                string protocol = "", portNumber = "";
#if UNITY_EDITOR_OSX
                if(String.IsNullOrEmpty(row))
                {
                    continue;
                }
                processId = Convert.ToInt32(row);
                protocol = "TCPv6";
                portNumber = targetPort;
#elif UNITY_EDITOR_WIN
                Regex tokenRE = new Regex("\\s+");
                Regex localAddressRE = new Regex(@"\[(.*?)\]");
                string[] tokens = tokenRE.Split(row);
                if (tokens.Length > 4 && (tokens[1].Equals("UDP") || tokens[1].Equals("TCP")))
                {
                    string localAddress = localAddressRE.Replace(tokens[2], "1.1.1.1");
                    portNumber = localAddress.Split(':')[1];
                    if (targetPort != portNumber)
                    {
                        continue;
                    }

                    processId = 0;
                    try
                    {
                        processId = tokens[1].Equals("UDP")
                            ? Convert.ToInt32(tokens[4])
                            : Convert.ToInt32(tokens[5]);

                        protocol = localAddress.Contains("1.1.1.1")
                            ? String.Format("{0}v6", tokens[1])
                            : String.Format("{0}v4", tokens[1]);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError(tokens[1] + " " + tokens[4] + " " + tokens[5]);
                        throw ex;
                    }
                }
#endif
                if (processId != 0)
                {
                    string processName = GetProcessByPid(processId);
                    UnityEngine.Debug.Log($"Found {protocol} port {portNumber} used by {processName}({processId})");
                    ports.Add(new ProcessPort
                    {
                        Protocol = protocol,
                        PortNumber = portNumber,
                        ProcessName = processName,
                        ProcessId = processId
                    });
                }
            }

            return ports.ToList();
        }

        public virtual void StopProcess(string processPort, string logContext)
        {
            var existingProcesses = Utils.ProcessUtils.GetProcessesByPort(processPort);
            foreach (var existingProcess in existingProcesses)
            {
                Utils.LogUtils.ReportInfo(logContext, $"Stopping {existingProcess.ProcessName} with PID {existingProcess.ProcessId}");
                var p = Process.GetProcessById(existingProcess.ProcessId);
                p.Kill();
                p.WaitForExit();
            }
        }

        public virtual void LaunchProcess(string binaryPath, string arguments, string logContext, bool createWindow = true)
        {
            Utils.LogUtils.ReportInfo(logContext, "Launching " + binaryPath + ", createWindow=" + createWindow + ", arguments=" + arguments);
            var newProcess = new Process();
            newProcess.StartInfo.FileName = binaryPath;
            newProcess.StartInfo.Arguments = arguments;

            if (!createWindow)
            {
                newProcess.StartInfo.CreateNoWindow = true;
                newProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                newProcess.StartInfo.UseShellExecute = false;
                newProcess.StartInfo.RedirectStandardOutput = true;
                newProcess.StartInfo.RedirectStandardError = true;
            }
            if (!newProcess.Start())
            {
                Utils.LogUtils.DisplayDialogOrError(logContext, "failed to launch " + binaryPath);
            }
        }

        public virtual (int retCode, string contents) ExecuteProcess(string path, string args)
        {
            using var p = new Process();
            var ps = new ProcessStartInfo
            {
                Arguments = args,
                FileName = path,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            p.StartInfo = ps;
            p.Start();

            StreamReader stdOutput = p.StandardOutput;
            StreamReader stdError = p.StandardError;

            string content = stdOutput.ReadToEnd() + stdError.ReadToEnd();
            p.WaitForExit();
            int retCode = p.ExitCode;
            return (retCode, content);
        }
    }
}
