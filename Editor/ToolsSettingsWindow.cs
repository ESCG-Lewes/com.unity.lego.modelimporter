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
            settings.Show();
        }

        [MenuItem(sceneBrickBuildingSettingsMenuPath, validate = true)]
        private static bool ValidateBrickBuildingSettings()
        {
            return !EditorApplication.isPlaying;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            var snapDistance = EditorGUILayout.FloatField("Sticky Snap Distance", ToolsSettings.StickySnapDistance);
            ToolsSettings.StickySnapDistance = snapDistance;

            EditorGUILayout.Space();

            var maxTries = EditorGUILayout.IntSlider("Max Tries Per Brick", ToolsSettings.MaxTriesPerBrick, 1, 20);
            ToolsSettings.MaxTriesPerBrick = maxTries;

            EditorGUILayout.Space();

            // Add this new toggle
            var lockBricks = EditorGUILayout.Toggle("Lock Bricks After Placement", ToolsSettings.LockBricksAfterPlacement);
            ToolsSettings.LockBricksAfterPlacement = lockBricks;
            
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Processing", EditorStyles.boldLabel);
            var autoProcess = EditorGUILayout.Toggle("Auto Process Groups", ToolsSettings.AutoProcessGroups);
            ToolsSettings.AutoProcessGroups = autoProcess;
            EditorGUILayout.HelpBox("When disabled, bricks can be moved after being placed. Enable to lock bricks via processing.", MessageType.Info);
        }
    }
}