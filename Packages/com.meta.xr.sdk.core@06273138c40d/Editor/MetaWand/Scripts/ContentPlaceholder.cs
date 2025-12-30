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
using System.Linq;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.RLDS.Styles;
using Button = Meta.XR.Editor.UserInterface.RLDS.Button;

namespace Meta.XR.MetaWand.Editor
{
    internal class ContentPlaceholder
    {
        public Texture PreviewImage { get; set; }
        public Action OnAddToSceneButton { get; set; }

        public Action OnErrorButton { get; set; }
        public Action OnTileClicked { get; set; }

        private string _errorMessage;
        private Color _backgroundColor = Styles.Colors.DarkerBackground;
        private ContentState _currentState = ContentState.Requesting;
        private ContentState _previousState = ContentState.None;
        private string _currentMessage;
        private bool _oscillateWithOffset;
        public string Id { get; }
        private bool _hover;
        private bool _clicked;
        private float _progress;
        private bool _showTryAgainButton;
        private int _leftPadding;
        private PromptHandler _promptHandler;
        private readonly Color _spectrumColor = new(1, 1, 1, 0.65f);
        private float[] _audioBarWaveforms;
        private int _spectrumAreaWidth;
        private float _maxBarHeight;
        private int _startX;
        private int _startY;
        private readonly Utils.GeneratorType _generator;
        private readonly bool _showLodsSelector;

        private readonly string[] _lodSizeSelection =
        {
            Constants.ModelLod0,
            Constants.ModelLod1,
            Constants.ModelLod2,
            Constants.ModelLod3
        };

        public int SelectedLod { get; private set; } = 1;

        public ContentPlaceholder(Utils.GeneratorType generatorType, bool showLodsSelector, string id = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            _generator = generatorType;
            _showLodsSelector = showLodsSelector;
        }


        public void SetPromptHandler(PromptHandler handler) => _promptHandler = handler;

        public void Draw(int containerSize)
        {
            var container = new GUIStyle(Styles.GUIStyles.SmallGrid)
            {
                fixedWidth = containerSize,
                fixedHeight = containerSize
            };

            var rect = EditorGUILayout.BeginVertical(container);
            {
                EditorGUILayout.Space();
            }

            switch (_currentState)
            {
                case ContentState.Requesting:
                case ContentState.Downloading:
                case ContentState.Saving:
                    ProcessingState(rect, containerSize);
                    break;
                case ContentState.Preview:
                    PreviewState(rect, containerSize, Editor.PreviewState.Preview);
                    break;
                case ContentState.Downloaded:
                case ContentState.Generated:
                    PreviewState(rect, containerSize, Editor.PreviewState.Generated);
                    break;
                case ContentState.Error:
                    PreviewState(rect, containerSize, Editor.PreviewState.Error);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void ThumbnailView(Rect rect, PreviewState previewState)
        {
            if (PreviewImage == null || previewState == Editor.PreviewState.Error)
            {
                return;
            }

            const int paddingXs = Spacing.SpaceXS;
            var placeholderRect = new Rect(rect.x + paddingXs, rect.y + paddingXs,
                rect.width - paddingXs * 2, rect.height - paddingXs * 2);
            GUI.DrawTexture(placeholderRect, PreviewImage, ScaleMode.ScaleAndCrop, false, 1, GUI.color, 0,
                Radius.RadiusSM);
        }

        private void PreviewState(Rect rect, int containerSize, PreviewState previewState)
        {
            Utils.DrawBorderedRectangle(rect);
            ThumbnailView(rect, previewState);

            _hover = HoverHelper.IsHover(Id, Event.current, rect);
            if (!_hover && Event.current.type == EventType.Repaint && previewState != Editor.PreviewState.Error)
            {
                return;
            }

            if (previewState == Editor.PreviewState.Error)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_currentMessage) ? "Something went wrong." : _currentMessage,
                    MessageType.Error);
            }


            new AddSpace(true).Draw();

            var padding = containerSize * 0.10f;
            var buttonWidth = Mathf.RoundToInt(containerSize - padding * 2);
            var label = previewState switch
            {
                Editor.PreviewState.Generated => "Add to scene",
                Editor.PreviewState.Error => "Try Again",
                _ => ""
            };
            var action = previewState switch
            {
                Editor.PreviewState.Generated => OnAddToSceneButton,
                Editor.PreviewState.Error => OnErrorButton,
                _ => () => { }
            };

            if (_showTryAgainButton)
            {
                if (previewState == Editor.PreviewState.Error || !_showLodsSelector)
                {
                    Utils.DrawCenterAligned(new Button(new ActionLinkDescription()
                    {
                        Content = new GUIContent(label),
                        Action = action
                    }, Styles.GUIStyles.TinyButton, buttonWidth));
                }
                else
                {
                    DrawLODSection((int)padding, action);
                }
            }

            new AddSpace(Spacing.Space2XS).Draw();

            _clicked = HoverHelper.Button(Id, rect, new GUIContent(""), GUIStyle.none, out _);
            if (_clicked)
            {
                OnTileClicked?.Invoke();
            }
        }

        private void DrawLODSection(int margin, Action addToSceneAction)
        {
            var style = new GUIStyle()
            {
                margin = new RectOffset(margin, margin, Spacing.Space4XS, Spacing.Space4XS)
            };
            var popupStyle = new GUIStyle(EditorStyles.popup)
            {
                fixedHeight = IconSize.SizeMD,
                margin = new RectOffset(0, Spacing.Space4XS, 0, Spacing.Space4XS)
            };
            EditorGUILayout.BeginHorizontal(style);
            SelectedLod = EditorGUILayout.Popup(SelectedLod, _lodSizeSelection, popupStyle);
            new Button(new ActionLinkDescription
            {
                Content = new GUIContent(XR.Editor.UserInterface.Styles.Contents.PlusIcon.Image, "Add to scene"),
                Action = addToSceneAction
            }, Styles.GUIStyles.TinyButton, IconSize.SizeMD).Draw();
            EditorGUILayout.EndHorizontal();
        }

        public void SetState(ContentState state, string overrideMessage = "", Texture image = null,
            bool showTryAgainButton = true)
        {
            _previousState = _currentState;
            _currentState = state;
            _currentMessage = state switch
            {
                ContentState.Requesting or ContentState.Downloading => state + "...",
                ContentState.Error => "Something went wrong.",
                _ => ""
            };

            if (!string.IsNullOrEmpty(overrideMessage))
            {
                _currentMessage = overrideMessage;
            }

            PreviewImage = image;
            _showTryAgainButton = showTryAgainButton;
            _progress = 0;
        }

        public ContentState GetState() => _currentState;
        public ContentState GetPreviousState() => _previousState;

        public void SetOscillationState(bool oscillateWithOffset) => _oscillateWithOffset = oscillateWithOffset;

        private void ProcessingState(Rect rect, int containerSize)
        {
            switch (_currentState)
            {
                case ContentState.Requesting:
                    DrawProcessing(rect);
                    break;
                case ContentState.Downloading:
                    Utils.DrawBorderedRectangle(rect);
                    DrawSpinnerInCenter(rect, containerSize);
                    DrawProgress(rect);
                    break;
                case ContentState.Saving:
                    Utils.DrawBorderedRectangle(rect);
                    DrawSpinnerInCenter(rect, containerSize);
                    break;
            }
        }

        private void DrawSpinnerInCenter(Rect rect, int gridSize)
        {
            var centerX = Mathf.RoundToInt((gridSize - Utils.SpinnerSize - Spacing.SpaceXS) * 0.5f);
            var centerY = Mathf.RoundToInt((gridSize - Utils.SpinnerSize) * 0.5f) - Spacing.SpaceMD;
            var style = new GUIStyle
            {
                margin = new RectOffset(centerX, 0, centerY, 0),
                fixedWidth = rect.width,
                fixedHeight = rect.height,
                stretchHeight = false
            };
            EditorGUILayout.BeginVertical(style);
            Utils.DrawSpinner();
            EditorGUILayout.EndVertical();
        }

        private float Oscillate(float frequency = 1f, float minRange = 0.7f, float maxRange = 1.0f)
        {
            var sineValue = _oscillateWithOffset
                ? Mathf.Cos((float)EditorApplication.timeSinceStartup * frequency * 2 * Mathf.PI)
                : Mathf.Sin((float)EditorApplication.timeSinceStartup * frequency * 2 * Mathf.PI);
            var normalizedValue = (sineValue + 1f) / 2f;
            return Mathf.Lerp(minRange, maxRange, normalizedValue);
        }

        private void DrawProgress(Rect rect)
        {
            var positionRect = new Rect(rect.x + Spacing.SpaceMD, rect.y + rect.height - Spacing.SpaceMD,
                rect.width - 2 * Spacing.SpaceMD, 4);
            try
            {
                EditorGUI.ProgressBar(positionRect, _progress, "");
            }
            catch (Exception)
            {
                return;
            }

            var gridStyle = new GUIStyle()
            {
                margin = new RectOffset(Spacing.SpaceMD, 0, 0, Spacing.SpaceMD),
                fixedWidth = rect.width,
                fixedHeight = rect.height
            };
            new AddSpace(true).Draw();
            EditorGUILayout.BeginHorizontal(gridStyle);
            var progressText = $"{_currentMessage} {Mathf.RoundToInt(_progress * 100)}%";
            new Label(progressText, Styles.GUIStyles.Body2TextTiny).Draw();
            EditorGUILayout.EndHorizontal();
        }

        // Glimmer effect
        private void DrawProcessing(Rect rect)
        {
            _backgroundColor.a = Oscillate();
            GUI.DrawTexture(rect, _backgroundColor.ToTexture(), ScaleMode.ScaleAndCrop, true, 1, GUI.color,
                0, Radius.RadiusSM);
        }

        public void UpdateProgress(float progress) => _progress = progress;
    }

    internal enum ContentState
    {
        Requesting, // API request started state
        Downloading, // preview image download state with progress
        Saving, // saving asset to project
        Preview, // showing preview
        Downloaded, // showing preview downloaded asset
        Generated, // showing preview for generated asset
        Error, // showing error message
        None
    }

    internal enum PreviewState
    {
        Preview, // Showing preview from initial query
        Generated,
        Error
    }
}
