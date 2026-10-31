using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MysticKombat
{
    /// <summary>Full-screen (or framed) video from StreamingAssets drawn into a RawImage.</summary>
    public sealed class VideoClipPlayer : MonoBehaviour
    {
        VideoPlayer vp;
        RenderTexture rt;
        RawImage img;
        bool started;
        public bool Finished { get; private set; }
        public bool Ready => vp != null && vp.isPrepared;

        public static VideoClipPlayer Create(Transform parent, string path, bool loop, bool playNow = true)
        {
            var rect = Ui.Rect(parent, "video").Fill();
            var p = rect.gameObject.AddComponent<VideoClipPlayer>();
            p.img = rect.gameObject.AddComponent<RawImage>();
            p.img.color = Color.black;
            p.img.raycastTarget = false;
            p.rt = new RenderTexture(1280, 720, 0);
            p.img.texture = p.rt;
            p.vp = rect.gameObject.AddComponent<VideoPlayer>();
            p.vp.playOnAwake = false;
            p.vp.source = VideoSource.Url;
            p.vp.url = path;
            p.vp.isLooping = loop;
            p.vp.renderMode = VideoRenderMode.RenderTexture;
            p.vp.targetTexture = p.rt;
            p.vp.audioOutputMode = VideoAudioOutputMode.Direct;
            p.vp.skipOnDrop = true;
            p.vp.loopPointReached += _ => { if (!loop) p.Finished = true; };
            p.vp.errorReceived += (_, msg) => { Debug.LogWarning("Video error: " + msg); p.Finished = true; };
            p.vp.Prepare();
            p.started = playNow;
            return p;
        }

        public void Play()
        {
            started = true;
            Finished = false;
            if (vp.isPrepared) { vp.time = 0; vp.Play(); }
        }

        void Update()
        {
            if (started && vp.isPrepared && !vp.isPlaying && !Finished) vp.Play();
            if (vp.isPlaying && vp.frame > 0) img.color = Color.white;
        }

        public void Stop()
        {
            if (vp != null) vp.Stop();
            started = false;
        }

        void OnDestroy()
        {
            if (rt != null) rt.Release();
        }
    }
}
