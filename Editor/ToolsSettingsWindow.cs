// Copyright (C) LEGO System A/S - All Rights Reserved
// Unauthorized copying of this file, via any medium is strictly prohibited

using UnityEditor;
using UnityEngine;

namespace LEGOModelImporter
{
    internal class ToolsSettingsWindow : EditorWindow
    {
        const string sceneBrickBuildingSettingsMenuPath = "LEGO Tools/Brick Building Settings";

        [MenuItem(sceneBrickBuildingSettingsMenuPath, priority = 30)]
        private static void ShowSettingsWindow()
        {
            ToolsSettingsWindow settings = (ToolsSettingsWindow)EditorWindow.GetWindow(typeof(ToolsSettingsWindow));
            settings.titleContent = new GUIContent("Brick Building Settings");
            settings.Show();
        }

        [MenuItem(sceneBrickBuildingSettingsMenuPath, validate = true)]
        private static bool ValidateBrickBuildingSettings()
        {
            return !EditorApplication.isPlaying;
        }

        private Vector2 scrollPosition;

        private void OnGUI()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scroll.scrollPosition;
                EditorGUILayout.LabelField("Movement and snapping", EditorStyles.boldLabel);

                ToolsSettings.StickySnapDistance = EditorGUILayout.FloatField("Sticky Snap Distance", ToolsSettings.StickySnapDistance);
                Describe("How far the pointer must move before a drag starts in the Scene view. Lower values start the drag sooner.");

                ToolsSettings.MaxTriesPerBrick = EditorGUILayout.IntSlider("Max Tries Per Brick", ToolsSettings.MaxTriesPerBrick, 1, 20);
                Describe("How many candidate connections the tool can try while you move a brick. Higher values can help in crowded builds and may make dragging slower.");

                ToolsSettings.LockBricksAfterPlacement = EditorGUILayout.Toggle("Lock Bricks After Placement", ToolsSettings.LockBricksAfterPlacement);
                Describe("Turn this off to decouple placed bricks. The Decouple Selected Bricks command can also turn it off for you.");

                EditorGUILayout.LabelField("Processing", EditorStyles.boldLabel);
                ToolsSettings.AutoProcessGroups = EditorGUILayout.Toggle("Auto Process Groups", ToolsSettings.AutoProcessGroups);
                Describe("This version saves the preference. To process a group, select it and use Process in the Inspector.");
            }
        }

        private static void Describe(string description)
        {
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
        }
    }
}
