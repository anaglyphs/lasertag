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
using Meta.XR.ImmersiveDebugger.Hierarchy;
using Meta.XR.ImmersiveDebugger.Manager;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    internal class InspectorPanel : DebugPanel, IDebugUIPanel
    {
        private ScrollView _scrollView;
        private Flex Flex => _scrollView.Flex;
        internal ScrollView ScrollView => _scrollView;

        private ScrollView _categoryScrollView;
        private Flex CategoryFlex => _categoryScrollView.Flex;

        private ScrollView _hierarchyScrollView;
        private Flex HierarchyFlex => _hierarchyScrollView.Flex;

        private readonly Dictionary<Category, Dictionary<Type, Dictionary<InstanceHandle, Inspector>>>
            _registries = new();

        private readonly Dictionary<Category, CategoryButton> _categories = new();
        private readonly Dictionary<Item, HierarchyItemButton> _items = new();

        private CategoryButton _selectedCategory;
        private HierarchyItemButton _selectedItem;

        private Background _categoryBackground;

        private Vector3 _currentPosition;
        private Vector3 _targetPosition;
        private readonly float _lerpSpeed = 10f;
        private bool _lerpCompleted = true;
        private ImageStyle _categoryBackgroundImageStyle;
        private DebugInterface _debugInterface;
        private Flex _buttonsAnchor;
        private Label _selectedModeTitle;
        private Toggle _hierarchyIcon;
        private Toggle _categoriesIcon;
        private Flex _categoryDiv;

        // Throttle hierarchy refresh (minimal cadence control)
        private readonly int _refreshEveryN = 10;
        private int _refreshTick;

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

#if UNITY_2022_1_OR_NEWER
            _debugInterface = FindFirstObjectByType<DebugInterface>();
#else
            _debugInterface = FindObjectOfType<DebugInterface>();
#endif

            var div = Append<Flex>("div");
            div.LayoutStyle = Style.Load<LayoutStyle>("InspectorDivFlex");

            _categoryDiv = div.Append<Flex>("categories_div");
            _categoryDiv.LayoutStyle = Style.Load<LayoutStyle>("CategoriesDiv");

            // Left Tab Background
            _categoryBackground = _categoryDiv.Append<Background>("background");
            _categoryBackground.LayoutStyle = Style.Load<LayoutStyle>("CategoriesDivBackground");
            _categoryBackgroundImageStyle = Style.Load<ImageStyle>("CategoriesDivBackground");
            CategoryBackgroundStyle = _categoryBackgroundImageStyle;

            // Flex
            _buttonsAnchor = _categoryDiv.Append<Flex>("header");
            _buttonsAnchor.LayoutStyle = Style.Load<LayoutStyle>("ConsoleButtons");

            // Add Icons to switch mode
            _hierarchyIcon = RegisterControl("Hierarchy",
                Resources.Load<Texture2D>("Textures/hierarchy_icon"),
                Style.Load<ImageStyle>("InspectorModeIcon"),
                SelectHierarchyMode);
            _categoriesIcon = RegisterControl("Categories",
                Resources.Load<Texture2D>("Textures/categories_icon"),
                Style.Load<ImageStyle>("InspectorModeIcon"),
                SelectCategoryMode);

            // Title
            _selectedModeTitle = _buttonsAnchor.Append<Label>("title");
            _selectedModeTitle.LayoutStyle = Style.Load<LayoutStyle>("InspectorModeTitle");
            _selectedModeTitle.TextStyle = Style.Load<TextStyle>("MemberTitle");

            // Category Scroll View
            _categoryScrollView = _categoryDiv.Append<ScrollView>("categories");
            _categoryScrollView.LayoutStyle = Style.Load<LayoutStyle>("CategoriesScrollView");
            CategoryFlex.LayoutStyle = Style.Load<LayoutStyle>("InspectorCategoryFlex");

            // Hierarchy Scroll View
            _hierarchyScrollView = _categoryDiv.Append<ScrollView>("categories");
            _hierarchyScrollView.LayoutStyle = Style.Load<LayoutStyle>("CategoriesScrollView");
            HierarchyFlex.LayoutStyle = Style.Load<LayoutStyle>("InspectorCategoryFlex");

            // Main scroll view
            _scrollView = div.Append<ScrollView>("main");
            _scrollView.LayoutStyle = Style.Load<LayoutStyle>("PanelScrollView");
            Flex.LayoutStyle = Style.Load<LayoutStyle>("InspectorMainFlex");

            SelectCategoryMode();
        }

        private Toggle RegisterControl(string buttonName, Texture2D icon, ImageStyle style, Action callback)
        {
            if (buttonName == null) throw new ArgumentNullException(nameof(buttonName));
            if (icon == null) throw new ArgumentNullException(nameof(icon));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var toggle = _buttonsAnchor.Append<Toggle>(buttonName);
            toggle.LayoutStyle = Style.Load<LayoutStyle>("ConsoleButton");
            toggle.Icon = icon;
            toggle.IconStyle = style ? style : Style.Default<ImageStyle>();
            toggle.Callback = callback;
            return toggle;
        }

        private void SelectCategoryMode()
        {
            _selectedModeTitle.Content = "Custom Inspectors";
            if (_categoryDiv)
            {
                if (_hierarchyScrollView)
                {
                    _categoryDiv.Forget(_hierarchyScrollView);
                }

                if (_categoryScrollView)
                {
                    _categoryDiv.Remember(_categoryScrollView);
                }
            }

            if (_categoriesIcon)
            {
                _categoriesIcon.State = true;
            }

            if (_hierarchyIcon)
            {
                _hierarchyIcon.State = false;
            }
        }

        private void SelectHierarchyMode()
        {
            _selectedModeTitle.Content = "Hierarchy View";
            if (_categoryDiv != null)
            {
                if (_categoryScrollView != null)
                {
                    _categoryDiv.Forget(_categoryScrollView);
                }

                if (_hierarchyScrollView != null)
                {
                    _categoryDiv.Remember(_hierarchyScrollView);
                }
            }

            if (_hierarchyIcon != null)
            {
                _hierarchyIcon.State = true;
            }

            if (_categoriesIcon != null)
            {
                _categoriesIcon.State = false;
            }
        }

        protected override void OnTransparencyChanged()
        {
            base.OnTransparencyChanged();
            if (_categoryBackground && _categoryBackgroundImageStyle)
            {
                _categoryBackground.Color = Transparent
                    ? _categoryBackgroundImageStyle.colorOff
                    : _categoryBackgroundImageStyle.color;
            }
        }

        public IInspector RegisterInspector(InstanceHandle instanceHandle, Category category)
        {
            if (instanceHandle.Instance != null && instanceHandle.Instance is not Component)
            {
                // Special case for when the instance is actually not a component
                // This can happen when it's the GameObject or a Scene, coming from the hierarchy view.
                // In this case, we want to still create the hierarchy item button if needed,
                // but we actually won't register an inspector.
                var button = GetHierarchyItemButton(category.Item, true);
                if (button)
                {
                    button.Counter++;
                }

                return null;
            }

            var inspector = GetInspectorInternal(instanceHandle, category, true, out var registry);
            if (inspector != null) return inspector;

            var previousScroll = _scrollView ? _scrollView.Progress : 1f;

            var instance = instanceHandle.Instance;
            var inspectorName = instance != null ? instance.name : instanceHandle.Type.Name;

            if (!_scrollView)
            {
                return null;
            }

            inspector = Flex.Append<Inspector>(inspectorName);
            inspector.LayoutStyle = Style.Load<LayoutStyle>("Inspector");
            inspector.InstanceHandle = instanceHandle;

            registry.Add(instanceHandle, inspector);

            _scrollView.Progress = previousScroll;

            if (category.Item != null)
            {
                inspector.Foldout.State = false;

                var button = GetHierarchyItemButton(category.Item, true);
                if (!button)
                {
                    return inspector;
                }

                button.Counter++;

                if (!_hierarchyScrollView || !_hierarchyScrollView.Visibility || _selectedItem != button)
                {
                    Flex.Forget(inspector);
                }
            }
            else
            {
                var button = GetCategoryButton(category, true);
                if (!button)
                {
                    return inspector;
                }

                button.Counter++;

                if ((!_categoryScrollView || !_categoryScrollView.Visibility) || _selectedCategory != button)
                {
                    Flex.Forget(inspector);
                }
            }

            return inspector;
        }

        public void UnregisterInspector(InstanceHandle instanceHandle, Category category, bool allCategories)
        {
            if (allCategories)
            {
                foreach (var (otherCategory, categoryRegistry) in _registries)
                {
                    if (!categoryRegistry.TryGetValue(instanceHandle.Type, out var typeRegistry))
                    {
                        continue;
                    }

                    if (!typeRegistry.Remove(instanceHandle, out var inspector))
                    {
                        continue;
                    }

                    RemoveInspector(otherCategory, inspector);
                }
            }
            else
            {
                if (instanceHandle.Instance is not Component)
                {
                    // Special case for when the instance is actually not a component
                    // This can happen when it's the GameObject or a Scene, coming from the hierarchy view.
                    // In this case, we don't have an inspector to remove
                    // but we still want to remove the hierarchy item button if necessary.
                    TryRemoveHierarchyItemButton(category.Item);
                    return;
                }

                var inspector = GetInspectorInternal(instanceHandle, category, false, out var registry);
                if (!inspector)
                {
                    return;
                }

                registry?.Remove(instanceHandle);
                RemoveInspector(category, inspector);
            }
        }

        private void RemoveInspector(Category category, Inspector inspector)
        {
            var previousScroll = _scrollView ? _scrollView.Progress : 1f;

            if (_scrollView)
            {
                Flex.Remove(inspector, true);
                _scrollView.Progress = previousScroll;
            }

            if (category.Item != null)
            {
                TryRemoveHierarchyItemButton(category.Item);
            }
            else
            {
                var button = GetCategoryButton(category);
                if (button)
                {
                    button.Counter--;
                }
            }
        }

        public IInspector GetInspector(InstanceHandle instanceHandle, Category category)
            => GetInspectorInternal(instanceHandle, category, false, out _);

        private Inspector GetInspectorInternal(InstanceHandle instanceHandle, Category category, bool createRegistries,
            out Dictionary<InstanceHandle, Inspector> registry)
        {
            if (!_registries.TryGetValue(category, out var categoryRegistry) && createRegistries)
            {
                categoryRegistry = new Dictionary<Type, Dictionary<InstanceHandle, Inspector>>();
                _registries.Add(category, categoryRegistry);
            }

            if (categoryRegistry == null)
            {
                registry = null;
                return null;
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
                    return null;
                }
            }

            registry.TryGetValue(instanceHandle, out var inspector);
            return inspector;
        }

        internal CategoryButton GetCategoryButton(Category category, bool create = false)
        {
            if (_categories.TryGetValue(category, out var button) || !create)
            {
                return button;
            }

            if (!_categoryScrollView)
            {
                return null;
            }

            button = CategoryFlex.Append<CategoryButton>(category.Id);
            button.LayoutStyle = Style.Instantiate<LayoutStyle>("CategoryButton");
            button.Category = category;
            button.Callback = () => SelectCategoryButton(button);
            _categories.Add(category, button);

            if (!_selectedCategory)
            {
                SelectCategoryButton(button);
            }

            return button;
        }

        private Controller ComputeIdealPreviousItem(Item item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.Parent is null or SceneRegistry)
            {
                return null;
            }

            var parent = GetHierarchyItemButton(item.Parent, true);
            if (!_hierarchyScrollView)
            {
                return null;
            }

            Controller idealPreviousItem = null;
            foreach (var child in HierarchyFlex.Children)
            {
                if (child is not HierarchyItemButton childButton)
                {
                    continue;
                }

                if (childButton.Item.Parent == item.Parent || childButton == parent)
                {
                    idealPreviousItem = childButton;
                }
                else if (idealPreviousItem)
                {
                    break;
                }
            }

            return idealPreviousItem;
        }

        private HierarchyItemButton GetHierarchyItemButton(Item item, bool create = false)
        {
            if (item == null || _items == null)
            {
                return null;
            }

            if (_items.TryGetValue(item, out var button) || !create)
            {
                return button;
            }

            if (!_hierarchyScrollView)
            {
                return null;
            }

            var idealPreviousItem = ComputeIdealPreviousItem(item);
            button = idealPreviousItem
                ? HierarchyFlex.InsertAfter<HierarchyItemButton>(item.Label, idealPreviousItem)
                : HierarchyFlex.Append<HierarchyItemButton>(item.Label);

            button.LayoutStyle = Style.Instantiate<LayoutStyle>(nameof(HierarchyItemButton));
            button.Item = item;
            button.LayoutStyle.SetIndent((item.Depth - 1) * 10);
            button.LayoutStyle.SetWidth(button.LayoutStyle.size.x - item.Depth * 10);
            button.Label.Callback = () => SelectHierarchyItemButton(button);
            button.Foldout.Callback = () => ToggleFoldItem(button);
            _items[item] = button;
            return button;
        }

        private void TryRemoveHierarchyItemButton(Item item)
        {
            if (item == null || _items == null)
            {
                return;
            }

            if (!_items.TryGetValue(item, out var button) || !button)
            {
                return;
            }

            button.Counter--;
            if (button.Counter != 0)
            {
                return;
            }

            if (_items.Remove(item, out var btn) && _hierarchyScrollView)
            {
                HierarchyFlex.Remove(btn, true);
            }
        }

        internal void SelectCategoryButton(CategoryButton categoryButton)
        {
            if (_selectedCategory == categoryButton)
            {
                return;
            }

            SelectHierarchyItemButton(null);

            if (_scrollView)
            {
                Flex.ForgetAll();
            }

            if (_selectedCategory)
            {
                _selectedCategory.State = false;
            }

            _selectedCategory = categoryButton;

            if (_selectedCategory)
            {
                _selectedCategory.State = true;
                SelectCategory(categoryButton.Category);
            }

            if (_scrollView)
            {
                _scrollView.Progress = 1.0f;
            }
        }

        private void SelectCategory(Category category)
        {
            if (!_registries.TryGetValue(category, out var categoryRegistry))
            {
                return;
            }

            foreach (var typeRegistry in categoryRegistry)
            {
                foreach (var inspector in typeRegistry.Value)
                {
                    if (_scrollView)
                    {
                        Flex.Remember(inspector.Value);
                    }

                    if (_debugInterface)
                    {
                        _debugInterface.SetTransparencyRecursive(inspector.Value, !_debugInterface.OpacityOverride);
                    }
                }
            }
        }

        private static void ToggleFoldItem(HierarchyItemButton button)
        {
            if (button == null) return;

            if (button.Foldout.State)
            {
                FoldItem(button);
            }
            else
            {
                UnfoldItem(button);
            }
        }

        private static void FoldItem(HierarchyItemButton button)
        {
            if (button)
            {
                button.Foldout.State = false;
            }
        }

        private static void UnfoldItem(HierarchyItemButton button)
        {
            if (button)
            {
                button.Foldout.State = true;
            }
        }

        private void SelectHierarchyItemButton(HierarchyItemButton button)
        {
            if (_selectedItem == button)
            {
                ToggleFoldItem(button);
                return;
            }

            SelectCategoryButton(null);

            if (_scrollView)
            {
                Flex.ForgetAll();
            }

            if (_selectedItem)
            {
                _selectedItem.Item?.ClearContent();
                _selectedItem.Label.State = false;
            }

            _selectedItem = button;

            if (_selectedItem)
            {
                _selectedItem.Label.State = true;

                if (_selectedItem.Item != null)
                {
                    SelectItem(_selectedItem.Item);
                }

                UnfoldItem(button);
            }

            if (_scrollView)
            {
                _scrollView.Progress = 1.0f;
            }
        }

        private void SelectItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            item.BuildContent();

            SelectCategory(item.Category);
        }

        private void Update()
        {
            if (_hierarchyIcon && _hierarchyIcon.State)
            {
                _refreshTick++;
                if ((_refreshTick % _refreshEveryN) == 0)
                {
                    Hierarchy.Manager.Instance?.Refresh();
                }
            }

            if (_lerpCompleted) return;
            _currentPosition = Utils.LerpPosition(_currentPosition, _targetPosition, _lerpSpeed);
            _lerpCompleted = _currentPosition == _targetPosition;
            SphericalCoordinates = _currentPosition;
        }

        protected void OnDestroy()
        {
            if (_items != null)
            {
                foreach (var kv in _items)
                {
                    var b = kv.Value;
                    if (!b)
                    {
                        continue;
                    }

                    b.Label.Callback = null;
                    b.Foldout.Callback = null;
                }
            }

            if (_categories != null)
            {
                foreach (var kv in _categories)
                {
                    var c = kv.Value;
                    if (c != null) c.Callback = null;
                }
            }

            _items?.Clear();
            _registries?.Clear();
            _categories?.Clear();
        }
    }
}
