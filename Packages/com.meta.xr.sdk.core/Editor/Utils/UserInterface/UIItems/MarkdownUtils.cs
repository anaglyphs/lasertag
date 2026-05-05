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
using System.Text.RegularExpressions;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    internal static class MarkdownUtils
    {
        public static IEnumerable<IUserInterfaceItem> GetGuideItemsForMarkdownText(string markdownText)
        {
            if (markdownText == null)
            {
                return Array.Empty<IUserInterfaceItem>();
            }

            markdownText = ConvertMarkdownToUnityRichText(markdownText);

            return SplitMarkdownInLines(markdownText)
                .Select(ParseMarkdownLine)
                .SelectMany(GetGuideItemsForMarkdownContent);
        }

        /// <summary>
        /// Parses markdown text and returns a <see cref="GroupedItem"/> containing all UI elements.
        /// Supports: headlines (#, ##, ###), bold text (**text**), code blocks (```...```),
        /// bullet points (- ), and omits note symbols (>).
        /// </summary>
        /// <param name="markdownText">The markdown text to parse</param>
        /// <returns>A GroupedItem containing all parsed UI elements</returns>
        public static GroupedItem ParseMarkdownToUserInterfaceItems(string markdownText)
        {
            var items = new List<IUserInterfaceItem>();

            if (string.IsNullOrEmpty(markdownText))
            {
                return new GroupedItem(items, Utils.UIItemPlacementType.Vertical);
            }

            var segments = ExtractCodeBlocksAndText(markdownText);

            foreach (var segment in segments)
            {
                if (segment.IsCodeBlock)
                {
                    items.Add(CreateCodeBlockItem(segment.Content, segment.Language));
                }
                else
                {
                    var textItems = ParseTextSegment(segment.Content);
                    items.AddRange(textItems);
                }
            }

            return new GroupedItem(items, Utils.UIItemPlacementType.Vertical);
        }

        private struct MarkdownSegment
        {
            public string Content { get; set; }
            public bool IsCodeBlock { get; set; }
            public string Language { get; set; }
        }

        private static List<MarkdownSegment> ExtractCodeBlocksAndText(string markdown)
        {
            var segments = new List<MarkdownSegment>();
            var codeBlockPattern = @"```(\w*)\r?\n([\s\S]*?)```";
            var matches = Regex.Matches(markdown, codeBlockPattern);

            var lastIndex = 0;
            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    var textBefore = markdown.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrWhiteSpace(textBefore))
                    {
                        segments.Add(new MarkdownSegment
                        {
                            Content = textBefore,
                            IsCodeBlock = false,
                            Language = null
                        });
                    }
                }

                segments.Add(new MarkdownSegment
                {
                    Content = match.Groups[2].Value.TrimEnd('\r', '\n'),
                    IsCodeBlock = true,
                    Language = match.Groups[1].Value
                });

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < markdown.Length)
            {
                var remainingText = markdown.Substring(lastIndex);
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    segments.Add(new MarkdownSegment
                    {
                        Content = remainingText,
                        IsCodeBlock = false,
                        Language = null
                    });
                }
            }

            return segments;
        }

        private static IUserInterfaceItem CreateCodeBlockItem(string code, string language)
        {
            return new CodeBlockItem(code, language);
        }

        private static List<IUserInterfaceItem> ParseTextSegment(string text)
        {
            var items = new List<IUserInterfaceItem>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    items.Add(new AddSpace(RLDS.Styles.Spacing.SpaceXS));
                    continue;
                }

                if (line.StartsWith(">"))
                {
                    line = line.Substring(1).TrimStart();
                }

                line = ConvertMarkdownToUnityRichText(line);

                if (line.StartsWith("### "))
                {
                    var headingText = line.Substring(4).Trim();
                    items.Add(new Label(headingText, Props.Typography.Heading4));
                }
                else if (line.StartsWith("## "))
                {
                    var headingText = line.Substring(3).Trim();
                    items.Add(new Label(headingText, Props.Typography.Heading3));
                }
                else if (line.StartsWith("# "))
                {
                    var headingText = line.Substring(2).Trim();
                    items.Add(new Label(headingText, Props.Typography.Heading2));
                }
                else if (line.StartsWith("-- "))
                {
                    var bulletText = "    ◦ " + line.Substring(3);
                    items.Add(new Label(bulletText, Props.Typography.Body1Text));
                }
                else if (line.StartsWith("- "))
                {
                    var bulletText = "• " + line.Substring(2);
                    items.Add(new Label(bulletText, Props.Typography.Body1Text));
                }
                else
                {
                    items.Add(new Label(line, Props.Typography.Body1Text));
                }
            }

            return items;
        }

        private struct Link
        {
            public string Content { get; set; }
            public string Url { get; set; }
        }

        private struct MarkdownContent
        {
            public string Text { get; set; }
            public IEnumerable<Link> Links { get; set; }
            public bool IsTitle { get; set; }
            public int TitleLevel { get; set; }
        }

        private static IEnumerable<IUserInterfaceItem> GetGuideItemsForMarkdownContent(MarkdownContent content)
        {
            if (!string.IsNullOrEmpty(content.Text))
            {
                if (content.IsTitle)
                {
                    // Use different styles or sizes for titles based on TitleLevel
                    var style = content.TitleLevel == 1
                        ? Styles.GUIStyles.TitleStyle
                        : Styles.GUIStyles.SubtitleStyle;
                    yield return new Label(content.Text, style);
                }
                else
                {
                    yield return new Label(content.Text, Styles.GUIStyles.RichTextStyle);
                }
            }

            foreach (var link in content.Links)
            {
                yield return new LinkLabel(new GUIContent(link.Content), link.Url, null);
                yield return new AddSpace(5);
            }
        }

        private static string CapitalizeFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return char.ToUpper(input[0]) + input.Substring(1);
        }

        private static MarkdownContent ParseMarkdownLine(string line)
        {
            var isTitle = false;
            var titleLevel = 0;
            if (line.StartsWith("# "))
            {
                isTitle = true;
                titleLevel = 1;
                line = line[2..].Trim();
            }
            else if (line.StartsWith("## "))
            {
                isTitle = true;
                titleLevel = 2;
                line = line[3..].Trim();
            }
            else if (line.StartsWith("- "))
            {
                line = "• " + line[2..];
            }

            var links = new List<Link>();
            const string pattern = @"\[(.*?)\]\((.*?)\)(\.)?";
            var originalLine = line;
            line = Regex.Replace(line, pattern, match =>
            {
                var trailingDot = match.Groups[3].Value;
                var link = new Link
                {
                    Content = CapitalizeFirstLetter(match.Groups[1].Value) + trailingDot,
                    Url = match.Groups[2].Value
                };
                links.Add(link);
                var matchEndIndex = match.Index + match.Length;
                if (matchEndIndex == originalLine.Length || originalLine[matchEndIndex] == '\n')
                {
                    // Erase the link from the string if it's at the end of a line
                    return string.Empty;
                }

                return link.Content + trailingDot;
            });
            return new MarkdownContent
            {
                Text = line,
                Links = links,
                IsTitle = isTitle,
                TitleLevel = titleLevel
            };
        }

        private static string ConvertMarkdownToUnityRichText(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            const string boldPattern = @"\*\*(.+?)\*\*";
            const string boldReplacement = "<b>$1</b>";
            input = Regex.Replace(input, boldPattern, boldReplacement);

            const string boldUnderscorePattern = @"__(.+?)__";
            const string boldUnderscoreReplacement = "<b>$1</b>";
            input = Regex.Replace(input, boldUnderscorePattern, boldUnderscoreReplacement);

            const string italicsPattern = @"(\*|_)(.+?)\1";
            const string italicsReplacement = "<i>$2</i>";
            input = Regex.Replace(input, italicsPattern, italicsReplacement);

            const string strikethroughPattern = @"~~(.+?)~~";
            const string strikethroughReplacement = "<s>$1</s>";
            input = Regex.Replace(input, strikethroughPattern, strikethroughReplacement);
            return input;
        }

        private static IEnumerable<string> SplitMarkdownInLines(string markdown)
        {
            var lines = markdown.Split('\n').ToList();

            // Remove leading empty lines
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count > 0)
            {
                lines[0] = lines[0].TrimStart();
            }

            // Remove trailing empty lines
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines;
        }
    }

    /// <summary>
    /// A UI item that displays a code block with monospace font and bordered box styling.
    /// </summary>
    internal class CodeBlockItem : IUserInterfaceItem
    {
        private readonly string _code;
        private readonly string _language;
        private VisualElement _visualElement;
        private const int ToastDisplayDurationMs = 2000;

        public bool Hide { get; set; }

        public CodeBlockItem(string code, string language = null)
        {
            _code = code;
            _language = language;
        }

        public void Draw()
        {
        }

        public VisualElement Get()
        {
            if (_visualElement != null)
            {
                return _visualElement;
            }

            _visualElement = new VisualElement();

            if (!string.IsNullOrEmpty(_language))
            {
                var languageLabel = new UnityEngine.UIElements.Label(_language);
                languageLabel.AddToClassList(Props.CodeBlock.Language);
                _visualElement.Add(languageLabel);
            }

            var codeContainer = new VisualElement();
            codeContainer.AddToClassList(Props.CodeBlock.Container);
            codeContainer.style.position = Position.Relative;

            var codeLabel = new UnityEngine.UIElements.Label(_code);
            codeLabel.AddToClassList(Props.CodeBlock.Label);

            // Try to load Inconsolata font, fall back to system monospace fonts
            var monoFont = Resources.Load<Font>("Inconsolata");
            if (monoFont != null)
            {
                codeLabel.style.unityFontDefinition = new StyleFontDefinition(monoFont);
            }
            else
            {
                // Fallback to system monospace font (Consolas on Windows, Menlo on Mac)
                codeLabel.style.unityFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Consolas", "Menlo", "Monaco", "Courier New" }, 12);
            }

            // Create toast notification (hidden by default)
            var toast = new UnityEngine.UIElements.Label("Code copied!");
            toast.AddToClassList(Props.Toast.Root);

            // Create copy button (hidden by default, shown on hover)
            var copyButton = new UnityEngine.UIElements.Button();
            copyButton.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = _code;

                // Show toast and hide after 2 seconds
                toast.style.display = DisplayStyle.Flex;
                copyButton.style.display = DisplayStyle.None;
                toast.schedule.Execute(() =>
                {
                    toast.style.display = DisplayStyle.None;
                }).ExecuteLater(ToastDisplayDurationMs);
            };
            copyButton.AddToClassList(Props.IconButton.Root);
            copyButton.AddToClassList(Props.IconButton.Absolute);
            copyButton.style.backgroundImage = new StyleBackground(Background.FromTexture2D(UIStyles.Contents.CopyIcon.Image as Texture2D));
            copyButton.tooltip = "Copy code";

            // Show/hide copy button on hover
            codeContainer.RegisterCallback<MouseEnterEvent>(evt =>
            {
                copyButton.style.display = DisplayStyle.Flex;
            });
            codeContainer.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                copyButton.style.display = DisplayStyle.None;
            });

            codeContainer.Add(codeLabel);
            codeContainer.Add(copyButton);
            codeContainer.Add(toast);
            _visualElement.Add(codeContainer);

            return _visualElement;
        }

    }
}
