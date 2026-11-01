using System.Collections.Generic;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat
{
    /// <summary>Draws one fighter: pose sprite chosen from sim state, plus squash, lean, flashes, aura, afterimages, shadow and callouts.</summary>
    public sealed class FighterView
    {
        readonly Fighter f;
        readonly int index;
        readonly Transform root;
        readonly SpriteRenderer body, flash, aura, shadow;
        readonly TextMesh callout;
        readonly List<SpriteRenderer> ghosts = new List<SpriteRenderer>();
        readonly Color color;
        public float Flash, Squash;
        float calloutTime = 10f;
        float ghostTimer;
        int ghostNext;
        float tumbleSpin;
        string lastPose;
        float posePop;
        public Color Tint = Color.white;   // alternate colour for mirror matches

        public FighterView(Transform parent, Fighter f, int index)
        {
            this.f = f;
            this.index = index;
            color = AssetDb.Hex(f.Def.Color, Color.white);
            root = new GameObject("Fighter" + index + "_" + f.Def.Id).transform;
            root.SetParent(parent, false);

            shadow = Make("shadow", -8);
            shadow.sprite = AssetDb.SoftCircle;
            shadow.color = new Color(0, 0, 0, 0.55f);

            aura = Make("aura", 4 + index * 2);
            aura.sprite = AssetDb.Vfx("aura");
            aura.sharedMaterial = BattleView.Additive;
            aura.gameObject.SetActive(false);

            body = Make("body", 5 + index * 2);
            flash = Make("flash", 6 + index * 2);
            flash.sharedMaterial = BattleView.Silhouette;
            flash.color = new Color(1, 1, 1, 0);

            for (int i = 0; i < 5; i++)
            {
                var g = Make("ghost", 3);
                g.sharedMaterial = BattleView.Silhouette;
                g.gameObject.SetActive(false);
                ghosts.Add(g);
            }

            var cgo = new GameObject("callout");
            cgo.transform.SetParent(root, false);
            callout = cgo.AddComponent<TextMesh>();
            callout.font = AssetDb.Font;
            cgo.GetComponent<MeshRenderer>().sharedMaterial = AssetDb.Font.material;
            cgo.GetComponent<MeshRenderer>().sortingOrder = 70;
            callout.fontSize = 64;
            callout.characterSize = 0.045f;
            callout.anchor = TextAnchor.LowerCenter;
            callout.alignment = TextAlignment.Center;
            callout.text = "";
        }

        SpriteRenderer Make(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = order;
            return r;
        }

        /// <summary>Draw priority: the fighter who is acting is drawn in front, the one getting hit behind.</summary>
        public void SetFront(bool front)
        {
            int b = front ? 12 : 4;
            aura.sortingOrder = b;
            body.sortingOrder = b + 1;
            flash.sortingOrder = b + 2;
        }

        public void Callout(string text)
        {
            callout.text = text.ToUpper();
            calloutTime = 0f;
        }

        string PoseFor(BattleSim sim)
        {
            switch (f.State)
            {
                case FState.Intro: return (sim.PhaseFrame / 20) % 2 == 0 ? "idle_0" : "idle_1";
                case FState.Idle: return ((f.StateFrame + index * 7) / 22) % 2 == 0 ? "idle_0" : "idle_1";
                case FState.Walk: return (f.StateFrame / 10) % 2 == 0 ? "walk_0" : "walk_1";
                case FState.Crouch: return "crouch";
                case FState.PreJump: return "crouch";
                case FState.Air: return "jump";
                case FState.Landing: return "crouch";
                case FState.Attack: return f.Move?.Pose ?? "light";
                case FState.Hitstun: return "hit";
                case FState.Thrown: return "hit";
                case FState.Blockstun: return f.Crouched ? "crouch" : "block";
                case FState.Tumble: return "hit";
                case FState.Knockdown: return "ko";
                case FState.GetUp: return "crouch";
                case FState.KO: return "ko";
                case FState.Victory: return "victory";
                case FState.Dash: return "walk_1";
                case FState.BackDash: return "walk_0";
            }
            return "idle_0";
        }

        public void Render(BattleSim sim, FighterView[] all, float dt, bool ulting)
        {
            string pose = PoseFor(sim);
            var sprite = AssetDb.Pose(f.Def, pose);
            body.sprite = sprite;
            flash.sprite = sprite;
            // a tiny squash on every pose change hides the hard cut between two drawings
            if (pose != lastPose)
            {
                bool cycle = lastPose != null && lastPose.Length > 2 && pose.Length > 2 && lastPose.Substring(0, lastPose.Length - 1) == pose.Substring(0, pose.Length - 1);
                posePop = cycle ? 0.35f : 1f;
                lastPose = pose;
            }
            posePop = Mathf.MoveTowards(posePop, 0, dt * 12f);

            Vector3 pos = new Vector3(f.X, f.Y, 0);
            float sx = 1f, sy = 1f, rot = 0f;

            // attack anticipation and follow-through
            if (f.State == FState.Attack && f.Move != null)
            {
                var m = f.Move;
                int mf = f.MoveFrame;
                if (mf < m.Startup) { float t = mf / (float)Mathf.Max(1, m.Startup); sx = 1f - 0.05f * t; rot = 4f * t; }
                else if (mf < m.Startup + m.Active) { sx = 1.06f; rot = -3f; pos.x += f.Facing * 0.08f; }
                if (m.Slot == "crouch_light") { sy = 1f; pos.x += f.Facing * (mf >= m.Startup && mf < m.Startup + m.Active ? 0.25f : 0f); }
            }
            // recoil: snap back on impact, ease upright as hitstun runs out
            if (f.State == FState.Hitstun || f.State == FState.Thrown) { rot = 4f + Mathf.Min(f.Stun, 18) * 0.55f; pos.x -= f.Facing * Mathf.Min(f.Stun, 18) * 0.004f; }
            if (f.State == FState.Blockstun) rot = 3f + Mathf.Min(f.Stun, 12) * 0.3f;
            if (f.State == FState.Walk) rot = (f.VX * f.Facing > 0 ? -3f : 2.5f);
            if (f.State == FState.Air) rot = Mathf.Clamp(-f.VY * 1.2f, -7f, 7f);
            if (f.State == FState.Dash) { rot = -8f; pos.y += 0.05f; }
            if (f.State == FState.BackDash) { rot = 8f; pos.y += Mathf.Sin(Mathf.Clamp01(f.StateFrame / 14f) * Mathf.PI) * 0.35f; }
            if (f.State == FState.Tumble)
            {
                tumbleSpin += dt * 360f * (f.VY > 0 ? 1.4f : 0.9f);
                rot = Mathf.Repeat(tumbleSpin, 360f) * 0.25f + 10f;
            }
            else tumbleSpin = 0;
            if (f.State == FState.Walk) pos.y += Mathf.Abs(Mathf.Sin(f.StateFrame * 0.3f)) * 0.04f;
            if (f.State == FState.Idle || f.State == FState.Intro) sy *= 1f + 0.012f * Mathf.Sin(Time.time * 3f + index);

            // landing squash
            Squash = Mathf.MoveTowards(Squash, 0, dt * 5f);
            sx *= 1f + Squash * 0.12f + posePop * 0.045f;
            sy *= 1f - Squash * 0.12f - posePop * 0.03f;

            // hitstop shake on the victim
            if (f.Hitstop > 0 && (f.State == FState.Hitstun || f.State == FState.Tumble || f.State == FState.Blockstun))
                pos.x += Mathf.Sin(Time.time * 90f) * 0.06f;

            root.position = pos;
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(f.Facing * sx, sy, 1);
            body.transform.localRotation = Quaternion.Euler(0, 0, rot * -f.Facing);
            flash.transform.localScale = body.transform.localScale;
            flash.transform.localRotation = body.transform.localRotation;

            // flicker while invulnerable (but not while lying down)
            bool flicker = f.Invuln > 0 && f.State != FState.Knockdown && f.State != FState.KO && f.State != FState.GetUp && (Time.frameCount / 3) % 2 == 0;
            body.color = flicker ? new Color(Tint.r, Tint.g, Tint.b, 0.55f) : Tint;

            // hit flash, rage pulse, ultimate glow
            Flash = Mathf.MoveTowards(Flash, 0, dt * 7f);
            float rage = f.Raging(sim.R) && f.Hp > 0 ? 0.25f + 0.2f * Mathf.Sin(Time.time * 10f) : 0f;
            if (Flash > 0.01f) flash.color = new Color(1, 1, 1, Flash * 0.55f);
            else if (ulting) flash.color = new Color(color.r, color.g, color.b, 0.3f + 0.2f * Mathf.Sin(Time.time * 30f));
            else if (rage > 0) flash.color = new Color(1f, 0.1f, 0.05f, rage);
            else flash.color = new Color(1, 1, 1, 0);

            // aura while buffed, raging or ulting
            bool showAura = f.BuffTimer > 0 || ulting || (f.Raging(sim.R) && f.Hp > 0);
            aura.gameObject.SetActive(showAura);
            if (showAura)
            {
                var ac = f.BuffTimer > 0 || ulting ? color : new Color(1f, 0.15f, 0.1f);
                aura.color = new Color(ac.r, ac.g, ac.b, 0.55f + 0.2f * Mathf.Sin(Time.time * 12f));
                float h = f.Def.Height * 1.5f / (aura.sprite.rect.height / aura.sprite.pixelsPerUnit);
                aura.transform.localPosition = new Vector3(0, f.Def.Height * 0.55f, 0);
                aura.transform.localScale = new Vector3(h * (1f + 0.05f * Mathf.Sin(Time.time * 20f)), h, 1);
            }

            // shadow on the floor
            float height = Mathf.Clamp01(f.Y / 4f);
            shadow.transform.position = new Vector3(f.X, 0.02f, 0);
            shadow.transform.localScale = new Vector3(1.3f * (1f - height * 0.5f), 0.22f * (1f - height * 0.5f), 1);

            // afterimages for dashes / clones / ultimates
            bool trail = (f.State == FState.Attack && f.Move != null && (f.Move.Afterimages || ulting || f.Move.MoveX > 8)) || f.State == FState.Dash || f.State == FState.BackDash;
            ghostTimer -= dt;
            if (trail && ghostTimer <= 0)
            {
                bool clones = f.Move != null && f.Move.Vfx == "clones";
                ghostTimer = clones ? 0.035f : 0.05f;
                var g = ghosts[ghostNext];
                ghostNext = (ghostNext + 1) % ghosts.Count;
                g.gameObject.SetActive(true);
                g.sprite = sprite;
                g.transform.position = root.position;
                g.transform.localScale = body.transform.localScale;
                g.transform.rotation = body.transform.rotation;
                g.color = new Color(color.r, color.g, color.b, 0.6f);
                if (clones)
                {
                    // shadow clones: solid copies of the fighter, not silhouettes
                    g.sharedMaterial = body.sharedMaterial;
                    g.color = new Color(1, 1, 1, 0.75f);
                }
                else g.sharedMaterial = BattleView.Silhouette;
            }
            foreach (var g in ghosts)
            {
                if (!g.gameObject.activeSelf) continue;
                var c = g.color;
                c.a -= dt * 3f;
                g.color = c;
                if (c.a <= 0) g.gameObject.SetActive(false);
            }

            // floating callouts (special names, COUNTER!, RAGE!)
            calloutTime += dt;
            bool showCallout = calloutTime < 0.9f && callout.text.Length > 0;
            callout.gameObject.SetActive(showCallout);
            if (showCallout)
            {
                float a = 1f - Mathf.Clamp01((calloutTime - 0.6f) / 0.3f);
                callout.color = new Color(1f, 0.85f, 0.35f, a);
                callout.transform.localPosition = new Vector3(0, f.Def.Height * 0.95f + calloutTime * 0.35f, 0);
                callout.transform.localScale = Vector3.one * (calloutTime < 0.1f ? Mathf.Lerp(1.5f, 1f, calloutTime / 0.1f) : 1f);
            }
        }
    }
}
