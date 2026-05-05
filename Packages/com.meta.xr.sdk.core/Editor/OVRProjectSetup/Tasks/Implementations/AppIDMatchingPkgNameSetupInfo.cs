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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using UnityEngine;
using Styles = Meta.XR.Editor.UserInterface.Styles;
using Meta.XR.Guides.Editor;

[GuideItems]
internal class AppIDMatchingPkgNameSetupInfo : UPSTGuidedSetup
{
    private static GuideWindow _window;
    private const int ContentHeight = 420;

    [GuideItems]
    private List<IUserInterfaceItem> GetItems()
    {
        BulletedLabel Bullet(string text) => new BulletedLabel(text, UIStyles.ContentStatusType.Normal);
        return new List<IUserInterfaceItem>
        {
            new Label("If the application has not been created in Meta Horizon Dashboard:"),
            Bullet("Create the application from Meta Horizon Dashboard."),
            Bullet("Click <b>Development</b> from the left navigation."),
            Bullet("Click <b>API</b> under <b>Development</b>."),
            Bullet("You will find the <b>App ID</b> from the right panel. The App ID should be a string of digits."),
            Bullet("Copy the App ID."),
            new AddSpace(true),

            new Label("Set the application ID for the project:"),
            Bullet("Open your project in Unity."),
            Bullet("<b>Meta</b> -> <b>Platform</b> -> <b>Edit Settings</b>"),
            Bullet("Under <b>Oculus Platform Settings</b>, find the <b>Application ID</b> section."),
            Bullet("Enter the application ID for <b>Oculus Rift</b> if the project is building to Windows target."),
            Bullet("Enter the application ID for <b>Meta Quest</b> if the project is building to Android target."),
            new AddSpace(true),

            new Label("Confirm the match between the App ID and the package name if the application has been uploaded in Meta Horizon Dashboard:"),
            Bullet("Go to the Meta Horizon Dashboard."),
            Bullet("Go to left navigation."),
            Bullet("Click <b>Distribution</b>."),
            Bullet("Click <b>Builds</b>."),
            Bullet("Click the 1st(i.e., with the earlist <b>Uploaded</b> date) build from the list of builds."),
            Bullet("Select the <b>Details</b> tab."),
            Bullet("Get the package name from the <b>Package Name</b> section."),
            Bullet("Go to the Unity editor."),
            Bullet("Click <b>Edit</b> tab."),
            Bullet("Click <b>Project Settings...</b>."),
            Bullet("From the left navigation, click <b>Player</b>."),
            Bullet("From the right, select <b>Android settings</b> tab."),
            Bullet("Find the <b>Other Settings</b> section."),
            Bullet("Get the package name under the <b>Identification</b> section."),
            Bullet("The package name from Meta Horizon Dashboard must be same as the package name from Unity editor."),
        };
    }

    internal override GuideWindow CreateWindow()
    {
        if (_window != null) return _window;

        var title = "Setup Guide - Set up the app ID and the package name";
        string description =
            "The application ID must be copied from Meta Horizon Dashboard and" +
            "pasted to fill in the Unity editor. The package name for the app is" +
            "determined by the first upload to Meta Horizon Dashboard, and" +
            "subsequent upload to the Dashboard needs to use the same package name.";

        var options = new GuideWindow.GuideOptions(GuideWindow.DefaultOptions)
        {
            ShowDontShowAgainOption = false,
            MinWindowHeight = ContentHeight,
            MaxWindowHeight = ContentHeight,
            BottomMargin = 4,
        };
        _window = Guide.Create(title, description, this, options);
        return _window;
    }
}
