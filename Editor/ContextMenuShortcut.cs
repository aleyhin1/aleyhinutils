using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace aleyhin.utils
{
    static class ContextMenuShortcut
    {
        [Shortcut("Window/Open Context Menu", KeyCode.M, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void Open()
        {
            var window = EditorWindow.focusedWindow;
            if (window == null) return;

            string menu = window.GetType().Name switch
            {
                "ProjectBrowser" => "Assets/",
                "SceneHierarchyWindow" => "GameObject/",
                _ => null
            };
            if (menu == null) return;

            var p = window.position;
            EditorUtility.DisplayPopupMenu(new Rect(p.x + 20, p.y + 40, 0, 0), menu, null);
        }
    }
}
