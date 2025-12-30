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
internal class DataUseCheckupManualSetupInfo : UPSTGuidedSetup
{
    private static GuideWindow _window;
    private const int ContentHeight = 300;
    private const string _whenToSubmitUrl = "https://developers.meta.com/horizon/resources/publish-data-use#when-to-submit-a-duc";
    private const string _howToSubmitUrl = "https://developers.meta.com/horizon/resources/publish-data-use#submitting-a-duc";
    private const string _devAppWhileWaitingUrl = "https://developers.meta.com/horizon/resources/publish-data-use#how-to-develop-apps-while-waiting-for-duc-approvals";
    private const string _ducFeatureRefUrl = "https://developers.meta.com/horizon/resources/publish-data-use#duc-feature-reference";

    [GuideItems]
    private List<IUserInterfaceItem> GetItems()
    {
        return new List<IUserInterfaceItem>
        {
            new LinkLabel(new GUIContent("When to submit a DUC"), _whenToSubmitUrl, _window),
            new LinkLabel(new GUIContent("How to submit a DUC"), _howToSubmitUrl, _window),
            new LinkLabel(new GUIContent("How to develop apps while waiting for DUC approvals"), _devAppWhileWaitingUrl, _window),
            new LinkLabel(new GUIContent("DUC feature reference"), _ducFeatureRefUrl, _window),
        };
    }

    internal override GuideWindow CreateWindow()
    {
        if (_window != null) return _window;

        var title = "Setup Guide - Complete a Data Use Checkup";
        var description = "Data Use Checkup (DUC) is a tool in the Developer Dashboard that helps protects the data and privacy of our users. It requires an admin from your organization to affirm that your API access to certain Platform SDK features (https://developers.meta.com/horizon/resources/publish-data-use#table) complies with the Developer Data Use Policy (DDUP).";
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
