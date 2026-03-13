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
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Xml;
using Meta.XR.Telemetry;
using Oculus.VR.Editor;

#if USING_XR_SDK_OPENXR
using UnityEngine.XR.OpenXR;
using Meta.XR;
using UnityEditor.XR.Management;
#endif

public class OVRManifestPreprocessor : EditorWindow
{
    private static readonly string ManifestFileName = "AndroidManifest.xml";
    private static readonly string ManifestFolderName = "Plugins/Android";

    private static readonly string ManifestFolderPathAbsolute =
        Path.Combine(Application.dataPath, ManifestFolderName);

    private static readonly string BuildManifestFolderPathRelative =
        Path.Combine("Assets", ManifestFolderName);

    private static readonly string BuildManifestFilePathAbsolute =
        Path.Combine(ManifestFolderPathAbsolute, ManifestFileName);

    private static readonly string BuildManifestFilePathRelative =
        Path.Combine(BuildManifestFolderPathRelative, ManifestFileName);

    private static string StoreCompatibleSrcFilePathAbsolute
    {
        get
        {

            var so = ScriptableObject.CreateInstance(typeof(OVRPluginInfo));
            var script = MonoScript.FromScriptableObject(so);
            string assetPath = AssetDatabase.GetAssetPath(script);
            string editorDir = Directory.GetParent(assetPath).FullName;
            return editorDir + "/AndroidManifest.OVRSubmission.xml";
        }
    }

    [MenuItem("Meta/Tools/Android Manifest Tool", false, 100000)]
    public static void OpenAndroidManifestToolWindow()
    {
        GetWindow(typeof(OVRManifestPreprocessor));
        OVRPlugin.SendEvent("manifest_processor", "activated");
    }

    private void OnGUI()
    {
        this.titleContent.text = "Android Manifest Tool";
        GUILayout.Label("Android Manifest Tool adds relevant Quest-only tags to AndroidManifest.xml "
            + "to ensure compatibility with the Meta Horizon Store. For details, see:"
            , EditorStyles.wordWrappedLabel);
        if (EditorGUILayout.LinkButton("Meta documentation"))
        {
            Application.OpenURL("https://developers.meta.com/horizon/resources/publish-mobile-manifest");
        }
        GUILayout.Space(15f);

        bool hasManifest = DoesAndroidManifestExist();


        if (hasManifest)
        {
            List<string> differenceFromShip = new List<string>();
            GetManifestDiff(StoreCompatibleSrcFilePathAbsolute, BuildManifestFilePathAbsolute, differenceFromShip);

            if (differenceFromShip.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Resetting AndroidManifest.xml will override any custom settings.\n" + string.Join("\n", differenceFromShip),
                    MessageType.Info, true);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Manifest has no changes from the default Store-Compatible manifest.",
                    MessageType.Info, true);
            }
            EditorGUI.BeginDisabledGroup(differenceFromShip.Count == 0);
            if (GUILayout.Button("Reset AndroidManifest.xml to default Store-Compatible version"))
            {
                GenerateManifestForSubmission();
            }
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            if (GUILayout.Button("Generate New Store-Compatible AndroidManifest.xml"))
            {
                GenerateManifestForSubmission();
            }
        }

        if (hasManifest)
        {
            List<string> differenceFromCurrent = new List<string>();
            GetManifestDiff(BuildManifestFilePathAbsolute, BuildManifestFilePathAbsolute, differenceFromCurrent);

            if (differenceFromCurrent.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Update will cause the following changes:\n" + string.Join("\n", differenceFromCurrent),
                    MessageType.Info, true);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "All settings changes for Store compatibility are already applied to AndroidManifest",
                    MessageType.Info, true);
            }
            EditorGUI.BeginDisabledGroup(differenceFromCurrent.Count == 0);
            if (GUILayout.Button("Update AndroidManifest.xml for Store Compatibility"))
            {
                UpdateAndroidManifest();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            if (GUILayout.Button("Remove Custom AndroidManifest.xml"))
            {
                RemoveAndroidManifest();
            }
        }
    }

    public static void GenerateManifestForSubmission()
    {
        GenerateManifestForSubmissionInternal(silentMode: false);
    }

    private static void GenerateManifestForSubmissionInternal(bool silentMode)
    {
        var srcFile = StoreCompatibleSrcFilePathAbsolute;

        if (!File.Exists(srcFile))
        {
            if (!silentMode)
            {
                IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-template-not-found",
                    "Cannot find Android manifest template for submission. Please delete the OVR folder and reimport the Oculus Utilities.");
            }
            return;
        }

        if (!silentMode && DoesAndroidManifestExist())
        {
            if (!EditorUtility.DisplayDialog("AndroidManifest.xml Already Exists!",
                    "Would you like to replace the existing manifest with a new one? All modifications will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }
        }

        // IO methods use absolute paths
        if (!Directory.Exists(ManifestFolderPathAbsolute))
            Directory.CreateDirectory(ManifestFolderPathAbsolute);

        PatchAndroidManifest(srcFile, BuildManifestFilePathAbsolute, false);
        AssetDatabase.Refresh();
        ShowAndroidManifestInProject();
    }

    public static bool DoesAndroidManifestExist()
    {
        // IO methods use absolute paths
        return File.Exists(BuildManifestFilePathAbsolute);
    }

    public static void GenerateOrUpdateAndroidManifest(bool silentMode)
    {
        if (DoesAndroidManifestExist())
        {
            UpdateAndroidManifestInternal(silentMode);
        }
        else
        {
            GenerateManifestForSubmissionInternal(silentMode);
        }
    }

    public static void UpdateAndroidManifest()
    {
        UpdateAndroidManifestInternal(silentMode: false);
    }

    private static void UpdateAndroidManifestInternal(bool silentMode)
    {
        if (!DoesAndroidManifestExist())
        {
            if (!silentMode)
            {
                IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-update-file-not-found",
                    "Unable to update manifest because it does not exist! Run \"Create store-compatible AndroidManifest.xml\" first");
            }
            return;
        }

        if (!silentMode && !EditorUtility.DisplayDialog("Update AndroidManifest.xml",
                "This will overwrite all Oculus specific AndroidManifest Settings. Continue?", "Overwrite", "Cancel"))
        {
            return;
        }

        PatchAndroidManifest(BuildManifestFilePathAbsolute, skipExistingAttributes: false);
        AssetDatabase.Refresh();
        ShowAndroidManifestInProject();
    }

    public static void RemoveAndroidManifest(bool silentMode = false)
    {
        if (!silentMode && !EditorUtility.DisplayDialog("Remove AndroidManifest.xml",
                "This will permanently delete existing AndroidManifest.xml. Continue?", "Delete", "Cancel"))
        {
            return;
        }

        // AssetDatabase functions uses relative paths
        AssetDatabase.DeleteAsset(BuildManifestFilePathRelative);
        AssetDatabase.Refresh();
    }

    private static void ShowAndroidManifestInProject()
    {
        if (!DoesAndroidManifestExist())
            return;

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(BuildManifestFilePathRelative);
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    private static void AddOrRemoveTag(XmlDocument doc, string @namespace, string path, string elementName, string name,
        bool required, bool modifyIfFound, string prefix = "", params string[] attrs) // name, value pairs
    {
        XmlElement element = null;
        XmlNodeList nodeList = null;
        string rootXmlPath = path;
        if (!string.IsNullOrEmpty(elementName))
        {
            rootXmlPath = path + "/" + elementName;
            nodeList = doc.SelectNodes(rootXmlPath);
        }
        else
        {
            var node = doc.SelectSingleNode(rootXmlPath);
            nodeList = node.ChildNodes;
        }

        foreach (XmlElement e in nodeList)
        {
            if (name == null || name == e.GetAttribute("name", @namespace) || name == e.LocalName)
            {
                element = e;
                break;
            }
        }

        if (required)
        {
            if (element == null)
            {
                var parent = doc.SelectSingleNode(path);
                if (string.IsNullOrEmpty(elementName))
                {
                    element = doc.CreateElement(prefix, name, @namespace);
                }
                else
                {
                    element = doc.CreateElement(elementName);
                    element.SetAttribute("name", @namespace, name);
                }
                parent.AppendChild(element);
            }

            for (int i = 0; i < attrs.Length; i += 2)
            {
                if (modifyIfFound || string.IsNullOrEmpty(element.GetAttribute(attrs[i], @namespace)))
                {
                    if (attrs[i + 1] != null)
                    {
                        element.SetAttribute(attrs[i], @namespace, attrs[i + 1]);
                    }
                    else
                    {
                        element.RemoveAttribute(attrs[i], @namespace);
                    }
                }
            }
        }
        else
        {
            if (element != null && modifyIfFound)
            {
                element.ParentNode.RemoveChild(element);
            }
        }
    }

    private static void AddReplaceValueTag(XmlDocument doc, string @namespace, string path, string elementName, string name)
    {
        XmlElement element = (XmlElement)doc.SelectSingleNode("/manifest");
        if (element == null)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-tag-not-found",
                "Could not find manifest tag in android manifest.");
            return;
        }

        string toolsNamespace = element.GetAttribute("xmlns:tools");
        if (string.IsNullOrEmpty(toolsNamespace))
        {
            toolsNamespace = "http://schemas.android.com/tools";
            element.SetAttribute("xmlns:tools", toolsNamespace);
        }

        var nodes = doc.SelectNodes(path + "/" + elementName);
        foreach (XmlElement e in nodes)
        {
            if (name == null || name == e.GetAttribute("name", @namespace))
            {
                element = e;
                break;
            }
        }

        element.SetAttribute("replace", toolsNamespace, "android:value");
    }

    private static void ApplyPrefixTag(XmlDocument doc, string @namespace, string path, string name, string prefix)
    {
        XmlElement element = (XmlElement)doc.SelectSingleNode("/manifest");
        if (element == null)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-prefix-tag-not-found",
                "Could not find manifest tag in android manifest.");
            return;
        }

        foreach (XmlElement e in element.ChildNodes)
        {
            if (name == e.Name)
            {
                var newElement = doc.CreateElement(prefix, name, @namespace);
                foreach (XmlAttribute a in e.Attributes)
                {
                    newElement.SetAttribute(a.LocalName, @namespace, a.Value);
                }
                element.RemoveChild(e);
                element.AppendChild(newElement);
            }
        }
    }

    public static XmlDocument GetAndroidManifestXmlDocument()
    {
        if (!DoesAndroidManifestExist())
        {
            return null;
        }

        try
        {
            var doc = new XmlDocument();
            doc.Load(BuildManifestFilePathAbsolute);
            return doc;
        }
        catch (System.Exception e)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-load-failed-v2", e, enableDebugLog: false);
            return null;
        }
    }

    private static XmlDocument LoadDocument(string sourceFile, bool skipAllModifications, bool modifyIfFound, bool enableSecurity, out string androidNamespaceURI)
    {
        // Load android manifest file
        var doc = new XmlDocument();
        doc.Load(sourceFile);

        androidNamespaceURI = null;
        XmlElement element = (XmlElement)doc.SelectSingleNode("/manifest");
        if (element == null)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-load-tag-not-found",
                "Could not find manifest tag in android manifest.");
            return null;
        }

        // Get android namespace URI from the manifest
        androidNamespaceURI = element.GetAttribute("xmlns:android");
        if (string.IsNullOrEmpty(androidNamespaceURI))
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-namespace-not-found",
                "Could not find Android Namespace in manifest.");
            return null;
        }

        if (skipAllModifications)
        {
            return doc;
        }

        // Add Horizon OS SDK namespace
        var horizonOsSdkNamespaceURI = "http://schemas.horizonos/sdk";
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (!projectConfig.horizonOsSdkDisabled)
        {
            var namespaceAttribute = element.GetAttribute("xmlns:horizonos");
            if (string.IsNullOrEmpty(namespaceAttribute))
            {
                element.SetAttribute("xmlns:horizonos", horizonOsSdkNamespaceURI);
            }
        }

#if UNITY_2023_2_OR_NEWER
            // replace UnityPlayerActivity to UnityPlayerGameActivity if GameActivity is selected in setting
            bool isUsingGameActivity = false;
            try
            {
                isUsingGameActivity = UnityEditor.PlayerSettings.Android.applicationEntry == UnityEditor.AndroidApplicationEntry.GameActivity;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to check PlayerSettings.Android.applicationEntry. Android Build Support may not be installed. Error: {e.Message}");
                isUsingGameActivity = false;
            }

            if (isUsingGameActivity)
            {
                XmlElement activityNode = doc.SelectSingleNode("/manifest/application/activity") as XmlElement;
                string activityName = activityNode.GetAttribute("name", androidNamespaceURI);
                if (activityName == "com.unity3d.player.UnityPlayerActivity")
                {
                    activityNode.SetAttribute("name", androidNamespaceURI, "com.unity3d.player.UnityPlayerGameActivity");
                }

                // use a Theme.AppCompat theme for the compatbility with UnityPlayerGameActivity
                string activityTheme = activityNode.GetAttribute("theme", androidNamespaceURI);
                if (activityTheme == "@android:style/Theme.Black.NoTitleBar.Fullscreen")
                {
                    activityNode.SetAttribute("theme", androidNamespaceURI, "@style/Theme.AppCompat.DayNight.NoActionBar");
                }
            }
#endif

        ApplyRequiredManifestTags(doc, androidNamespaceURI, modifyIfFound, enableSecurity);
        ApplyFeatureManifestTags(doc, androidNamespaceURI, modifyIfFound, horizonOsSdkNamespaceURI);

        // The following manifest entries are all handled through Oculus XR SDK Plugin
#if !PRIORITIZE_OCULUS_XR_SETTINGS
        ApplyOculusXRManifestTags(doc, androidNamespaceURI, modifyIfFound);
        ApplyTargetDevicesManifestTags(doc, androidNamespaceURI, true /*modifyIfFound*/);
#endif


        return doc;
    }

    public static void PatchAndroidManifest(string sourceFile, string destinationFile = null,
        bool skipExistingAttributes = true, bool enableSecurity = false)
    {
        if (destinationFile == null)
        {
            destinationFile = sourceFile;
        }

        bool modifyIfFound = !skipExistingAttributes;

        try
        {
            var doc = LoadDocument(sourceFile, skipAllModifications: false, modifyIfFound, enableSecurity, out _);
            if (doc == null)
            {
                return;
            }

            var settings = new XmlWriterSettings
            {
                NewLineChars = GetSuggestedLineEnding(sourceFile),
                Indent = true,
                Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };

            using (var writer = XmlWriter.Create(destinationFile, settings))
            {
                doc.Save(writer);
            }

            // noeol is an error to many linters. and POSIX.
            // due to internal encoder discrepancies, we must re-open the file in order to remedy noeol.
            using (var fileRW = new FileStream(destinationFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var reader = new StreamReader(fileRW, settings.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true))
            using (var writer = new StreamWriter(fileRW, settings.Encoding, bufferSize: 256, leaveOpen: false))
            {
                string compliantNewline = settings.NewLineChars.EndsWith('\n') ? settings.NewLineChars : "\n";

                fileRW.Seek(-1 * settings.Encoding.GetByteCount(compliantNewline), SeekOrigin.End);

                var buf = new char[compliantNewline.Length];
                _ = reader.ReadBlock(new System.Span<char>(buf));
                if (new string(buf) != compliantNewline)
                {
                    writer.NewLine = compliantNewline;
                    writer.Write(compliantNewline);
                }
            }
        }
        catch (System.Exception e)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-write-failed", e);
        }

        // see if the current file uses \r\n or \n by scanning the first line
        // the first line in the xml file is ~50 chars
        string GetSuggestedLineEnding(string filepath)
        {
            using var reader = File.OpenRead(filepath);
            var prevChar = 'a';
            for (var i = 0; i < 256; i++)
            {
                var cur = reader.ReadByte();
                if (cur == -1) break;
                var curChar = (char)cur;
                if (curChar == '\n')
                    return prevChar == '\r' ? "\r\n" : "\n";
                prevChar = curChar;
            }

            return EditorSettings.lineEndingsForNewScripts switch
            {
                LineEndingsMode.Unix => "\n",
                LineEndingsMode.Windows => "\r\n",
                _ => System.Environment.NewLine,
            };
        }
    }



    private static void ApplyRequiredManifestTags(XmlDocument doc, string androidNamespaceURI, bool modifyIfFound,
        bool enableSecurity)
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application/activity/intent-filter",
            "category",
            "android.intent.category.LEANBACK_LAUNCHER",
            required: false,
            modifyIfFound: true); // always remove leanback launcher

        // First add or remove headtracking flag if targeting Quest
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "android.hardware.vr.headtracking",
            OVRDeviceSelector.isTargetDeviceQuestFamily,
            true,
            prefix: "",
            "version", "1",
            "required", OVRProjectConfig.CachedProjectConfig.allowOptional3DofHeadTracking ? "false" : "true");

        // make sure android label and icon are set in the manifest
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "application",
            null,
            true,
            modifyIfFound,
            prefix: "",
            "label", "@string/app_name",
            "icon", "@mipmap/app_icon",
            // Disable allowBackup in manifest and add Android NSC XML file
            "allowBackup", projectConfig.disableBackups ? "false" : "true",
            "networkSecurityConfig", projectConfig.enableNSCConfig && enableSecurity ? "@xml/network_sec_config" : null
        );
    }

    private static void ApplyFeatureManifestTags(XmlDocument doc, string androidNamespaceURI, bool modifyIfFound, string horizonOsSdkNamespaceURI = "")
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();

        //============================================================================
        // Hand Tracking
        // If Quest is the target device, add the handtracking manifest tags if needed
        // Mapping of project setting to manifest setting:
        // OVRProjectConfig.HandTrackingSupport.ControllersOnly => manifest entry not present
        // OVRProjectConfig.HandTrackingSupport.ControllersAndHands => manifest entry present and required=false
        // OVRProjectConfig.HandTrackingSupport.HandsOnly => manifest entry present and required=true
        OVRProjectConfig.HandTrackingSupport targetHandTrackingSupport =
            OVRProjectConfig.CachedProjectConfig.handTrackingSupport;
        bool handTrackingEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                       (targetHandTrackingSupport !=
                                        OVRProjectConfig.HandTrackingSupport.ControllersOnly);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "oculus.software.handtracking",
            handTrackingEntryNeeded,
            modifyIfFound,
            prefix: "",
            "required",
            (targetHandTrackingSupport == OVRProjectConfig.HandTrackingSupport.HandsOnly) ? "true" : "false");
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.HAND_TRACKING",
            handTrackingEntryNeeded,
            modifyIfFound);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application",
            "meta-data",
            "com.oculus.handtracking.frequency",
            handTrackingEntryNeeded,
            modifyIfFound,
            prefix: "",
            "value", projectConfig.handTrackingFrequency.ToString());

        //============================================================================
        // System Keyboard
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "oculus.software.overlay_keyboard",
            projectConfig.requiresSystemKeyboard,
            modifyIfFound,
            prefix: "",
            "required", "false");

        //============================================================================
        // Experimental Features
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "com.oculus.experimental.enabled",
            projectConfig.experimentalFeaturesEnabled,
            modifyIfFound,
            prefix: "",
            "required", "true");

        //============================================================================
        // Anchor
        OVRProjectConfig.AnchorSupport targetAnchorSupport = OVRProjectConfig.CachedProjectConfig.anchorSupport;
        var sceneSupport = OVRProjectConfig.CachedProjectConfig.sceneSupport;
        bool anchorEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                 (targetAnchorSupport == OVRProjectConfig.AnchorSupport.Enabled ||
                                  sceneSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.USE_ANCHOR_API",
            anchorEntryNeeded,
            modifyIfFound);

        var targetSharedAnchorSupport = OVRProjectConfig.CachedProjectConfig.sharedAnchorSupport;
        bool sharedAnchorEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                       targetSharedAnchorSupport != OVRProjectConfig.FeatureSupport.None;

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.IMPORT_EXPORT_IOT_MAP_DATA",
            sharedAnchorEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Colocation Session
        var targetColocationSessionSupport = OVRProjectConfig.CachedProjectConfig.colocationSessionSupport;
        bool colocationSessionEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                         (targetColocationSessionSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.USE_COLOCATION_DISCOVERY_API",
            colocationSessionEntryNeeded,
            modifyIfFound);



        //============================================================================
        // Passthrough
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "com.oculus.feature.PASSTHROUGH",
            projectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None,
            modifyIfFound,
            prefix: "",
            "required", projectConfig.insightPassthroughSupport.ToRequiredAttributeValue());

        //============================================================================
        // System Splash Screen
        if (projectConfig.systemSplashScreen != null)
        {
            AddOrRemoveTag(doc,
                androidNamespaceURI,
                "/manifest/application",
                "meta-data",
                "com.oculus.ossplash",
                true,
                true /*modifyIfFound*/,
                prefix: "",
                "value",
                "true");
            AddOrRemoveTag(doc,
                androidNamespaceURI,
                "/manifest/application",
                "meta-data",
                "com.oculus.ossplash.type",
                true,
                true /*modifyIfFound*/,
                prefix: "",
                "value",
                projectConfig.systemSplashScreenType.ToManifestTag());
            AddOrRemoveTag(doc,
                androidNamespaceURI,
                "/manifest/application",
                "meta-data",
                "com.oculus.ossplash.colorspace",
                true,
                true /*modifyIfFound*/,
                prefix: "",
                "value",
                ColorSpaceToManifestTag(runtimeSettings.colorSpace));
        }

        // Contextual Passthrough
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application",
            "meta-data",
            "com.oculus.ossplash.background",
            required: true,
            true /*modifyIfFound*/,
            prefix: "",
            "value",
            projectConfig.systemLoadingScreenBackground == OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough
                ? "passthrough-contextual"
                : "black");

        //============================================================================
        // Render Model
        OVRProjectConfig.RenderModelSupport renderModelSupport = OVRProjectConfig.CachedProjectConfig.renderModelSupport;
        bool renderModelEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                      (renderModelSupport == OVRProjectConfig.RenderModelSupport.Enabled);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "com.oculus.feature.RENDER_MODEL",
            renderModelEntryNeeded,
            modifyIfFound);
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.RENDER_MODEL",
            renderModelEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Tracked Keyboard
        // If Quest is the target device, add the tracked keyboard manifest tags if needed
        // Mapping of project setting to manifest setting:
        // OVRProjectConfig.TrackedKeyboardSupport.None => manifest entry not present
        // OVRProjectConfig.TrackedKeyboardSupport.Supported => manifest entry present and required=false
        // OVRProjectConfig.TrackedKeyboardSupport.Required => manifest entry present and required=true
        OVRProjectConfig.TrackedKeyboardSupport targetTrackedKeyboardSupport =
            OVRProjectConfig.CachedProjectConfig.trackedKeyboardSupport;
        bool trackedKeyboardEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                          (targetTrackedKeyboardSupport !=
                                           OVRProjectConfig.TrackedKeyboardSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "oculus.software.trackedkeyboard",
            trackedKeyboardEntryNeeded,
            modifyIfFound,
            prefix: "",
            "required",
            (targetTrackedKeyboardSupport == OVRProjectConfig.TrackedKeyboardSupport.Required) ? "true" : "false");
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.TRACKED_KEYBOARD",
            trackedKeyboardEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Body Tracking
        // If Quest is the target device, add the bodytracking manifest tags if needed
        var targetBodyTrackingSupport = OVRProjectConfig.CachedProjectConfig.bodyTrackingSupport;
        bool bodyTrackingEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                       (targetBodyTrackingSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "com.oculus.software.body_tracking",
            bodyTrackingEntryNeeded,
            (targetBodyTrackingSupport == OVRProjectConfig.FeatureSupport.Required)
                ? true
                : modifyIfFound, // If Required, we should override the current entry
            prefix: "",
            "required", (targetBodyTrackingSupport == OVRProjectConfig.FeatureSupport.Required) ? "true" : "false");
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.BodyTracking),
            bodyTrackingEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Face Tracking
        var targetFaceTrackingSupport = OVRProjectConfig.CachedProjectConfig.faceTrackingSupport;
        bool faceTrackingEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                       (targetFaceTrackingSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "oculus.software.face_tracking",
            faceTrackingEntryNeeded,
            (targetFaceTrackingSupport == OVRProjectConfig.FeatureSupport.Required)
                ? true
                : modifyIfFound, // If Required, we should override the current entry
            prefix: "",
            "required", (targetFaceTrackingSupport == OVRProjectConfig.FeatureSupport.Required) ? "true" : "false");
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.FaceTracking),
            faceTrackingEntryNeeded,
            modifyIfFound);
        // Do not modify existing RECORD_AUDIO permission even when face tracking is not needed
        // (because it may be needed for other features)
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.RecordAudio),
            faceTrackingEntryNeeded, // audio recording for audio based face tracking
            modifyIfFound: false);

        //============================================================================
        // Eye Tracking
        var targetEyeTrackingSupport = OVRProjectConfig.CachedProjectConfig.eyeTrackingSupport;
#if USING_XR_SDK_OPENXR
        if (IsOpenXRLoaderActive())
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            if (settings != null)
            {
                var foveationFeature = settings.GetFeature<MetaXREyeTrackedFoveationFeature>();
                if (foveationFeature.enabled && targetEyeTrackingSupport == OVRProjectConfig.FeatureSupport.None)
                {
                    targetEyeTrackingSupport = OVRProjectConfig.FeatureSupport.Supported;
                }
            }
        }
#endif
        bool eyeTrackingEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                    (targetEyeTrackingSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "oculus.software.eye_tracking",
            eyeTrackingEntryNeeded,
            (targetEyeTrackingSupport == OVRProjectConfig.FeatureSupport.Required)
                ? true
                : modifyIfFound, // If Required, we should override the current entry
            prefix: "",
            "required", (targetEyeTrackingSupport == OVRProjectConfig.FeatureSupport.Required) ? "true" : "false");
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.EyeTracking),
            eyeTrackingEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Virtual Keyboard
        var virtualKeyboardSupport = OVRProjectConfig.CachedProjectConfig.virtualKeyboardSupport;
        bool virtualKeyboardEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                          (virtualKeyboardSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-feature",
            "com.oculus.feature.VIRTUAL_KEYBOARD",
            virtualKeyboardEntryNeeded,
            (virtualKeyboardSupport == OVRProjectConfig.FeatureSupport.Required)
                ? true
                : modifyIfFound, // If Required, we should override the current entry
            prefix: "",
            "required", (virtualKeyboardSupport == OVRProjectConfig.FeatureSupport.Required) ? "true" : "false");

        //============================================================================
        // Scene
        bool sceneEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                (sceneSupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.GetPermissionId(OVRPermissionsRequester.Permission.Scene),
            sceneEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Boundary Visibility
        var boundaryVisibilitySupport = OVRProjectConfig.CachedProjectConfig.boundaryVisibilitySupport;
        bool boundaryVisibilityEntryNeeded = OVRDeviceSelector.isTargetDeviceQuestFamily &&
                                (boundaryVisibilitySupport != OVRProjectConfig.FeatureSupport.None);

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            "com.oculus.permission.BOUNDARY_VISIBILITY",
            boundaryVisibilityEntryNeeded,
            modifyIfFound);

        //============================================================================
        // Processor Favor
        var processorFavor = OVRProjectConfig.CachedProjectConfig.processorFavor;
        bool tradeCpuForGpuAmountNeeded = processorFavor != OVRProjectConfig.ProcessorFavor.FavorEqually;

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application",
            "meta-data",
            "com.oculus.trade_cpu_for_gpu_amount",
            required: tradeCpuForGpuAmountNeeded,
            modifyIfFound: true,
            prefix: "",
            "value", ((int)processorFavor).ToString());

        //============================================================================
        // Telemetry Project GUID
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application",
            "meta-data",
            "com.oculus.telemetry.project_guid",
            required: true,
            modifyIfFound: true,
            prefix: "",
            "value",
            runtimeSettings.TelemetryProjectGuid);

        //============================================================================
        // Horizon OS SDK
        // If the store-compatible or any manifest override already has the horizon os sdk tag we need to re-add it with the proper
        // prefix and namespace because the prefix get stripped out during the Unity build process which then kills the gradle build.
        ApplyPrefixTag(doc, horizonOsSdkNamespaceURI, "", "uses-horizonos-sdk", "horizonos");
        if (!projectConfig.horizonOsSdkDisabled)
        {
            AddOrRemoveTag(doc,
                horizonOsSdkNamespaceURI,
                "/manifest",
                "",
                "uses-horizonos-sdk",
                required: true,
                modifyIfFound: true,
                prefix: "horizonos",
                "minSdkVersion",
                projectConfig.minHorizonOsSdkVersion.ToString(),
                "targetSdkVersion",
                projectConfig.targetHorizonOsSdkVersion.ToString());
        }

        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest",
            "uses-permission",
            OVRPermissionsRequester.PassthroughCameraAccessPermission,
            projectConfig.isPassthroughCameraAccessEnabled && projectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None,
            modifyIfFound);
    }



    private static void ApplyOculusXRManifestTags(XmlDocument doc, string androidNamespaceURI, bool modifyIfFound)
    {
        // Add focus aware tag if this app is targeting Quest Family
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application/activity",
            "meta-data",
            "com.oculus.vr.focusaware",
            OVRDeviceSelector.isTargetDeviceQuestFamily,
            modifyIfFound,
            prefix: "",
            "value", "true");

        // Add VR intent filter tag in the manifest
        AddOrRemoveTag(doc,
            androidNamespaceURI,
            "/manifest/application/activity/intent-filter",
            "category",
            "com.oculus.intent.category.VR",
            required: true,
            modifyIfFound: true);
    }

    private static void ApplyTargetDevicesManifestTags(XmlDocument doc, string androidNamespaceURI, bool modifyIfFound)
    {
        // Add support devices manifest according to the target devices
        if (OVRDeviceSelector.isTargetDeviceQuestFamily)
        {
            string targetDeviceValue = "";
            if (OVRDeviceSelector.isTargetDeviceQuest)
            {
                if (string.IsNullOrEmpty(targetDeviceValue))
                    targetDeviceValue = "quest";
                else
                    targetDeviceValue += "|quest";
            }
            if (OVRDeviceSelector.isTargetDeviceQuest2)
            {
                if (string.IsNullOrEmpty(targetDeviceValue))
                    targetDeviceValue = "quest2";
                else
                    targetDeviceValue += "|quest2";
            }
            if (OVRDeviceSelector.isTargetDeviceQuestPro)
            {
                if (string.IsNullOrEmpty(targetDeviceValue))
                    targetDeviceValue = "questpro";
                else
                    targetDeviceValue += "|questpro";
            }
            if (OVRDeviceSelector.isTargetDeviceQuest3)
            {
                if (string.IsNullOrEmpty(targetDeviceValue))
                    targetDeviceValue = "quest3";
                else
                    targetDeviceValue += "|quest3";
            }
            if (OVRDeviceSelector.isTargetDeviceQuest3S)
            {
                if (string.IsNullOrEmpty(targetDeviceValue))
                    targetDeviceValue = "quest3s";
                else
                    targetDeviceValue += "|quest3s";
            }
            if (string.IsNullOrEmpty(targetDeviceValue))
            {
                IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-empty-target-devices",
                    "Empty target devices");
            }

            AddOrRemoveTag(doc,
                androidNamespaceURI,
                "/manifest/application",
                "meta-data",
                "com.oculus.supportedDevices",
                true,
                modifyIfFound,
                prefix: "",
                "value", targetDeviceValue);

#if XR_MGMT_4_4_0_OR_NEWER && USING_XR_SDK_OPENXR
            // Fixes a manifest merge edge case where the supported devices tag collides with a cached version when using new XR Management manifest system
            AddReplaceValueTag(doc,
                androidNamespaceURI,
                "/manifest/application",
                "meta-data",
                "com.oculus.supportedDevices");
#endif
        }
    }

    private static Dictionary<string, XmlNode> GetNamedNodes(XmlNodeList nodeList, string androidNamespaceURI)
    {

        Dictionary<string, XmlNode> namedNodes = new Dictionary<string, XmlNode>();
        if (nodeList != null)
        {
            foreach (XmlNode node in nodeList)
            {
                var name = node.Attributes?["name", androidNamespaceURI];
                if (name != null)
                {
                    namedNodes[node.Name + "[\"" + name.Value + "\"]"] = node;
                }
                else
                {
                    namedNodes[node.Name] = node;
                }
            }
        }

        return namedNodes;
    }

    private static void CompareNodes(string prefix, XmlNodeList srcNodes, XmlNodeList dstNodes, string androidNamespaceURI,
        List<string> changes)
    {
        var namedSrcNodes = GetNamedNodes(srcNodes, androidNamespaceURI);
        var namedDstNodes = GetNamedNodes(dstNodes, androidNamespaceURI);

        foreach (var namedSrcNode in namedSrcNodes)
        {
            string name = namedSrcNode.Key;
            var srcNode = namedSrcNode.Value;
            if (namedDstNodes.TryGetValue(namedSrcNode.Key, out var dstNode))
            {
                if (srcNode.Attributes != null)
                {
                    // Compare all attributes
                    foreach (XmlAttribute attrib in srcNode.Attributes)
                    {
                        var dstAttrib = dstNode.Attributes?[attrib.LocalName, attrib.NamespaceURI];
                        if (dstAttrib == null)
                        {
                            changes.Add($" • {prefix}{name}: Add {attrib.Name}={attrib.Value}");
                        }
                        else if (dstAttrib.Value != attrib.Value)
                        {
                            changes.Add(
                                $" • {prefix}{name}: Change {attrib.Name} from {dstAttrib.Value} to {attrib.Value}");
                        }
                    }
                }

                if (dstNode.Attributes != null)
                {
                    foreach (XmlAttribute attrib in dstNode.Attributes)
                    {
                        var srcAttrib = srcNode.Attributes?[attrib.LocalName, attrib.NamespaceURI];
                        if (srcAttrib == null)
                        {
                            changes.Add($" • {prefix}{name}: Remove {attrib.Name}");
                        }
                    }
                }

                if (srcNode.HasChildNodes || dstNode.HasChildNodes)
                {
                    CompareNodes(prefix + srcNode.Name + "/", srcNode.ChildNodes, dstNode.ChildNodes, androidNamespaceURI, changes);
                }
            }
            else
            {
                changes.Add($" • Add {prefix}{name}");
                if (srcNode.HasChildNodes)
                {
                    CompareNodes(prefix + srcNode.Name + "/", srcNode.ChildNodes, null,
                        androidNamespaceURI, changes);
                }
            }
        }

        foreach (var namedDstNode in namedDstNodes)
        {
            string name = namedDstNode.Key;
            if (!namedSrcNodes.TryGetValue(namedDstNode.Key, out var srcNode))
            {
                changes.Add($" • Remove {prefix}{name}");
            }
        }
    }

    private static void GetManifestDiff(string sourceFile, string destinationFile, List<string> changes)
    {
        try
        {
            var srcDoc = LoadDocument(sourceFile, skipAllModifications: false, modifyIfFound: true, enableSecurity: false, out var androidNamespaceURI);
            var dstDoc = LoadDocument(destinationFile, skipAllModifications: true, modifyIfFound: false, enableSecurity: false, out _);

            CompareNodes("", srcDoc.ChildNodes, dstDoc.ChildNodes, androidNamespaceURI, changes);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to compare AndroidManifests!");
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-manifest-compare-failed", ex);
        }
    }

    private static bool IsOpenXRLoaderActive()
    {
#if USING_XR_SDK_OPENXR
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        if (settings.Manager.activeLoaders.Count > 0)
        {
            var openXRLoader = settings.Manager.activeLoaders[0] as OpenXRLoader;
            return openXRLoader != null;
        }
#endif
        return false;
    }

    private static string ColorSpaceToManifestTag(OVRManager.ColorSpace colorSpace)
    {
        switch (colorSpace)
        {
            case OVRManager.ColorSpace.Unmanaged:
                return "!Unmanaged";
            case OVRManager.ColorSpace.Rec_2020:
                return "Rec.2020";
            case OVRManager.ColorSpace.Rec_709:
                return "Rec.709";
            case OVRManager.ColorSpace.Rift_CV1:
                return "!RiftCV1";
            case OVRManager.ColorSpace.Rift_S:
                return "!RiftS";
            case OVRManager.ColorSpace.Quest:
                return "!Quest";
            case OVRManager.ColorSpace.P3:
                return "P3";
            case OVRManager.ColorSpace.Adobe_RGB:
                return "Adobe";
            default:
                return "";
        }
    }
}
