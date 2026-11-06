using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LEGOModelImporter
{
    internal static class GroupConnectedBricksMenu
    {
        private const string MenuPath = "LEGO Tools/Group Connected Bricks";

        [MenuItem(MenuPath, priority = 51)]
        private static void GroupSelection()
        {
            var assemblies = GetSelectedModels().SelectMany(model =>
                FindAssemblies(model).Select(assembly => (model, assembly))).ToArray();
            if (assemblies.Length == 0)
            {
                Debug.Log("The selected imported models have no connected assemblies to regroup.");
                return;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Group connected LEGO bricks");
            var groups = new List<GameObject>();
            try
            {
                foreach (var (model, assembly) in assemblies)
                {
                    var group = ModelGroupUtility.CreateNewDefaultModelGroup($"Connected Assembly {assembly.number}");
                    ModelGroupUtility.SetParent(group.transform, model.transform);
                    Undo.RecordObject(group.transform, "Position connected assembly");
                    group.transform.SetPositionAndRotation(assembly.bricks[0].transform.position, model.transform.rotation);
                    foreach (var brick in assembly.bricks)
                    {
                        ModelGroupUtility.SetParent(brick.transform, group.transform);
                    }
                    groups.Add(group.gameObject);
                }
                Selection.objects = groups.ToArray();
                SceneBrickBuilder.MarkSceneDirty();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool CanGroupSelection()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && GetSelectedModels().Any();
        }

        private static IEnumerable<Model> GetSelectedModels()
        {
            return Selection.gameObjects
                .Select(go => go.GetComponentInParent<Model>())
                .Where(model => model && !string.IsNullOrEmpty(model.absoluteFilePath) &&
                    model.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(model) &&
                    !PrefabUtility.IsPartOfPrefabInstance(model))
                .Distinct();
        }

        private static IEnumerable<(int number, List<Brick> bricks)> FindAssemblies(Model model)
        {
            var bricks = model.GetComponentsInChildren<Brick>(true);
            var remaining = new HashSet<Brick>();
            foreach (var brick in bricks)
            {
                var group = brick.GetComponentInParent<ModelGroup>();
                if (brick.GetComponentInParent<Model>() == model && group && !group.processed &&
                    !PrefabUtility.IsPartOfPrefabInstance(brick))
                {
                    remaining.Add(brick);
                }
            }

            var assemblyNumber = 0;
            foreach (var root in bricks)
            {
                if (!remaining.Remove(root))
                {
                    continue;
                }

                var assembly = new List<Brick> { root };
                for (var i = 0; i < assembly.Count; i++)
                {
                    foreach (var neighbor in assembly[i].GetConnectedBricks(false))
                    {
                        if (remaining.Remove(neighbor))
                        {
                            assembly.Add(neighbor);
                        }
                    }
                }

                if (assembly.Count < 2)
                {
                    continue;
                }

                assemblyNumber++;
                var parent = root.transform.parent;
                if (parent.TryGetComponent<ModelGroup>(out _) &&
                    assembly.All(brick => brick.transform.parent == parent) &&
                    parent.GetComponentsInChildren<Brick>(true).Length == assembly.Count)
                {
                    continue;
                }

                yield return (assemblyNumber, assembly);
            }
        }
    }
}
