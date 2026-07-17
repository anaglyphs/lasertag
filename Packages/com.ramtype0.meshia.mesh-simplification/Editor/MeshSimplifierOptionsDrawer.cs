#nullable enable
using CustomLocalization4EditorExtension;
using Meshia.MeshSimplification;
using Meshia.MeshSimplification.Editor.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
namespace Meshia.MeshSimplification.Editor
{
    [CustomPropertyDrawer(typeof(MeshSimplifierOptions))]
    public class MeshSimplifierOptionsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("29eaabb0631cacc44913c34b86fc38f0"));

            var root = visualTreeAsset.CloneTree();

            root.BindProperty(property);

            var languagePicker = root.Q<DropdownField>("LanguagePicker");

            var enableSmartLinkToggle = root.Q<Toggle>("EnableSmartLinkToggle");
            var smartLinkOptionsGroup = root.Q<GroupBox>("SmartLinkOptionsGroup");

            var resetOptionsButton = root.Q<Button>("ResetOptionsButton");

            LocalizationProvider.LocalizeBindedElements<MeshSimplifierOptions>(root);
            smartLinkOptionsGroup.text = LocalizationProvider.Localization.Tr("Meshia.MeshSimplification.MeshSimplifierOptions.SmartLinkOptions");

            LocalizationProvider.Localization.MountLanguagePicker(languagePicker);

            languagePicker.RegisterValueChangedCallback(evt =>
            {
                LocalizationProvider.LocalizeBindedElements<MeshSimplifierOptions>(root);
                smartLinkOptionsGroup.text = LocalizationProvider.Localization.Tr("Meshia.MeshSimplification.MeshSimplifierOptions.SmartLinkOptions");
            });

            enableSmartLinkToggle.RegisterValueChangedCallback(changeEvent =>
            {
                smartLinkOptionsGroup.style.display = changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;

            });


            resetOptionsButton.clicked += () =>
            {
                property.boxedValue = MeshSimplifierOptions.Default;
                property.serializedObject.ApplyModifiedProperties();
            };

            


            return root;
        }
    }

}
