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
using Meta.XR.Editor.Id;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class Page : GroupedItem, IIdentified
    {
        public string PageId { get; private set; }
        public Func<Page, bool> HasCompletedActionDelegate;
        public bool HasCompletedAction() => HasCompletedActionDelegate?.Invoke(this) ?? true;

        public Page(string pageId, IEnumerable<IUserInterfaceItem> items,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) : base(items, placementType, options)
        {
            PageId = pageId;
        }

        public Page(string pageId, IEnumerable<IUserInterfaceItem> items,
            GUIStyle style,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) : base(items, style, placementType, options)
        {
            PageId = pageId;
        }

        public string Id => PageId;
    }
}
