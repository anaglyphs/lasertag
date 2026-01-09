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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Feature(Feature.VirtualKeyboard)]
public class OVRVirtualKeyboardInputFieldTextHandler : OVRVirtualKeyboard.AbstractTextHandler
{
    /// <summary>
    /// Set an input field to connect to the Virtual Keyboard with the Unity Inspector
    /// </summary>
    [SerializeField]
    private InputField inputField;

    private bool _isSelected;

    /// <summary>
    /// Set/Get an input field to connect to the Virtual Keyboard at runtime
    /// </summary>
    public InputField InputField
    {
        get => inputField;
        set
        {
            if (value == inputField)
            {
                return;
            }
            if (inputField)
            {
                inputField.onValueChanged.RemoveListener(ProxyOnValueChanged);
            }
            inputField = value;
            if (inputField)
            {
                inputField.onValueChanged.AddListener(ProxyOnValueChanged);
            }
            OnTextChanged?.Invoke(Text);
        }
    }

    public override Action<string> OnTextChanged { get; set; }

    public override string Text => inputField ? inputField.text : string.Empty;

    public override bool SubmitOnEnter => inputField && inputField.lineType != InputField.LineType.MultiLineNewline;

    public override bool IsFocused => inputField && inputField.isFocused;

    public override void Submit()
    {
        if (!inputField)
        {
            return;
        }
        inputField.onEndEdit.Invoke(inputField.text);
    }

    public override void AppendText(string s)
    {
        if (!inputField)
        {
            return;
        }
        inputField.text += s;
    }

    public override void ApplyBackspace()
    {
        if (!inputField || string.IsNullOrEmpty(inputField.text))
        {
            return;
        }
        inputField.text = Text.Substring(0, Text.Length - 1);
    }

    public override void MoveTextEnd()
    {
        if (!inputField)
        {
            return;
        }
        inputField.MoveTextEnd(false);
    }

    protected void Start()
    {
        if (inputField)
        {
            inputField.onValueChanged.AddListener(ProxyOnValueChanged);
        }
    }

    protected void ProxyOnValueChanged(string arg0)
    {
        OnTextChanged?.Invoke(arg0);
    }
}
