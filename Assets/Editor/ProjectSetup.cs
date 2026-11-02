using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MysticKombat.EditorTools
{
    /// <summary>Batch-mode entry points: project setup and platform builds (Windows / Linux / macOS).</summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Mystic Kombat/Setup Project")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var c = cam.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 5.4f;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.03f, 0.02f, 0.06f);
            cam.transform.position = new Vector3(0, 0, -10);
            cam.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "WeHateDSA";
            PlayerSettings.productName = "Mystic Kombat";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            AlwaysInclude("Legacy Shaders/Particles/Additive", "Sprites/Default", "UI/Default"); // GUI/Text Shader ships in default resources
            AssetDatabase.SaveAssets();
            Debug.Log("[setup] Mystic Kombat project configured");
        }

        static void AlwaysInclude(params string[] names)
        {
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) { Debug.LogWarning("[setup] shader not found: " + n); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
        }

        static void Build(BuildTarget target, string path)
        {
            if (!File.Exists(ScenePath)) Setup();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[build] {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors -> {path}");
            if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        [MenuItem("Mystic Kombat/Build Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/MysticKombat.exe");
        public static void BuildRelease() => Build(BuildTarget.StandaloneWindows64, "Builds/Release/MysticKombat/MysticKombat.exe");

        [MenuItem("Mystic Kombat/Build Linux")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/MysticKombat.x86_64");

        [MenuItem("Mystic Kombat/Build macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/macOS/MysticKombat.app");

        public static void BuildAll()
        {
            BuildWindows();
            BuildLinux();
            BuildMac();
        }
    }

    /// <summary>Import settings for the generated art and audio (no NPOT rescale, no mips, good compression).</summary>
    public sealed class MysticImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = assetPath.Contains("/Arenas/") || assetPath.Contains("/UI/") ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            if (assetPath.Contains("/Fighters/") && assetPath.EndsWith("portrait.png")) ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.8f;
            }
            ai.defaultSampleSettings = s;
        }
    }
}
