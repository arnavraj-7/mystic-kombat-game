using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MysticKombat
{
    /// <summary>Music with crossfades, a pooled SFX bus and an announcer/voice channel.</summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        AudioSource musicA, musicB, voice;
        readonly AudioSource[] fighterVoice = new AudioSource[2];
        readonly float[] fighterVoiceAt = new float[2];
        static readonly Dictionary<string, AudioClip> overrides = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> sfx = new List<AudioSource>();
        int nextSfx;
        string currentTrack;
        float fade = 1f;
        bool aActive = true;
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        public float MusicVolume = 0.6f, SfxVolume = 0.9f, VoiceVolume = 1f;
        public float MusicDuck = 1f;

        void Awake()
        {
            musicA = gameObject.AddComponent<AudioSource>();
            musicB = gameObject.AddComponent<AudioSource>();
            foreach (var m in new[] { musicA, musicB }) { m.loop = true; m.playOnAwake = false; m.volume = 0; }
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            for (int i = 0; i < 2; i++)
            {
                fighterVoice[i] = gameObject.AddComponent<AudioSource>();
                fighterVoice[i].playOnAwake = false;
                fighterVoice[i].panStereo = i == 0 ? -0.3f : 0.3f;   // P1 voice from the left, P2 from the right
            }
            StartCoroutine(LoadOverrides());
            for (int i = 0; i < 14; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                sfx.Add(s);
            }
        }

        public void PlayMusic(string track)
        {
            if (track == currentTrack) return;
            var clip = AssetDb.Clip("Music/" + track);
            currentTrack = track;
            var next = aActive ? musicB : musicA;
            next.clip = clip;
            next.volume = 0;
            if (clip != null) next.Play();
            aActive = !aActive;
            fade = 0f;
        }

        public void StopMusic()
        {
            currentTrack = null;
            fade = 0f;
            aActive = !aActive;
            (aActive ? musicA : musicB).Stop();
        }

        void Update()
        {
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / 0.8f);
            var cur = aActive ? musicA : musicB;
            var old = aActive ? musicB : musicA;
            float target = MusicVolume * MusicDuck;
            cur.volume = target * fade;
            old.volume = target * (1f - fade);
            if (fade >= 1f && old.isPlaying) old.Stop();
        }

        public void Sfx(string name, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(name)) return;
            var clip = AssetDb.Clip("Sfx/" + name);
            if (clip == null) return;
            // avoid machine-gunning the same sample in a single frame
            if (lastPlayed.TryGetValue(name, out var t) && Time.unscaledTime - t < 0.03f) return;
            lastPlayed[name] = Time.unscaledTime;
            var s = sfx[nextSfx];
            nextSfx = (nextSfx + 1) % sfx.Count;
            s.pitch = pitch * Random.Range(0.95f, 1.05f);
            s.PlayOneShot(clip, volume * SfxVolume);
        }

        /// <summary>Clips the player dropped into StreamingAssets/voice_override (file name = line key,
        /// e.g. ren_ult.ogg, kazu_s1.wav, fight.mp3). They replace the built-in line on this machine only.</summary>
        IEnumerator LoadOverrides()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "voice_override");
            if (!Directory.Exists(dir)) yield break;
            foreach (var file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                var type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
                if (type == AudioType.UNKNOWN) continue;
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + file.Replace('\\', '/'), type))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("voice override failed: " + file); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    overrides[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = clip;
                    Debug.Log("voice override: " + Path.GetFileName(file));
                }
            }
        }

        static AudioClip VoiceClip(string key)
        {
            int slash = key.LastIndexOf('/');
            string bare = (slash >= 0 ? key.Substring(slash + 1) : key).ToLowerInvariant();
            if (overrides.TryGetValue(bare, out var o)) return o;
            return AssetDb.Clip("Voice/" + key);
        }

        /// <summary>A fighter's own voice line: line = s1/s2/s3/ult/hurt/ko/win. Each fighter has a channel,
        /// so both can shout at once; a new important line cuts the old one, hurt grunts never cut a callout.</summary>
        public void FighterVoice(int who, string fighterId, string line, float delay = 0f)
        {
            int lang = GameApp.I != null && GameApp.I.Save != null ? GameApp.I.Save.VoiceLang : 0;
            if (lang == 2) return;
            AudioClip clip = null;
            if (lang == 0) clip = VoiceClip("jp/" + fighterId + "_" + line);
            if (clip == null && line == "ult") clip = VoiceClip("ult_" + fighterId);
            if (clip == null) return;
            who = Mathf.Clamp(who, 0, 1);
            var src = fighterVoice[who];
            if (line == "hurt" && src.isPlaying) return;
            if (line == "hurt" && Time.unscaledTime - fighterVoiceAt[who] < 0.7f) return;
            fighterVoiceAt[who] = Time.unscaledTime;
            src.Stop();
            src.clip = clip;
            src.volume = VoiceVolume * (line == "hurt" ? 0.8f : 1f);
            if (delay > 0) src.PlayDelayed(delay); else src.Play();
        }

        public void Voice(string name, float volume = 1f)
        {
            var clip = VoiceClip(name);
            if (clip == null) return;
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume * VoiceVolume;
            voice.Play();
        }

        public bool VoicePlaying => voice.isPlaying;
    }
}
