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

using Meta.XR.Editor.Id;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace Meta.XR.Editor.UserInterface
{
    internal class LinkLabel : IUserInterfaceItem
    {
        private VisualElement _visualElement;
        private readonly string _typography;
        private UnityEngine.UIElements.Label _uiLabel;

        public bool Hide { get; set; }
        public LinkDescription LinkDescription { get; }

        private readonly GUILayoutOption[] _options;

        public LinkLabel(GUIContent label, string url, IIdentified originData, params GUILayoutOption[] options) :
            this(new UrlLinkDescription()
            {
                Content = label,
                URL = url,
                Underline = true,
                Origin = Origins.GuidedSetup,
                OriginData = originData
            }, options)
        { }

        public LinkLabel(LinkDescription description, params GUILayoutOption[] options)
        {
            LinkDescription = description;
            _options = options;
        }

        public virtual void Draw() => LinkDescription.Draw();

        /// <summary>
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled clickable link label</returns>
        public VisualElement Get() => new IMGUIContainer(Draw);
    }
}
