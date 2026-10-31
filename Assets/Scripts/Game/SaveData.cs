using System;
using System.Collections.Generic;
using System.IO;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat
{
    public sealed class HighScore
    {
        public string Fighter;
        public int Score;
        public int Stage;
        public string Date;
        public int Difficulty;
    }

    /// <summary>Options, unlocks and the arcade high-score table, stored as JSON in persistentDataPath.</summary>
    public sealed class SaveData
    {
        public float MusicVolume = 0.6f, SfxVolume = 0.9f, VoiceVolume = 1f;
        public bool Cutscenes = true, ShowHints = true, Fullscreen = true;
        public int CpuLevel = 1, RoundSeconds = 99;
        public bool BossUnlocked;
        public string LastIp = "192.168.1.";
        public readonly List<HighScore> Scores = new List<HighScore>();

        static string PathFile => Path.Combine(Application.persistentDataPath, "mystic_kombat_save.json");

        public static SaveData Load()
        {
            var s = new SaveData();
            try
            {
                if (!File.Exists(PathFile)) return s;
                var d = MiniJson.Parse(File.ReadAllText(PathFile)) as Dictionary<string, object>;
                s.MusicVolume = J.F(d, "music", s.MusicVolume);
                s.SfxVolume = J.F(d, "sfx", s.SfxVolume);
                s.VoiceVolume = J.F(d, "voice", s.VoiceVolume);
                s.Cutscenes = J.B(d, "cutscenes", true);
                s.ShowHints = J.B(d, "hints", true);
                s.Fullscreen = J.B(d, "fullscreen", true);
                s.CpuLevel = J.I(d, "cpuLevel", 1);
                s.RoundSeconds = J.I(d, "roundSeconds", 99);
                s.BossUnlocked = J.B(d, "bossUnlocked");
                s.LastIp = J.Str(d, "lastIp", s.LastIp);
                var arr = J.Arr(d, "scores");
                if (arr != null)
                    foreach (var o in arr)
                    {
                        var h = o as Dictionary<string, object>;
                        s.Scores.Add(new HighScore { Fighter = J.Str(h, "fighter"), Score = J.I(h, "score"), Stage = J.I(h, "stage"), Date = J.Str(h, "date"), Difficulty = J.I(h, "difficulty") });
                    }
            }
            catch (Exception e) { Debug.LogWarning("Save load failed: " + e.Message); }
            return s;
        }

        public void Save()
        {
            try
            {
                var scores = new List<object>();
                foreach (var h in Scores)
                    scores.Add(new Dictionary<string, object> { { "fighter", h.Fighter }, { "score", h.Score }, { "stage", h.Stage }, { "date", h.Date }, { "difficulty", h.Difficulty } });
                var d = new Dictionary<string, object>
                {
                    { "music", MusicVolume }, { "sfx", SfxVolume }, { "voice", VoiceVolume }, { "cutscenes", Cutscenes },
                    { "hints", ShowHints }, { "fullscreen", Fullscreen }, { "cpuLevel", CpuLevel }, { "roundSeconds", RoundSeconds },
                    { "bossUnlocked", BossUnlocked }, { "lastIp", LastIp }, { "scores", scores },
                };
                File.WriteAllText(PathFile, MiniJson.Serialize(d));
            }
            catch (Exception e) { Debug.LogWarning("Save failed: " + e.Message); }
        }

        public int AddScore(string fighter, int score, int stage, int difficulty)
        {
            var h = new HighScore { Fighter = fighter, Score = score, Stage = stage, Difficulty = difficulty, Date = DateTime.Now.ToString("yyyy-MM-dd") };
            Scores.Add(h);
            Scores.Sort((a, b) => b.Score.CompareTo(a.Score));
            while (Scores.Count > 10) Scores.RemoveAt(Scores.Count - 1);
            Save();
            return Scores.IndexOf(h);
        }
    }
}
