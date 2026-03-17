using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GhostHunt.Editor
{
    /// <summary>
    /// Auto-configures the project on first open.
    /// Sets up layers, tags, quality settings, and build profiles.
    /// Run via menu: GhostHunt → Setup Project
    /// </summary>
    public static class ProjectSetup
    {
        [MenuItem("GhostHunt/Setup Project")]
        public static void Setup()
        {
            SetupLayers();
            SetupTags();
            SetupQuality();
            SetupPlayerSettings();
            Debug.Log("[GhostHunt] Project setup complete! Next: import Photon Fusion 2 SDK.");
        }

        private static void SetupLayers()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            SetLayer(layers, 6, "Wall");
            SetLayer(layers, 7, "Collectible");
            SetLayer(layers, 8, "Ghost");
            SetLayer(layers, 9, "Target");
            SetLayer(layers, 10, "Portal");
            SetLayer(layers, 11, "Radar");

            tagManager.ApplyModifiedProperties();
            Debug.Log("[Setup] Layers configured");
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var layer = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(layer.stringValue))
                layer.stringValue = name;
        }

        private static void SetupTags()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tagManager.FindProperty("tags");

            AddTag(tags, "Collectible");
            AddTag(tags, "PowerPellet");
            AddTag(tags, "Portal");
            AddTag(tags, "GhostSpawn");
            AddTag(tags, "TargetSpawn");
            AddTag(tags, "Decoy");

            tagManager.ApplyModifiedProperties();
            Debug.Log("[Setup] Tags configured");
        }

        private static void AddTag(SerializedProperty tags, string tagName)
        {
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tagName)
                    return; // Already exists
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
        }

        private static void SetupQuality()
        {
            // Quest 1 floor: minimize quality for performance
            // The dither shader does all visual work — we don't need fancy rendering
            QualitySettings.shadows = ShadowQuality.Disable; // No shadows in 1-bit world
            QualitySettings.vSyncCount = 0; // VR handles its own vsync
            QualitySettings.antiAliasing = 0; // Dithering IS the anti-aliasing
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;

            Debug.Log("[Setup] Quality settings configured for Quest 1 floor");
        }

        private static void SetupPlayerSettings()
        {
            // Company and product
            PlayerSettings.companyName = "GhostHunt";
            PlayerSettings.productName = "Ghost Hunt";

            // Use new Input System
            PlayerSettings.SetActiveInputHandler(PlayerSettings.ActiveInputHandler.Both);

            // Android / Quest settings
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Standalone / PC
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);

            Debug.Log("[Setup] Player settings configured");
        }
    }

    /// <summary>
    /// Creates the GameBootstrap scene with minimal setup.
    /// The GameBootstrap.cs script handles all runtime object creation,
    /// so the scene itself just needs to exist.
    /// </summary>
    public static class BootstrapSceneCreator
    {
        [MenuItem("GhostHunt/Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            // Create new empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Save it
            string path = "Assets/Scenes/GameBootstrap.unity";
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, path);

            // Add to build settings
            var buildScenes = EditorBuildSettings.scenes;
            bool alreadyAdded = false;
            foreach (var s in buildScenes)
            {
                if (s.path == path) { alreadyAdded = true; break; }
            }
            if (!alreadyAdded)
            {
                var newScenes = new EditorBuildSettingsScene[buildScenes.Length + 1];
                newScenes[0] = new EditorBuildSettingsScene(path, true);
                System.Array.Copy(buildScenes, 0, newScenes, 1, buildScenes.Length);
                EditorBuildSettings.scenes = newScenes;
            }

            Debug.Log($"[GhostHunt] Bootstrap scene created at {path}. Press Play — GameBootstrap auto-wires everything.");
        }
    }

    /// <summary>
    /// Editor tool: preview maze generation without entering play mode.
    /// </summary>
    public class MazePreviewWindow : EditorWindow
    {
        private int _seed;
        private int _inputWidth = 29;
        private int _inputHeight = 31;
        private int[,] _grid;
        private int _gridWidth;
        private int _gridHeight;
        private float _cellDrawSize = 8f;

        [MenuItem("GhostHunt/Maze Preview")]
        public static void ShowWindow()
        {
            GetWindow<MazePreviewWindow>("Maze Preview");
        }

        private void OnGUI()
        {
            GUILayout.Label("Maze Generator Preview", EditorStyles.boldLabel);

            _seed = EditorGUILayout.IntField("Seed (0=random)", _seed);
            _inputWidth = EditorGUILayout.IntSlider("Width", _inputWidth, 10, 40);
            _inputHeight = EditorGUILayout.IntSlider("Height", _inputHeight, 10, 40);
            _cellDrawSize = EditorGUILayout.Slider("Cell Size", _cellDrawSize, 4f, 16f);

            if (GUILayout.Button("Generate"))
            {
                var gen = new Maze.MazeGenerator(_inputWidth, _inputHeight, _seed);
                _grid = gen.Generate();
                _gridWidth = gen.Width;
                _gridHeight = gen.Height;
                Repaint();
            }

            if (_grid == null) return;

            // Draw maze
            var rect = GUILayoutUtility.GetRect(
                _gridWidth * _cellDrawSize,
                _gridHeight * _cellDrawSize
            );

            for (int x = 0; x < _gridWidth; x++)
            {
                for (int y = 0; y < _gridHeight; y++)
                {
                    var cellRect = new Rect(
                        rect.x + x * _cellDrawSize,
                        rect.y + (_gridHeight - 1 - y) * _cellDrawSize,
                        _cellDrawSize - 1,
                        _cellDrawSize - 1
                    );

                    Color color = _grid[x, y] switch
                    {
                        Maze.MazeGenerator.Wall => Color.black,
                        Maze.MazeGenerator.Collectible => new Color(0.9f, 0.9f, 0.8f),
                        Maze.MazeGenerator.PowerPellet => Color.white,
                        Maze.MazeGenerator.GhostSpawn => new Color(0.3f, 0.3f, 0.8f),
                        Maze.MazeGenerator.TargetSpawn => new Color(0.8f, 0.8f, 0.2f),
                        Maze.MazeGenerator.Portal => new Color(0.8f, 0.2f, 0.8f),
                        _ => new Color(0.5f, 0.5f, 0.5f)
                    };

                    EditorGUI.DrawRect(cellRect, color);
                }
            }
        }
    }
}
