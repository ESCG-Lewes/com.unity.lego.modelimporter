using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LEGOModelImporter
{
    /// <summary>Disconnects bricks and updates their model groups as one undoable operation.</summary>
    internal static class BrickDecoupler
    {
        public static bool CanDecoupleBrick(Brick brick)
        {
            if (!brick || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorUtility.IsPersistent(brick) || !brick.gameObject.scene.IsValid() ||
                PrefabUtility.IsPartOfPrefabInstance(brick)) return false;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            // A prefab rooted at a brick or group cannot be split into sibling groups.
            if (stage != null && (stage.prefabContentsRoot.GetComponent<Brick>() ||
                stage.prefabContentsRoot.GetComponent<ModelGroup>())) return false;

            var group = brick.GetComponentInParent<ModelGroup>();
            return group && !group.processed &&
                (group.GetComponentsInChildren<Brick>(true).Length > 1 || brick.GetConnectedBricks(false).Count > 0);
        }

        public static ModelGroup DecoupleBrick(Brick brick)
        {
            var groups = DecoupleBricks(new[] { brick });
            return groups.Count > 0 ? groups[0] : null;
        }

        public static List<ModelGroup> DecoupleBricks(IEnumerable<Brick> bricks)
        {
            var result = new List<ModelGroup>();
            if (bricks == null || ToolsSettings.LockBricksAfterPlacement) return result;
            var selected = new HashSet<Brick>();
            var affected = new HashSet<Brick>();
            foreach (var brick in bricks)
            {
                if (!CanDecoupleBrick(brick) || !selected.Add(brick)) continue;
                affected.UnionWith(brick.GetConnectedBricks());
                affected.UnionWith(brick.GetComponentInParent<ModelGroup>().GetComponentsInChildren<Brick>(true));
            }
            if (selected.Count == 0) return result;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Decouple LEGO bricks");
            try
            {
                foreach (var brick in selected) brick.DisconnectAll();
                ModelGroupUtility.RecomputeHierarchy(affected, isolatedBricks: selected);
                foreach (var brick in selected)
                {
                    var group = brick.GetComponentInParent<ModelGroup>();
                    if (group && !result.Contains(group)) result.Add(group);
                    EditorSceneManager.MarkSceneDirty(brick.gameObject.scene);
                }
                SceneBrickBuilder.MarkSceneDirty();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
            return result;
        }
    }
}
