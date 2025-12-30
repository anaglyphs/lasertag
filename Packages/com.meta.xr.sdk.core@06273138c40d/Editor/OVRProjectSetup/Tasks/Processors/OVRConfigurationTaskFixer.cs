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
using UnityEditor;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

internal class OVRConfigurationTaskFixer : OVRConfigurationTaskProcessor
{
    public override int AllocatedTimeInMs => 10;
    public override ProcessorType Type => ProcessorType.Fixer;

    protected override Func<IEnumerable<OVRConfigurationTask>, List<OVRConfigurationTask>> OpenTasksFilter =>
        (Func<IEnumerable<OVRConfigurationTask>, List<OVRConfigurationTask>>)(tasksToFilter => tasksToFilter
            .Where(task => (task.FixAction != null || task.AsyncFixAction != null)
                           && !task.IsDone(BuildTargetGroup)
                           && !task.IsIgnored(BuildTargetGroup))
            .ToList());

    private const int LoopExitCount = 4;

    private bool _hasFixedSome = false;
    private int _counter = LoopExitCount;
    private Task _currentAsyncTask;
    private bool _isProcessingAsync = false;
    private Queue<OVRConfigurationTask> _asyncTaskQueue = new Queue<OVRConfigurationTask>();

    public OVRConfigurationTaskFixer(
        OVRConfigurationTaskRegistry registry,
        BuildTargetGroup buildTargetGroup,
        Func<IEnumerable<OVRConfigurationTask>, List<OVRConfigurationTask>> filter,
        OVRProjectSetup.LogMessages logMessages,
        bool blocking,
        Action<OVRConfigurationTaskProcessor> onCompleted)
        : base(registry, buildTargetGroup, filter, logMessages, blocking, onCompleted)
    {
    }

    protected override void ProcessTask(OVRConfigurationTask task)
    {
        if (task.FixAction != null)
        {
            _hasFixedSome |= task.Fix(BuildTargetGroup);
        }
        else if (task.AsyncFixAction != null)
        {
            // Queue async tasks to be processed one at a time
            _asyncTaskQueue.Enqueue(task);
            ProcessNextAsyncTask();
        }
    }

    private void ProcessNextAsyncTask()
    {
        if (_isProcessingAsync || _asyncTaskQueue.Count == 0)
        {
            return;
        }

        var task = _asyncTaskQueue.Dequeue();
        _currentAsyncTask = ProcessAsyncTaskInternal(task);
    }

    private async Task ProcessAsyncTaskInternal(OVRConfigurationTask task)
    {
        _isProcessingAsync = true;
        try
        {
            var result = await task.FixAsync(BuildTargetGroup);
            _hasFixedSome |= result;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Failed to execute async fix for task \"{task.Message.GetValue(BuildTargetGroup)}\": {ex.Message}");
        }
        finally
        {
            _isProcessingAsync = false;
            // Process next async task if any
            ProcessNextAsyncTask();
        }
    }

    public override void Update()
    {
        // Process synchronous tasks normally
        base.Update();

        // Check if async task is completed and handle completion
        if (_currentAsyncTask != null && _currentAsyncTask.IsCompleted)
        {
            if (_currentAsyncTask.IsFaulted)
            {
                Debug.LogError($"[{OVRProjectSetupUtils.ProjectSetupToolPublicName}] Async task failed: {_currentAsyncTask.Exception?.GetBaseException()?.Message}");
            }
            _currentAsyncTask = null;
        }
    }

    protected override void PrepareTasks()
    {
        _hasFixedSome = false;
        _asyncTaskQueue.Clear();
        base.PrepareTasks();
    }

    protected override void Validate()
    {
        // Don't validate while async operations are running
        if (_isProcessingAsync || _asyncTaskQueue.Count > 0)
        {
            return;
        }

        _counter--;

        if (_counter <= 0)
        {
            Debug.LogWarning("[Oculus Settings] Fixing Tasks has exited after too many iterations. " +
                             "(There might be some contradictory rules leading to a loop)");
            return;
        }

        if (!_hasFixedSome)
        {
            return;
        }

        // Preparing a new Run
        PrepareTasks();
        if (Blocking)
        {
            Update();
        }
    }

    public override bool Completed => base.Completed && !_isProcessingAsync && _asyncTaskQueue.Count == 0;
}
