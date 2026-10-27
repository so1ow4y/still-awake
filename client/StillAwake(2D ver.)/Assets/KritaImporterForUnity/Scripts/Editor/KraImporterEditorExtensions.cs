using UnityEngine.UIElements;

namespace KraImporter.Editor
{
    static class KraImporterEditorExtensions
    {
        internal static T WithClass<T>(this T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }

        internal static T WithTextFoldout<T>(this T element, string text) where T : Foldout
        {
            element.text = text;
            return element;
        }

        internal static T WithTextButton<T>(this T element, string text) where T : Button
        {
            element.text = text;
            return element;
        }
    }
}
