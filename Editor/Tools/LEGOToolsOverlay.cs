using UnityEditor;
using UnityEngine;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;

namespace LEGOModelImporter
{
    [EditorToolbarElement(id, typeof(SceneView))]
    class BrickBuildingToggleButton : EditorToolbarToggle, IAccessContainerWindow
    {
        const string id = "LEGO Tools/Brick Building";

        const string onIconPath = "Packages/com.unity.lego.modelimporter/Textures/Icons/Brick Building On@2x.png";
        const string offIconPath = "Packages/com.unity.lego.modelimporter/Textures/Icons/Brick Building Off@2x.png";

        public EditorWindow containerWindow { get; set; }

        [InitializeOnLoadMethod]
        static void AddToList()
        {
            LEGOToolsOverlay.AddToolbarButton(id);
        }

        public BrickBuildingToggleButton()
        {
            string toggleBrickBuildingShortcut = LEGOToolsOverlay.GetShortcutText(ToolsSettings.brickBuildingMenuPath);
            string toggleBrickBuildingTooltip = "Toggle Brick Building " + toggleBrickBuildingShortcut;

            text = "";
            onIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(onIconPath);
            offIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(offIconPath);
            tooltip = toggleBrickBuildingTooltip;
            this.RegisterValueChangedCallback(Changed);

            SetValueWithoutNotify(ToolsSettings.IsBrickBuildingOn);

            ToolsSettings.brickBuildingChanged += ValueChanged;
        }

        void ValueChanged(bool value)
        {
            SetValueWithoutNotify(value);
        }

        void Changed(ChangeEvent<bool> evt)
        {
            ToolsSettings.IsBrickBuildingOn = evt.newValue; 
        }
    }

    class SelectConnectedToggleButton : EditorToolbarToggle, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }

        const string selectConnectedTooltip = "Connected Brick Selection. Available when brick building is on.";
        const string selectSingleTooltip = "Single Brick Selection. Available when brick building is on.";

        private bool onButton;

        public SelectConnectedToggleButton(string offIconPath, string onIconPath, bool isOnButton)
        {
            onButton = isOnButton;
            string toggleSelectConnectedShortcut = LEGOToolsOverlay.GetShortcutText(ToolsSettings.selectConnectedMenuPath);
            text = "";
            onIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(onIconPath);
            offIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(offIconPath);
            tooltip = (onButton ? selectConnectedTooltip : selectSingleTooltip) + (string.IsNullOrEmpty(toggleSelectConnectedShortcut) ? "" : " Toggle with " + toggleSelectConnectedShortcut);
            this.RegisterValueChangedCallback(Changed);

            SetValueWithoutNotify(onButton ? ToolsSettings.SelectConnected : !ToolsSettings.SelectConnected);
            SetEnabled(ToolsSettings.IsBrickBuildingOn);

            ToolsSettings.selectConnectedChanged += ValueChanged;
            ToolsSettings.brickBuildingChanged += BrickBuildingChanged;
        }

        void BrickBuildingChanged(bool value)
        {
            SetEnabled(value);
        }

        void ValueChanged(bool value)
        {
            SetValueWithoutNotify(onButton == value);
        }

        void Changed(ChangeEvent<bool> evt)
        {
            if (!enabledSelf) return;
            if (!evt.newValue)
            {
                // Keep the button on if we click on it
                SetValueWithoutNotify(true);
                return;
            }
            ToolsSettings.SelectConnected = onButton == evt.newValue;
        }
    }

    [EditorToolbarElement(id)]
    class SelectConnectedStrip : VisualElement
    {
        public const string id = "LEGO Tools/Select Connected Strip";

        VisualElement buttons;

        [InitializeOnLoadMethod]
        static void AddToList()
        {
            LEGOToolsOverlay.AddToolbarButton(id);
        }

        public SelectConnectedStrip()
        {
            EditorToolbarUtility.SetupChildrenAsButtonStrip(this);

            buttons = new VisualElement() { name = "Select Connected Strip" };
            buttons.AddToClassList("toolbar-contents");
            Add(buttons);

            name = "SelectConnectedStrip";

            buttons.Clear();

            SelectConnectedToggleButton buttonOff = new SelectConnectedToggleButton("Packages/com.unity.lego.modelimporter/Textures/Icons/Select Single Brick Off@2x.png", "Packages/com.unity.lego.modelimporter/Textures/Icons/Select Single Brick On@2x.png", false);
            SelectConnectedToggleButton buttonOn = new SelectConnectedToggleButton("Packages/com.unity.lego.modelimporter/Textures/Icons/Select Connected Off@2x.png", "Packages/com.unity.lego.modelimporter/Textures/Icons/Select Connected On@2x.png", true);
            buttons.Add(buttonOff);
            buttons.Add(buttonOn);

            EditorToolbarUtility.SetupChildrenAsButtonStrip(buttons);
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class LEGOControlsButton : EditorToolbarButton
    {
        public const string id = "LEGO Tools/Controls";

        [InitializeOnLoadMethod]
        static void AddToList()
        {
            LEGOToolsOverlay.AddToolbarButton(id);
        }

        public LEGOControlsButton()
        {
            text = "Controls";
            tooltip = LEGOControlsWindow.ControlsText;
            clicked += LEGOControlsWindow.ShowWindow;
        }
    }

    internal class LEGOControlsWindow : EditorWindow
    {
        internal const string ControlsText =
            "W / S — move selection forward/backward by one LEGO stud\n" +
            "A / D — move selection left/right by one LEGO stud\n" +
            "Arrow keys — rotate selection in 90° steps\n" +
            "Cmd/Ctrl + Arrow — rotate selection in 90° steps (alternate)\n" +
            "Alt + Cmd/Ctrl + Arrow — nudge selection by one LEGO stud (alternate)";

        [MenuItem("LEGO Tools/Controls", priority = 60)]
        internal static void ShowWindow()
        {
            var window = GetWindow<LEGOControlsWindow>();
            window.titleContent = new GUIContent("LEGO Tools Controls");
            window.minSize = new Vector2(420.0f, 150.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Brick Building Controls", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(ControlsText, MessageType.Info);
        }
    }

    /// <summary>
    /// The LEGO tools overlay
    /// </summary>
    /// <remarks>
    /// The overlay is extensible by adding yourself with the <see cref="LEGOToolsOverlay.AddToolbarButton(string)"/> method using the <see cref="InitializeOnLoadMethodAttribute"/> on a method
    /// from a class with the <see cref="EditorToolbarElementAttribute"/> attribute
    /// </remarks>
    [Overlay(typeof(SceneView), toolsOverlayId, "LEGO® Tools")]
    [Icon(iconPath)]
    public class LEGOToolsOverlay : ToolbarOverlay
    {
        // Layout restoration can create the overlay before menu shortcuts are registered.
        internal static string GetShortcutText(string menuPath)
        {
            var manager = ShortcutManager.instance;
            var shortcutId = "Main Menu/" + menuPath;
            foreach (var availableId in manager.GetAvailableShortcutIds())
            {
                if (availableId == shortcutId)
                {
                    return manager.GetShortcutBinding(shortcutId).ToString();
                }
            }
            return "";
        }

        private static List<string> buttonIds = new List<string>();

        private const string toolsOverlayId = "com.unity.lego.modelimporter.toolsOverlayId";

        private const string iconPath = "Packages/com.unity.lego.modelimporter/Textures/Icons/Brick Building Off@2x.png";
        private const bool floatingDefault = true;
        private const string floatingEditorPref = "com.unity.lego.modelImporter.toolsOverlayUndocked";

        /// <summary>
        /// Add an id for a <see cref="EditorToolbarElementAttribute"/>
        /// </summary>
        /// <param name="id">The unique id of an element to add</param>
        public static void AddToolbarButton(string id)
        {
            buttonIds.Add(id);
        }

        LEGOToolsOverlay() : base(buttonIds.ToArray())
        {
            displayedChanged += (value) =>
            {
                ToolsSettings.ShowTools = value;
            };

            ToolsSettings.showToolsChanged += (value) =>
            {
                if (value == displayed) return;
                displayed = value;
            };

            floatingChanged += (value) =>
            {
                EditorPrefs.SetBool(floatingEditorPref, value);
            };

            EditorApplication.delayCall += () =>
            {
                displayed = ToolsSettings.ShowTools;
                bool shouldFloat = EditorPrefs.GetBool(floatingEditorPref, floatingDefault);
                if(shouldFloat)
                {
                    Undock();
                }
            };
        }
    }
}