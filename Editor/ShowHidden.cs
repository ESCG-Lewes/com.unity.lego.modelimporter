using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor utility for inspecting hidden imported LEGO body parts in loaded scenes.
public static class ShowHidden
{
    const string k_menu = "LEGO Tools/Show Hidden Objects";
    static readonly Dictionary<Object, HideFlags> s_OriginalFlags = new Dictionary<Object, HideFlags>();
    static bool s_Showing;

    [MenuItem(k_menu)]
    static void ToggleShowHidden()
    {
        s_Showing = !s_Showing;
        if (s_Showing)
        {
            s_OriginalFlags.Clear();
            int count = 0;
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                GameObject go = t.gameObject;
                if (!go.scene.IsValid()) continue;
                if ((t.hideFlags & HideFlags.HideInHierarchy) != 0)
                {
                    s_OriginalFlags[t] = t.hideFlags;
                    t.hideFlags &= ~HideFlags.HideInHierarchy;
                    count++;
                }
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0)
                {
                    s_OriginalFlags[go] = go.hideFlags;
                    go.hideFlags &= ~HideFlags.HideInHierarchy;
                    count++;
                }
            }
            Debug.Log("[ShowHidden] Un-hid " + count + " object(s).");
        }
        else
        {
            foreach (KeyValuePair<Object, HideFlags> entry in s_OriginalFlags)
                if (entry.Key != null) entry.Key.hideFlags = entry.Value;
            Debug.Log("[ShowHidden] Re-hid " + s_OriginalFlags.Count + " object(s).");
            s_OriginalFlags.Clear();
        }
        EditorApplication.DirtyHierarchyWindowSorting();
        EditorApplication.RepaintHierarchyWindow();
    }

    [MenuItem(k_menu, true)]
    static bool ToggleShowHiddenValidate()
    {
        Menu.SetChecked(k_menu, s_Showing);
        return true;
    }
}
