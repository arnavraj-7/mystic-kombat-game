using System.IO;
using MysticKombat.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MysticKombat
{
    public enum GameMode { Versus, VsCpu, CpuVsCpu, Arcade, Training, Lan }

    /// <summary>Everything needed to start a battle.</summary>
    public sealed class MatchConfig
    {
        public GameMode Mode;
        public string P1 = "kazu", P2 = "goro", Arena = "leaf_rooftops";
        public bool P1Cpu, P2Cpu;
        public int Level1 = 1, Level2 = 1;
        public int ArcadeStage;               // 0-based rung on the ladder
        public int ArcadeScore;
        public string[] ArcadeLadder;
        public Net.LanSession Net;
        public uint Seed = 12345;

        public bool IsHuman(int i) => i == 0 ? !P1Cpu : !P2Cpu;
        public MatchConfig Clone() => (MatchConfig)MemberwiseClone();
    }

    public abstract class GameScreen
    {
        public RectTransform Root;
        public virtual void Enter() { }
        public virtual void Tick() { }
        public virtual void Exit() { }
        protected static GameApp App => GameApp.I;
    }

    /// <summary>App shell: owns the grimoire, save data, audio, the UI canvas and the screen flow.</summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp I { get; private set; }
        public Grimoire Grimoire { get; private set; }
        public SaveData Save { get; private set; }
        public AudioDirector Audio { get; private set; }
        public Canvas Canvas { get; private set; }
        public Camera Cam { get; private set; }
        public GameScreen Screen { get; private set; }
        public string GrimoireError;

        public static void Create()
        {
            if (I != null) return;
            var go = new GameObject("MysticKombat");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Save = SaveData.Load();
            Audio = gameObject.AddComponent<AudioDirector>();
            ApplyVolumes();
            LoadGrimoire();

            Cam = Camera.main;
            if (Cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                Cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            DontDestroyOnLoad(Cam.gameObject);
            Cam.orthographic = true;
            Cam.orthographicSize = 5.4f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.02f, 0.06f);
            Cam.transform.position = new Vector3(0, 0, -10);

            Canvas = Ui.MakeCanvas("UI", 10);
            DontDestroyOnLoad(Canvas.gameObject);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
                DontDestroyOnLoad(es);
            }
        }

        void Start()
        {
            if (AutoTest.Requested())
            {
                gameObject.AddComponent<AutoTest>();
                return;
            }
            Go(new IntroScreen());
        }

        public void LoadGrimoire()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "grimoire");
            try
            {
                Grimoire = Grimoire.Load(File.ReadAllText(Path.Combine(dir, "fighters.json")), File.ReadAllText(Path.Combine(dir, "arenas.json")));
                GrimoireError = null;
            }
            catch (System.Exception e)
            {
                GrimoireError = e.Message;
                Debug.LogError("Grimoire load failed: " + e);
                if (Grimoire == null) throw;
            }
            Grimoire.Rules.RoundSeconds = Save.RoundSeconds;
        }

        public void ApplyVolumes()
        {
            Audio.MusicVolume = Save.MusicVolume;
            Audio.SfxVolume = Save.SfxVolume;
            Audio.VoiceVolume = Save.VoiceVolume;
        }

        public void Go(GameScreen next)
        {
            if (Screen != null)
            {
                Screen.Exit();
                if (Screen.Root != null) Destroy(Screen.Root.gameObject);
            }
            Screen = next;
            Screen.Root = Ui.Rect(Canvas.transform, next.GetType().Name).Fill();
            Screen.Enter();
        }

        void Update()
        {
            if (InputReader.KeyDown(UnityEngine.InputSystem.Key.F11))
            {
                Save.Fullscreen = !Save.Fullscreen;
                UnityEngine.Screen.fullScreen = Save.Fullscreen;
                Save.Save();
            }
            Screen?.Tick();
        }

        public void Click(string sfx = "ui_move") => Audio.Sfx(sfx, 0.7f);
    }

    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => GameApp.Create();
    }
}
