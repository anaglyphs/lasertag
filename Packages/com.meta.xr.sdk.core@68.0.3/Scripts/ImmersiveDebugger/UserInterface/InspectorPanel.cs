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


using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using Meta.XR.ImmersiveDebugger.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    internal class InspectorPanel : DebugPanel, IDebugUIPanel
    {
        public const string DefaultCategoryName = "Uncategorized";

        private ScrollView _scrollView;
        private Flex Flex => _scrollView.Flex;

        private ScrollView _categoryScrollView;
        private Flex CategoryFlex => _categoryScrollView.Flex;

        private readonly Dictionary<string, Dictionary<Type, Dictionary<InstanceHandle, Inspector>>>
            _registries = new();

        private readonly List<CategoryButton> _categories = new();
        private CategoryButton _selectedCategory = null;

        private Background _categoryBackground;

        private Vector3 _currentPosition;
        private Vector3 _targetPosition;
        private readonly float _lerpSpeed = 10f;
        private bool _lerpCompleted = true;

        public ImageStyle CategoryBackgroundStyle
        {
            set
            {
                _categoryBackground.Sprite = value.sprite;
                _categoryBackground.Color = value.color;
                _categoryBackground.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            var div = Append<Flex>("div");
            div.LayoutStyle = Style.Load<LayoutStyle>("InspectorDivFlex");

            var categoryDiv = div.Append<Flex>("categories_div");
            categoryDiv.LayoutStyle = Style.Load<LayoutStyle>("CategoriesDiv");

            // Left Tab Background
            _categoryBackground = categoryDiv.Append<Background>("background");
            _categoryBackground.LayoutStyle = Style.Load<LayoutStyle>("CategoriesDivBackground");
            CategoryBackgroundStyle = Style.Load<ImageStyle>("CategoriesDivBackground");

            _categoryScrollView = categoryDiv.Append<ScrollView>("categories");
            _categoryScrollView.LayoutStyle = Style.Load<LayoutStyle>("CategoriesScrollView");
            CategoryFlex.LayoutStyle = Style.Load<LayoutStyle>("InspectorCategoryFlex");

            _scrollView = div.Append<ScrollView>("main");
            _scrollView.LayoutStyle = Style.Load<LayoutStyle>("PanelScrollView");
            Flex.LayoutStyle = Style.Load<LayoutStyle>("InspectorMainFlex");

            // Initialize the Uncategorized category
            GetCategoryButton(null, true);
        }

        public IInspector RegisterInspector(InstanceHandle instanceHandle, string category)
        {
            var inspector = GetInspectorInternal(instanceHandle, category, true, out var registry);
            if (inspector == null)
            {
                var button = GetCategoryButton(category, true);
                button.Counter++;

                var previousScroll = _scrollView.Progress;

                var instance = instanceHandle.Instance;
                var inspectorName = instance != null ? instance.name : instanceHandle.Type.Name;
                inspector = Flex.Append<Inspector>(inspectorName);
                inspector.LayoutStyle = Style.Load<LayoutStyle>("Inspector");
                var inspectorTitle = instance != null ? $"{instance.name} - {instanceHandle.Type.Name}" : $"{instanceHandle.Type.Name}";
                inspector.Title = inspectorTitle;
                registry.Add(instanceHandle, inspector);

                _scrollView.Progress = previousScroll;

                if (_selectedCategory != button)
                {
                    Flex.Forget(inspector);
                }
            }

            return inspector;
        }

        public void UnregisterInspector(InstanceHandle instanceHandle, string category, bool allCategories)
        {
            if (allCategories)
            {
                foreach (var (_, categoryRegistry) in _registries)
                {
                    if (categoryRegistry.TryGetValue(instanceHandle.Type, out var typeRegistry))
                    {
                        if (typeRegistry.TryGetValue(instanceHandle, out var inspector))
                        {
                            typeRegistry.Remove(instanceHandle);
                        }
                    }
                }
            }
            else
            {
                var inspector = GetInspectorInternal(instanceHandle, category, false, out var registry); ;
                if (inspector != null)
                {
                    var previousScroll = _scrollView.Progress;

                    // Unregister the inspector
                    registry?.Remove(instanceHandle);

                    // Destroy the inspector
                    Flex.Remove(inspector, true);
                    var button = GetCategoryButton(category, false);
                    if (button != null)
                    {
                        button.Counter--;
                    }

                    _scrollView.Progress = previousScroll;
                }
            }
        }

        public IInspector GetInspector(InstanceHandle instanceHandle, string category)
            => GetInspectorInternal(instanceHandle, category, false, out _);

        public Inspector GetInspectorInternal(InstanceHandle instanceHandle, string category, bool createRegistries, out Dictionary<InstanceHandle, Inspector> registry)
        {
            category ??= string.Empty;
            Inspector inspector = null;
            if (!_registries.TryGetValue(category, out var categoryRegistry))
            {
                if (createRegistries)
                {
                    categoryRegistry = new Dictionary<Type, Dictionary<InstanceHandle, Inspector>>();
                    _registries.Add(category, categoryRegistry);
                }
                else
                {
                    registry = null;
                    return inspector;
                }
            }

            if (!categoryRegistry.TryGetValue(instanceHandle.Type, out registry))
            {
                if (createRegistries)
                {
                    registry = new Dictionary<InstanceHandle, Inspector>();
                    categoryRegistry.Add(instanceHandle.Type, registry);
                }
                else
                {
                    return inspector;
                }
            }

            registry.TryGetValue(instanceHandle, out inspector);
            return inspector;
        }

        private CategoryButton GetCategoryButton(string category, bool create = false)
        {
            category = string.IsNullOrEmpty(category) ? DefaultCategoryName : category;
            foreach (var button in _categories)
            {
                if (button.CategoryName == category)
                {
                    return button;
                }
            }

            if (create)
            {
                var button = CategoryFlex.Append<CategoryButton>(category);
                button.LayoutStyle = Style.Instantiate<LayoutStyle>("CategoryButton");
                button.CategoryName = category;
                button.Callback = () => SelectCategory(button);
                _categories.Add(button);

                if (_selectedCategory == null)
                {
                    SelectCategory(button);
                }

                return button;
            }

            return null;
        }

        private void SelectCategory(CategoryButton category)
        {
            if (_selectedCategory == category) return;

            if (_selectedCategory != null)
            {
                _selectedCategory.State = false;

                Flex.ForgetAll();
            }

            _selectedCategory = category;

            if (_selectedCategory != null)
            {
                _selectedCategory.State = true;

                var categoryName = category.CategoryName;
                if (_registries.TryGetValue(categoryName == DefaultCategoryName ? string.Empty : categoryName, out var categoryRegistry))
                {
                    foreach (var typeRegistry in categoryRegistry)
                    {
                        foreach (var inspector in typeRegistry.Value)
                        {
                            Flex.Remember(inspector.Value);
                        }
                    }
                }
            }

            _scrollView.Progress = 1.0f;
        }

        internal void SetPanelPosition(RuntimeSettings.DistanceOption distanceOption, bool skipAnimation = false)
        {
            _targetPosition = distanceOption switch
            {
                RuntimeSettings.DistanceOption.Close => Utils.InspectorsPanelClosePosition,
                RuntimeSettings.DistanceOption.Far => Utils.InspectorsPanelFarPosition,
                _ => Utils.InspectorsPanelDefaultPosition
            };

            if (skipAnimation)
            {
                SphericalCoordinates = _targetPosition;
                _currentPosition = _targetPosition;
                return;
            }

            _lerpCompleted = false;
        }

        private void Update()
        {
            if (_lerpCompleted) return;
            _currentPosition = Utils.LerpPosition(_currentPosition, _targetPosition, _lerpSpeed);
            _lerpCompleted = _currentPosition == _targetPosition;
            SphericalCoordinates = _currentPosition;
        }
    }
}

