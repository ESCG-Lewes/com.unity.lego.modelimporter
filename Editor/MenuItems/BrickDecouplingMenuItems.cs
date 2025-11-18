// Copyright (C) LEGO System A/S - All Rights Reserved
// Unauthorized copying of this file, via any medium is strictly prohibited
// Place this file in: Editor/MenuItems/BrickDecouplingMenuItems.cs

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace LEGOModelImporter
{
    /// <summary>
    /// Menu items for brick decoupling operations
    /// </summary>
    public static class BrickDecouplingMenuItems
    {
        [MenuItem("LEGO Tools/Decouple Selected Bricks %&d", priority = 50)]
        private static void DecoupleSelectedBricks()
        {
            var bricks = GetSelectedBricks();
            
            if (bricks.Count == 0)
            {
                Debug.LogWarning("No bricks selected to decouple");
                return;
            }

            if (ToolsSettings.LockBricksAfterPlacement)
            {
                if (!EditorUtility.DisplayDialog(
                    "Lock Bricks After Placement Enabled",
                    "Lock Bricks After Placement is currently enabled. Do you want to disable it and decouple the selected bricks?",
                    "Yes, Disable and Decouple",
                    "Cancel"))
                {
                    return;
                }
                
                ToolsSettings.LockBricksAfterPlacement = false;
            }

            var decoupledCount = 0;
            var newGroups = BrickDecoupler.DecoupleBricks(bricks);
            decoupledCount = newGroups.Count;

            if (decoupledCount > 0)
            {
                Debug.Log($"Successfully decoupled {decoupledCount} brick(s)");
                
                // Select the new model groups
                var newSelection = new List<GameObject>();
                foreach (var group in newGroups)
                {
                    if (group != null && group.transform.parent != null)
                    {
                        newSelection.Add(group.transform.parent.gameObject);
                    }
                }
                Selection.objects = newSelection.ToArray();
            }
            else
            {
                Debug.LogWarning("No bricks were decoupled");
            }
        }

        [MenuItem("LEGO Tools/Decouple Selected Bricks %&d", validate = true)]
        private static bool ValidateDecoupleSelectedBricks()
        {
            if (EditorApplication.isPlaying) return false;
            if (!ToolsSettings.IsBrickBuildingOn) return false;
            
            var bricks = GetSelectedBricks();
            return bricks.Count > 0;
        }

        [MenuItem("GameObject/LEGO/Decouple Brick", priority = 20)]
        private static void DecoupleBrickContextMenu()
        {
            DecoupleSelectedBricks();
        }

        [MenuItem("GameObject/LEGO/Decouple Brick", validate = true)]
        private static bool ValidateDecoupleBrickContextMenu()
        {
            return ValidateDecoupleSelectedBricks();
        }

        private static List<Brick> GetSelectedBricks()
        {
            var bricks = new List<Brick>();
            
            foreach (var go in Selection.gameObjects)
            {
                if (go != null)
                {
                    var brick = go.GetComponent<Brick>();
                    if (brick != null && BrickDecoupler.CanDecoupleBrick(brick))
                    {
                        bricks.Add(brick);
                    }
                }
            }
            
            return bricks;
        }
    }
}