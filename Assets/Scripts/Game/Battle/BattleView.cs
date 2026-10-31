using System.Collections.Generic;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat
{
    /// <summary>Renders a <see cref="BattleSim"/>: arena layers, fighters, projectiles, VFX, camera.</summary>
    public sealed class BattleView
    {
        public readonly GameObject Root;
        readonly BattleSim sim;
        readonly FighterView[] fighters = new FighterView[2];
        readonly Dictionary<int, SpriteRenderer> projViews = new Dictionary<int, SpriteRenderer>();
        readonly List<Fx> fx = new List<Fx>();
        readonly List<SpriteRenderer> fxPool = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> orbViews = new List<SpriteRenderer>();
        readonly List<Ambient> ambient = new List<Ambient>();
        readonly Camera cam;
        SpriteRenderer sky, mid, floorShade;
        float skyH, midH;
        Vector3 camPos;
        float camSize = 3.4f, shake, ultZoom, flashScreen;
        public bool ShowBoxes;
        readonly List<SpriteRenderer> boxes = new List<SpriteRenderer>();
        int boxesUsed;
        public static Material Additive, Silhouette;
        readonly ArenaDef arena;
        Color tint = Color.white;

        public const float BaseSize = 3.4f;

        sealed class Fx
        {
            public SpriteRenderer R;
            public float Age, Life, Scale0, Scale1, Spin, Vy, Vx;
            public Color Color;
            public bool Additive;
        }

        sealed class Ambient
        {
            public SpriteRenderer R;
            public Vector2 V;
            public float Spin, Phase;
        }

        public BattleView(BattleSim sim, string arenaId, Camera cam)
        {
            this.sim = sim;
            this.cam = cam;
            Root = new GameObject("World");
            EnsureMaterials();
            arena = sim.G.Arenas.Find(a => a.Id == arenaId) ?? (sim.G.Arenas.Count > 0 ? sim.G.Arenas[0] : null);
            if (arena != null) tint = AssetDb.Hex(arena.Tint, Color.white);
            BuildArena(arena != null ? arena.Id : "none");
            for (int i = 0; i < 2; i++) fighters[i] = new FighterView(Root.transform, sim.F[i], i);
            if (sim.F[0].Def.Id == sim.F[1].Def.Id) fighters[1].Tint = new Color(0.62f, 0.72f, 1f); // mirror match: shadow self
            camPos = new Vector3(0, 0, -10);
            cam.orthographicSize = BaseSize;
        }

        public static void EnsureMaterials()
        {
            if (Additive == null)
            {
                var sh = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
                Additive = new Material(sh);
                if (Additive.HasProperty("_TintColor")) Additive.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            if (Silhouette == null)
            {
                var sh = Shader.Find("GUI/Text Shader") ?? Shader.Find("Sprites/Default");
                Silhouette = new Material(sh);
            }
        }

        // ------------------------------------------------------------------ arena

        void BuildArena(string id)
        {
            var skySprite = AssetDb.Load("Arenas/" + id + "/sky", 100);
            sky = NewSprite("sky", -100);
            if (skySprite != null)
            {
                sky.sprite = skySprite;
                // scale so the image covers the view with a margin for parallax; floor line at the spec's 17%
                float viewH = BaseSize * 2f * 1.18f;
                float s = viewH / (skySprite.rect.height / 100f);
                sky.transform.localScale = Vector3.one * s;
                skyH = viewH;
            }
            else
            {
                sky.sprite = Ui.Gradient(new Color(0.25f, 0.12f, 0.42f), new Color(0.05f, 0.03f, 0.1f));
                sky.transform.localScale = new Vector3(40f * 64f, 10f, 1f);
                var floor = NewSprite("floor", -90);
                floor.sprite = AssetDb.White;
                floor.color = new Color(0.12f, 0.08f, 0.18f);
                floor.transform.localScale = new Vector3(40f, 3f, 1);
                floor.transform.position = new Vector3(0, -1.5f, 0);
            }
            var midSprite = AssetDb.Load("Arenas/" + id + "/mid", 100);
            if (midSprite != null)
            {
                mid = NewSprite("mid", -60);
                mid.sprite = midSprite;
                float viewH = BaseSize * 2f * 1.12f;
                mid.transform.localScale = Vector3.one * (viewH / (midSprite.rect.height / 100f));
                midH = viewH;
            }
            floorShade = NewSprite("floorShade", -55);
            floorShade.sprite = Ui.Gradient(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.45f));
            floorShade.transform.localScale = new Vector3(60f * 64f, 1.6f, 1);
            floorShade.transform.position = new Vector3(0, -0.4f, 0);

            string kind = arena != null ? arena.Particles : "dust";
            int count = kind == "void" || kind == "sparks" ? 50 : 36;
            for (int i = 0; i < count; i++)
            {
                var r = NewSprite("amb", i % 3 == 0 ? 40 : -45);
                var a = new Ambient { R = r, Phase = Random.value * 10f };
                ResetAmbient(a, kind, true);
                ambient.Add(a);
            }
        }

        void ResetAmbient(Ambient a, string kind, bool anywhere)
        {
            float x = Random.Range(-11f, 11f);
            float y = anywhere ? Random.Range(-1f, 7f) : 7.5f;
            var r = a.R;
            switch (kind)
            {
                case "leaves":
                case "petals":
                    r.sprite = AssetDb.HasVfx("petals") ? AssetDb.Vfx("petals") : AssetDb.Circle;
                    r.color = kind == "petals" ? new Color(1f, 0.75f, 0.85f, 0.9f) : new Color(0.55f, 0.8f, 0.3f, 0.85f);
                    r.transform.localScale = Vector3.one * Random.Range(0.12f, 0.24f) * (AssetDb.HasVfx("petals") ? 1f : 0.6f);
                    a.V = new Vector2(Random.Range(-1.2f, -0.3f), Random.Range(-0.9f, -0.4f));
                    a.Spin = Random.Range(-180f, 180f);
                    break;
                case "embers":
                    r.sprite = AssetDb.SoftCircle;
                    r.color = new Color(1f, Random.Range(0.35f, 0.6f), 0.1f, 0.9f);
                    r.transform.localScale = Vector3.one * Random.Range(0.06f, 0.16f);
                    a.V = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.4f, 1.1f));
                    if (!anywhere) y = -1f;
                    break;
                case "sparks":
                    r.sprite = AssetDb.SoftCircle;
                    r.color = new Color(1f, 0.8f, 0.3f, 0.8f);
                    r.transform.localScale = Vector3.one * Random.Range(0.05f, 0.14f);
                    a.V = new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.2f, 0.7f));
                    if (!anywhere) y = -1f;
                    break;
                case "void":
                    r.sprite = AssetDb.SoftCircle;
                    r.color = new Color(Random.Range(0.5f, 0.8f), 0.3f, 1f, 0.7f);
                    r.transform.localScale = Vector3.one * Random.Range(0.05f, 0.2f);
                    a.V = new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.1f, 0.6f));
                    if (!anywhere) y = -1f;
                    break;
                default:
                    r.sprite = AssetDb.SoftCircle;
                    r.color = new Color(1f, 0.95f, 0.85f, 0.35f);
                    r.transform.localScale = Vector3.one * Random.Range(0.04f, 0.12f);
                    a.V = new Vector2(Random.Range(-0.5f, -0.1f), Random.Range(-0.1f, 0.15f));
                    break;
            }
            r.transform.position = new Vector3(x, y, 0);
        }

        SpriteRenderer NewSprite(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? Root.transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = order;
            return r;
        }

        // ------------------------------------------------------------------ events -> effects

        public void OnEvents(List<SimEvent> events, AudioDirector audio)
        {
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case Ev.Hit:
                    {
                        var victim = fighters[1 - Mathf.Clamp(e.Who, 0, 1)];
                        victim.Flash = 1f;
                        bool big = e.Heavy;
                        Spawn(AssetDb.HasVfx(big ? "hit_spark_heavy" : "hit_spark") ? AssetDb.Vfx(big ? "hit_spark_heavy" : "hit_spark") : AssetDb.SoftCircle,
                              new Vector2(e.X, e.Y), big ? 1.6f : 0.95f, big ? 2.4f : 1.4f, 0.22f, big ? new Color(1f, 0.85f, 0.5f) : Color.white, true, Random.Range(0, 360f));
                        Spawn(AssetDb.Circle, new Vector2(e.X, e.Y), 0.2f, big ? 2.2f : 1.2f, 0.18f, new Color(1, 1, 1, 0.6f), true);
                        shake = Mathf.Max(shake, big ? 0.22f : 0.08f);
                        audio.Sfx(big ? "hit_heavy" : "hit_light", big ? 1f : 0.8f, big ? 0.95f : 1.05f);
                        break;
                    }
                    case Ev.Block:
                        Spawn(AssetDb.HasVfx("shield") ? AssetDb.Vfx("shield") : AssetDb.Circle, new Vector2(e.X, e.Y), 0.6f, 1.1f, 0.18f, new Color(0.4f, 0.8f, 1f, 0.9f), true);
                        audio.Sfx("block", 0.8f);
                        break;
                    case Ev.ShieldBlock:
                        Spawn(AssetDb.Vfx("shield"), new Vector2(e.X, e.Y), 1.2f, 1.6f, 0.25f, new Color(0.6f, 0.9f, 1f), true);
                        audio.Sfx("shield", 0.8f);
                        break;
                    case Ev.Armor:
                        fighters[1 - Mathf.Clamp(e.Who, 0, 1)].Flash = 0.6f;
                        Spawn(AssetDb.SoftCircle, new Vector2(e.X, e.Y), 0.6f, 1.8f, 0.2f, new Color(1f, 0.4f, 0.2f), true);
                        audio.Sfx("block", 1f, 0.7f);
                        break;
                    case Ev.Whiff:
                    {
                        var m = sim.F[e.Who].Def.Move(e.Str);
                        if (m != null && !string.IsNullOrEmpty(m.Sfx)) audio.Sfx(m.Sfx, 0.7f);
                        break;
                    }
                    case Ev.MoveStart:
                    {
                        var f = sim.F[e.Who];
                        var m = f.Def.Move(e.Str);
                        if (m != null && m.Slot.StartsWith("special") && m.Name != null) fighters[e.Who].Callout(m.Name);
                        if (m != null && m.Slot.StartsWith("special")) audio.Sfx("ki_charge", 0.35f, 1.3f);
                        break;
                    }
                    case Ev.ProjectileSpawn:
                        Spawn(AssetDb.SoftCircle, new Vector2(e.X, e.Y), 0.4f, 1.2f, 0.18f, Color.white, true);
                        break;
                    case Ev.Clash:
                        Spawn(AssetDb.Vfx("hit_spark_heavy"), new Vector2(e.X, e.Y), 1f, 2.6f, 0.3f, new Color(1f, 0.9f, 0.6f), true);
                        audio.Sfx("explosion", 0.7f, 1.2f);
                        shake = Mathf.Max(shake, 0.15f);
                        break;
                    case Ev.Jump:
                        audio.Sfx("jump", 0.5f);
                        break;
                    case Ev.Land:
                        fighters[e.Who].Squash = 1f;
                        Dust(e.X, 0.4f);
                        audio.Sfx("land", 0.4f);
                        break;
                    case Ev.Knockdown:
                        Dust(e.X, 1.2f);
                        shake = Mathf.Max(shake, 0.18f);
                        audio.Sfx("land", 1f, 0.8f);
                        break;
                    case Ev.UltStart:
                    {
                        flashScreen = 1f;
                        ultZoom = 1f;
                        var f = sim.F[e.Who];
                        Spawn(AssetDb.Vfx("aura"), new Vector2(f.X, f.Y + 1.2f), 2.5f, 4f, 0.7f, AssetDb.Hex(f.Def.Color, Color.white), true);
                        Spawn(AssetDb.Vfx("rift"), new Vector2(f.X, f.Y + 1.2f), 0.5f, 5f, 0.6f, new Color(1f, 0.8f, 0.4f), true, 0, 90);
                        audio.Sfx("ult_flash");
                        audio.Voice("ult_" + f.Def.Id);
                        break;
                    }
                    case Ev.UltConnect:
                        shake = Mathf.Max(shake, 0.35f);
                        flashScreen = Mathf.Max(flashScreen, 0.6f);
                        break;
                    case Ev.Heal:
                        for (int i = 0; i < 10; i++)
                            Spawn(AssetDb.HasVfx("heal") ? AssetDb.Vfx("heal") : AssetDb.SoftCircle, new Vector2(e.X + Random.Range(-0.6f, 0.6f), e.Y + Random.Range(0.2f, 2f)), 0.3f, 0.6f, 1.1f, new Color(0.4f, 1f, 0.5f), true, 0, 0, 1.3f);
                        audio.Sfx("heal");
                        break;
                    case Ev.Buff:
                        audio.Sfx("ki_charge", 1f, 0.8f);
                        flashScreen = Mathf.Max(flashScreen, 0.3f);
                        break;
                    case Ev.Counter:
                        Spawn(AssetDb.Vfx("hit_spark_heavy"), new Vector2(e.X, e.Y), 1.4f, 3f, 0.35f, new Color(0.6f, 0.9f, 1f), true);
                        fighters[e.Who].Callout("COUNTER!");
                        audio.Sfx("block", 1f, 0.6f);
                        shake = 0.25f;
                        break;
                    case Ev.Throw:
                        audio.Sfx("whoosh_heavy", 1f, 0.8f);
                        break;
                    case Ev.ThrowBreak:
                        Spawn(AssetDb.Vfx("hit_spark_heavy"), new Vector2(e.X, e.Y), 1.2f, 2f, 0.25f, new Color(0.7f, 0.85f, 1f), true);
                        fighters[e.Who].Callout("BREAK!");
                        audio.Sfx("block", 1f, 1.2f);
                        break;
                    case Ev.Teleport:
                        Spawn(AssetDb.HasVfx("rift") ? AssetDb.Vfx("rift") : AssetDb.Circle, new Vector2(e.X, e.Y + 1.1f), 1.2f, 0.2f, 0.3f, new Color(0.7f, 0.8f, 1f), true);
                        Spawn(AssetDb.HasVfx("rift") ? AssetDb.Vfx("rift") : AssetDb.Circle, new Vector2(e.Value, 1.1f), 0.2f, 1.2f, 0.3f, new Color(0.7f, 0.8f, 1f), true);
                        audio.Sfx("teleport");
                        break;
                    case Ev.OrbTaken:
                        Spawn(AssetDb.SoftCircle, new Vector2(e.X, e.Y), 0.5f, 3f, 0.4f, new Color(1f, 0.85f, 0.3f), true);
                        fighters[e.Who].Callout("+" + (int)e.Value + " KI");
                        audio.Sfx("ki_charge", 1f, 1.4f);
                        break;
                    case Ev.Rage:
                        fighters[e.Who].Callout("RAGE!");
                        audio.Sfx("ki_charge", 1f, 0.6f);
                        break;
                    case Ev.Reflect:
                        Spawn(AssetDb.Vfx("shield"), new Vector2(e.X, e.Y), 0.8f, 1.6f, 0.2f, Color.cyan, true);
                        audio.Sfx("shield");
                        break;
                    case Ev.Dash:
                        Dust(e.X - e.Value * sim.F[e.Who].Facing * 0.4f, 0.6f);
                        audio.Sfx("whoosh_light", 0.6f, 1.2f);
                        break;
                    case Ev.WallSplat:
                        Spawn(AssetDb.Vfx("hit_spark_heavy"), new Vector2(e.X, e.Y), 1.4f, 3.2f, 0.35f, new Color(1f, 0.8f, 0.5f), true);
                        Spawn(AssetDb.Circle, new Vector2(e.X, e.Y), 0.4f, 3f, 0.3f, new Color(1, 1, 1, 0.7f), true);
                        fighters[e.Who].Callout("WALL SPLAT!");
                        shake = Mathf.Max(shake, 0.35f);
                        audio.Sfx("hit_heavy", 1f, 0.7f);
                        break;
                    case Ev.KO:
                        flashScreen = 1f;
                        shake = 0.4f;
                        audio.Sfx("ko");
                        break;
                }
            }
        }

        void Dust(float x, float size)
        {
            for (int i = 0; i < 4; i++)
                Spawn(AssetDb.SoftCircle, new Vector2(x + Random.Range(-0.5f, 0.5f) * size, 0.15f), 0.3f * size, 0.9f * size, 0.4f, new Color(0.85f, 0.8f, 0.7f, 0.5f), false, 0, 0, 0.6f, Random.Range(-1.5f, 1.5f));
        }

        void Spawn(Sprite s, Vector2 pos, float s0, float s1, float life, Color c, bool additive, float rot = 0, float spin = 0, float vy = 0, float vx = 0)
        {
            SpriteRenderer r = null;
            foreach (var p in fxPool) if (!p.gameObject.activeSelf) { r = p; break; }
            if (r == null) { r = NewSprite("fx", 30); fxPool.Add(r); }
            r.gameObject.SetActive(true);
            r.sprite = s;
            r.sharedMaterial = additive ? Additive : null;
            if (r.sharedMaterial == null) r.sharedMaterial = DefaultMat;
            r.color = c;
            r.transform.position = new Vector3(pos.x, pos.y, 0);
            r.transform.rotation = Quaternion.Euler(0, 0, rot);
            r.transform.localScale = Vector3.one * s0;
            fx.Add(new Fx { R = r, Life = life, Scale0 = s0, Scale1 = s1, Spin = spin, Vy = vy, Vx = vx, Color = c, Additive = additive });
        }

        static Material defaultMat;
        static Material DefaultMat => defaultMat != null ? defaultMat : (defaultMat = new Material(Shader.Find("Sprites/Default")));

        // ------------------------------------------------------------------ per-frame render

        public void Render(float dt)
        {
            for (int i = 0; i < 2; i++) fighters[i].Render(sim, fighters, dt, i == sim.ActiveUltOwner);
            RenderMoveFx();
            RenderProjectiles();
            RenderOrbs();
            UpdateFx(dt);
            UpdateAmbient(dt);
            UpdateCamera(dt);
            if (ShowBoxes) DrawBoxes(); else HideBoxes();
        }

        readonly Dictionary<int, SpriteRenderer> moveFx = new Dictionary<int, SpriteRenderer>();

        void RenderMoveFx()
        {
            for (int i = 0; i < 2; i++)
            {
                var f = sim.F[i];
                if (!moveFx.TryGetValue(i, out var r)) { r = NewSprite("movefx" + i, 25); r.sharedMaterial = Additive; moveFx[i] = r; }
                var m = f.Move;
                bool show = f.State == FState.Attack && m != null && !string.IsNullOrEmpty(m.Vfx) && m.Projectile == null && m.Kind != "none"
                            && f.MoveFrame >= m.Startup - 3 && f.MoveFrame <= m.Startup + m.Active + 2 && m.Vfx != "clones" && m.Vfx != "teleport";
                bool aura = f.State == FState.Attack && m != null && m.Kind == "none" && !string.IsNullOrEmpty(m.Vfx);
                if (!show && !aura) { r.gameObject.SetActive(false); continue; }
                r.gameObject.SetActive(true);
                if (aura)
                {
                    // heals, shields, counters and power-ups glow around the body
                    r.sprite = AssetDb.Vfx(m.Vfx);
                    r.color = m.Vfx == "heal" ? new Color(0.5f, 1f, 0.6f, 0.8f) : m.Vfx == "shield" ? new Color(0.6f, 0.9f, 1f, 0.75f) : AssetDb.Hex(f.Def.Color, Color.white);
                    float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 18f);
                    float size = m.Vfx == "aura" ? 3.4f : 2.8f;
                    r.transform.position = new Vector3(f.X, f.Y + f.Def.Height * 0.5f, 0);
                    r.transform.localScale = Vector3.one * size * pulse;
                    r.transform.rotation = Quaternion.identity;
                    continue;
                }
                BattleSim.HitboxWorld(f, out float cx, out float cy, out float w, out float h);
                if (m.Vfx == "stretch_arm")
                {
                    // rubber arm: a skin-tone band from the shoulder out to the fist, snapping back in recovery
                    float reach = Mathf.Clamp01((f.MoveFrame - m.Startup + 4f) / 4f) * (1f - Mathf.Clamp01((f.MoveFrame - m.Startup - m.Active) / 6f));
                    float far = (cx - f.X) * f.Facing + w * 0.5f;
                    float len = Mathf.Max(0.1f, (far - 0.5f) * reach);
                    r.sharedMaterial = DefaultMat;
                    r.sprite = AssetDb.White;
                    r.color = new Color(0.98f, 0.78f, 0.62f);
                    r.transform.position = new Vector3(f.X + f.Facing * (0.5f + len * 0.5f), f.Y + 1.38f, 0);
                    r.transform.localScale = new Vector3(len, 0.13f, 1);
                    r.transform.rotation = Quaternion.identity;
                    if (reach > 0.9f && Random.value < 0.3f)
                        Spawn(AssetDb.SoftCircle, new Vector2(f.X + f.Facing * (0.5f + len), f.Y + 1.38f), 0.35f, 0.6f, 0.12f, new Color(1f, 0.9f, 0.7f), true);
                    continue;
                }
                r.sharedMaterial = Additive;
                var sp = AssetDb.Vfx(m.Vfx);
                r.sprite = sp;
                r.color = Color.white;
                float t = (f.MoveFrame - m.Startup + 3f) / (m.Active + 5f);
                if (w > 6f)
                {
                    // beam: stretch across the hitbox, anchored at the fighter
                    float len = w;
                    float sx = len / (sp.rect.width / sp.pixelsPerUnit);
                    float sy = h * 1.6f / (sp.rect.height / sp.pixelsPerUnit) * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
                    r.transform.position = new Vector3(f.X + f.Facing * (len * 0.5f + 0.5f), cy, 0);
                    r.transform.localScale = new Vector3(sx * f.Facing, sy, 1);
                    r.transform.rotation = Quaternion.identity;
                    r.color = m.Vfx == "beam_violet" || f.Def.Id == "zevrin" ? new Color(0.6f, 0.75f, 1f) : Color.white;
                }
                else if (m.TargetOpponent)
                {
                    float sy = 7f / (sp.rect.height / sp.pixelsPerUnit);
                    r.transform.position = new Vector3(cx, 3.2f, 0);
                    r.transform.localScale = new Vector3(sy * 0.8f * (Random.value > 0.5f ? 1 : -1), sy, 1);
                    r.transform.rotation = Quaternion.identity;
                }
                else
                {
                    float size = Mathf.Max(w, h) * 1.5f;
                    float sc = size / (sp.rect.width / sp.pixelsPerUnit) * Mathf.Lerp(0.7f, 1.15f, t);
                    r.transform.position = new Vector3(cx, cy, 0);
                    r.transform.localScale = new Vector3(sc * f.Facing, sc, 1);
                    r.transform.rotation = Quaternion.Euler(0, 0, m.Vfx == "spiral_orb" || m.Vfx == "water_wheel" ? -Time.time * 720f * f.Facing : 0);
                    r.color = new Color(1, 1, 1, Mathf.Clamp01(1.6f - t));
                }
            }
        }

        void RenderProjectiles()
        {
            var alive = new HashSet<int>();
            foreach (var p in sim.Projectiles)
            {
                alive.Add(p.Id);
                if (!projViews.TryGetValue(p.Id, out var r))
                {
                    r = NewSprite("proj", 22);
                    r.sharedMaterial = p.Def.Vfx == "kunai" ? DefaultMat : Additive;
                    r.sprite = AssetDb.Vfx(p.Def.Vfx);
                    projViews[p.Id] = r;
                }
                var sp = r.sprite;
                float size = Mathf.Max(p.Def.W, p.Def.H) * 1.5f * (p.Def.Vfx == "crescent" ? 1.1f : 1f);
                float sc = size / (sp.rect.width / sp.pixelsPerUnit);
                if (p.Def.Vfx == "death_beam") sc = p.Def.W * 2.2f / (sp.rect.width / sp.pixelsPerUnit);
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 30f + p.Id);
                r.transform.position = new Vector3(p.X, p.Y, 0);
                r.transform.localScale = new Vector3(sc * (p.VX >= 0 ? 1 : -1) * pulse, sc * pulse, 1);
                bool spin = p.Def.Vfx.Contains("orb") || p.Def.Vfx == "ki_blast" || p.Def.Vfx == "fireball";
                r.transform.rotation = spin ? Quaternion.Euler(0, 0, Time.time * 400f) : Quaternion.Euler(0, 0, p.Def.Gravity > 0 ? Mathf.Atan2(p.VY, Mathf.Abs(p.VX)) * Mathf.Rad2Deg * Mathf.Sign(p.VX) : 0);
                if (Random.value < 0.4f)
                    Spawn(AssetDb.SoftCircle, new Vector2(p.X - p.VX * 0.02f, p.Y + Random.Range(-0.1f, 0.1f)), size * 0.25f, 0f, 0.25f, new Color(1f, 0.9f, 0.7f, 0.5f), true);
            }
            var dead = new List<int>();
            foreach (var kv in projViews) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead)
            {
                var r = projViews[id];
                Spawn(AssetDb.SoftCircle, r.transform.position, 0.6f, 1.8f, 0.2f, new Color(1f, 0.9f, 0.7f), true);
                Object.Destroy(r.gameObject);
                projViews.Remove(id);
            }
        }

        void RenderOrbs()
        {
            while (orbViews.Count < sim.Orbs.Count)
            {
                var r = NewSprite("orb", 21);
                r.sharedMaterial = Additive;
                r.sprite = AssetDb.HasVfx("ki_blast") ? AssetDb.Vfx("ki_blast") : AssetDb.SoftCircle;
                orbViews.Add(r);
            }
            for (int i = 0; i < orbViews.Count; i++)
            {
                bool on = i < sim.Orbs.Count;
                orbViews[i].gameObject.SetActive(on);
                if (!on) continue;
                var o = sim.Orbs[i];
                float bob = Mathf.Sin(Time.time * 3f) * 0.12f;
                orbViews[i].transform.position = new Vector3(o.X, o.Y + bob, 0);
                orbViews[i].transform.localScale = Vector3.one * (0.9f + 0.1f * Mathf.Sin(Time.time * 9f)) * 0.9f / (orbViews[i].sprite.rect.width / orbViews[i].sprite.pixelsPerUnit) * 1.3f;
                orbViews[i].color = new Color(1f, 0.85f, 0.3f);
                if (Random.value < 0.3f)
                    Spawn(AssetDb.SoftCircle, new Vector2(o.X + Random.Range(-0.3f, 0.3f), o.Y + bob + Random.Range(-0.3f, 0.3f)), 0.15f, 0f, 0.5f, new Color(1f, 0.8f, 0.3f), true, 0, 0, 0.8f);
            }
        }

        void UpdateFx(float dt)
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.Age += dt;
                float t = f.Age / f.Life;
                if (t >= 1f)
                {
                    f.R.gameObject.SetActive(false);
                    fx.RemoveAt(i);
                    continue;
                }
                float e = 1f - (1f - t) * (1f - t);
                f.R.transform.localScale = Vector3.one * Mathf.Lerp(f.Scale0, f.Scale1, e);
                f.R.transform.Rotate(0, 0, f.Spin * dt);
                f.R.transform.position += new Vector3(f.Vx * dt, f.Vy * dt, 0);
                var c = f.Color;
                c.a *= 1f - t;
                f.R.color = c;
            }
        }

        void UpdateAmbient(float dt)
        {
            string kind = arena != null ? arena.Particles : "dust";
            foreach (var a in ambient)
            {
                var p = a.R.transform.position;
                p.x += (a.V.x + Mathf.Sin(Time.time + a.Phase) * 0.2f) * dt;
                p.y += a.V.y * dt;
                a.R.transform.position = p;
                a.R.transform.Rotate(0, 0, a.Spin * dt);
                if (p.y < -1.2f || p.y > 8f || p.x < -13f || p.x > 13f) ResetAmbient(a, kind, false);
            }
        }

        void UpdateCamera(float dt)
        {
            var a = sim.F[0];
            var b = sim.F[1];
            float mx = (a.X + b.X) * 0.5f;
            float sep = Mathf.Abs(a.X - b.X);
            float targetSize = Mathf.Clamp(2.55f + sep * 0.16f, 2.9f, BaseSize);
            float halfW = targetSize * cam.aspect;
            float maxX = Mathf.Max(0, sim.R.StageHalfWidth + 0.6f - halfW);
            float tx = Mathf.Clamp(mx, -maxX, maxX);
            float ty = targetSize - 1.25f + Mathf.Max(0, Mathf.Max(a.Y, b.Y) - 2.5f) * 0.4f;
            if (ultZoom > 0 && sim.ActiveUltOwner >= 0)
            {
                var o = sim.F[sim.ActiveUltOwner];
                float z = ultZoom;
                targetSize = Mathf.Lerp(targetSize, 2.5f, z * 0.8f);
                tx = Mathf.Lerp(tx, o.X, z * 0.6f);
                ty = Mathf.Lerp(ty, 2.5f - 0.6f, z * 0.6f);
            }
            if (sim.Freeze <= 0) ultZoom = Mathf.MoveTowards(ultZoom, 0, dt * 2.5f);
            camSize = Mathf.Lerp(camSize, targetSize, 1f - Mathf.Exp(-dt * 6f));
            camPos.x = Mathf.Lerp(camPos.x, tx, 1f - Mathf.Exp(-dt * 8f));
            camPos.y = Mathf.Lerp(camPos.y, ty, 1f - Mathf.Exp(-dt * 6f));
            shake = Mathf.MoveTowards(shake, 0, dt * 1.2f);
            var sh = shake > 0 ? (Vector3)(Random.insideUnitCircle * shake) : Vector3.zero;
            cam.orthographicSize = camSize;
            cam.transform.position = camPos + sh;

            // parallax layers follow the camera at reduced rates
            // the generated plates have their floor line ~17% up from the bottom; keep it on y=0 at rest
            float dy = camPos.y - (BaseSize - 1.25f);
            float floorFrac = arena != null ? arena.FloorY : 0.17f;
            if (sky != null && skyH > 0) sky.transform.position = new Vector3(camPos.x * 0.85f, skyH * (0.5f - floorFrac) + dy * 0.9f, 0);
            else if (sky != null) sky.transform.position = new Vector3(camPos.x, camPos.y, 0);
            if (mid != null) mid.transform.position = new Vector3(camPos.x * 0.45f, midH * (0.5f - floorFrac) + dy * 0.5f, 0);
            flashScreen = Mathf.MoveTowards(flashScreen, 0, dt * 3f);
        }

        public float ScreenFlash => flashScreen;

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        // ------------------------------------------------------------------ training: hit/hurt boxes

        void DrawBoxes()
        {
            boxesUsed = 0;
            for (int i = 0; i < 2; i++)
            {
                var f = sim.F[i];
                BattleSim.HurtWorld(f, out float x0, out float y0, out float x1, out float y1);
                Box(x0, y0, x1, y1, f.Invuln > 0 ? new Color(1, 1, 1, 0.25f) : new Color(0.2f, 1f, 0.3f, 0.3f));
                if (f.State == FState.Attack && f.Move != null && f.Move.Kind != "projectile" && f.Move.Kind != "none")
                {
                    int mf = f.MoveFrame;
                    bool active = mf >= f.Move.Startup && mf < f.Move.Startup + f.Move.Active;
                    BattleSim.HitboxWorld(f, out float cx, out float cy, out float w, out float h);
                    Box(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, active ? new Color(1f, 0.15f, 0.15f, 0.45f) : new Color(1f, 0.6f, 0.1f, 0.18f));
                }
            }
            foreach (var p in sim.Projectiles)
                Box(p.X - p.Def.W / 2, p.Y - p.Def.H / 2, p.X + p.Def.W / 2, p.Y + p.Def.H / 2, new Color(1f, 0.9f, 0.1f, 0.4f));
            for (int i = boxesUsed; i < boxes.Count; i++) boxes[i].gameObject.SetActive(false);
        }

        void Box(float x0, float y0, float x1, float y1, Color c)
        {
            if (boxesUsed >= boxes.Count) { var r = NewSprite("box", 60); r.sprite = AssetDb.White; boxes.Add(r); }
            var b = boxes[boxesUsed++];
            b.gameObject.SetActive(true);
            b.color = c;
            b.transform.position = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, 0);
            b.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1);
        }

        void HideBoxes()
        {
            foreach (var b in boxes) b.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            Object.Destroy(Root);
        }
    }
}
