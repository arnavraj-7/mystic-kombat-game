using System.Collections.Generic;
using UnityEngine;

namespace MysticKombat
{
    /// <summary>Music with crossfades, a pooled SFX bus and an announcer/voice channel.</summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        AudioSource musicA, musicB, voice;
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

        public void Voice(string name, float volume = 1f)
        {
            var clip = AssetDb.Clip("Voice/" + name);
            if (clip == null) return;
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume * VoiceVolume;
            voice.Play();
        }

        public bool VoicePlaying => voice.isPlaying;
    }
}
