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
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public class OVRSceneChangeListener
{
    private static readonly List<string> TrackedAssemblies = new()
    {
        "Oculus.",
        "Meta.",
    };

    private static readonly List<Component> ComponentList = new();

    private const float ActivityEventTimeoutSeconds = 300.0f; // 5 minutes
    private const string LastActivityEventTimeKey = "OVRSceneChangeListener_LastActivityEventTime";
    private const string SessionStartTimeKey = "OVRSceneChangeListener_SessionStartTime";
    private const string IsActiveSessionKey = "OVRSceneChangeListener_IsActiveSession";

    static OVRSceneChangeListener()
    {
        Meta.XR.Editor.Callbacks.InitializeOnLoad.Register(OnEditorReady);
    }

    private static void OnEditorReady()
    {
        RegisterCallbacks();
        InitializeActivityTracking();

        OVRTelemetryConsent.OnTelemetrySet += OnTelemetrySet;
    }

    private static void OnTelemetrySet(bool enabled)
    {
        RegisterCallbacks();
        InitializeActivityTracking();
    }

    private static void RegisterCallbacks()
    {
        ObjectChangeEvents.changesPublished -= ChangesPublished;
        ObjectChangeEvents.changesPublished += ChangesPublished;

        CompilationPipeline.compilationStarted -= OnCompilationStarted;
        CompilationPipeline.compilationStarted += OnCompilationStarted;

        EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
        EditorApplication.wantsToQuit += OnEditorWantsToQuit;

        EditorApplication.update -= CheckForInactivity;
        EditorApplication.update += CheckForInactivity;
    }

    private static void RemoveCallbacks()
    {
        ObjectChangeEvents.changesPublished -= ChangesPublished;
        CompilationPipeline.compilationStarted -= OnCompilationStarted;
        EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
        EditorApplication.update -= CheckForInactivity;
    }

    private static void InitializeActivityTracking()
    {
        // Reset session start time if this is a new Unity session (not a domain reload)
        if (SessionState.GetFloat(SessionStartTimeKey, 0f) == 0f)
        {
            SessionState.SetFloat(SessionStartTimeKey, (float)EditorApplication.timeSinceStartup);
        }
    }

    private static void CheckForInactivity()
    {
        if (!SessionState.GetBool(IsActiveSessionKey, false))
            return;

        float lastActivityEventTime = SessionState.GetFloat(LastActivityEventTimeKey, 0f);
        double timeSinceLastActivity = EditorApplication.timeSinceStartup - lastActivityEventTime;

        if (timeSinceLastActivity >= ActivityEventTimeoutSeconds)
        {
            double sessionDuration = lastActivityEventTime - SessionState.GetFloat(SessionStartTimeKey, 0f);

            OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.SceneInactivity)
                .AddAnnotation(OVRTelemetryConstants.Editor.AnnotationType.SessionDuration, sessionDuration.ToString())
                .Send();

            SessionState.SetBool(IsActiveSessionKey, false);
        }
    }

    private static void RegisterUserActivity()
    {
        double currentTime = EditorApplication.timeSinceStartup;

        // If the session is not active, trigger a new session started event
        if (!SessionState.GetBool(IsActiveSessionKey, false))
        {
            // Send activity event - starting or resuming activity
            OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.SceneActivity)
                .Send();

            SessionState.SetBool(IsActiveSessionKey, true);

            // Reset session start time for new activity session
            SessionState.SetFloat(SessionStartTimeKey, (float)currentTime);
        }

        // Update last activity time (resets the inactivity timer)
        SessionState.SetFloat(LastActivityEventTimeKey, (float)currentTime);
    }

    private static void OnCompilationStarted(object obj)
    {
        RegisterUserActivity();
    }

    private static bool OnEditorWantsToQuit()
    {
        if (SessionState.GetBool(IsActiveSessionKey, false))
        {
            // Send inactivity event for explicit close/end of session
            // Include full session duration without subtracting timeout
            double sessionDuration = EditorApplication.timeSinceStartup - SessionState.GetFloat(SessionStartTimeKey, 0f);
            OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.SceneInactivity)
                .AddAnnotation(OVRTelemetryConstants.Editor.AnnotationType.SessionDuration, sessionDuration)
                .Send();
        }

        // Send editor shutdown event
        OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.EditorShutdown)
            .Send();

        return true;
    }

    private static void ProcessComponent(Component component)
    {
        if (component == null)
        {
            return;
        }

        var type = component.GetType();
        var assemblyName = type.Assembly.GetName().Name;
        if (!TrackedAssemblies.Any(trackedAssemblyName =>
               assemblyName.Contains(trackedAssemblyName, StringComparison.InvariantCultureIgnoreCase)))
        {
            return;
        }

        OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.ComponentAdd)
             .AddAnnotation(OVRTelemetryConstants.Editor.AnnotationType.ComponentName, type.Name)
             .AddAnnotation(OVRTelemetryConstants.Editor.AnnotationType.AssemblyName, assemblyName)
             .Send();
    }

    private static void ProcessGameObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        go.GetComponentsInChildren(ComponentList);
        foreach (var component in ComponentList)
        {
            ProcessComponent(component);
        }
    }

    private static void ChangesPublished(ref ObjectChangeEventStream stream)
    {
        RegisterUserActivity();

        // Only process the events if additional data is shared
        if (OVRTelemetryConsent.ShareAdditionalData)
        {
            for (var i = 0; i < stream.length; i++)
            {
                ParseEvent(stream, i);
            }
        }
    }

    private static void ParseEvent(ObjectChangeEventStream stream, int i)
    {
        var eventType = stream.GetEventType(i);

        switch (eventType)
        {
            case ObjectChangeKind.CreateGameObjectHierarchy:
                stream.GetCreateGameObjectHierarchyEvent(i, out var createGameObjectHierarchyEvent);
                ProcessGameObject(
                    EditorUtility.InstanceIDToObject(createGameObjectHierarchyEvent.instanceId) as GameObject);
                break;
            case ObjectChangeKind.ChangeGameObjectStructure:
                stream.GetChangeGameObjectStructureEvent(i, out var changeGameObjectStructure);
                ProcessGameObject(EditorUtility.InstanceIDToObject(changeGameObjectStructure.instanceId) as GameObject);
                break;
        }
    }
}
