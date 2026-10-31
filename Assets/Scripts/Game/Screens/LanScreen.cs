using System.Collections.Generic;
using System.Linq;
using System.Net;
using MysticKombat.Core;
using MysticKombat.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MysticKombat
{
    /// <summary>LAN lobby: host / discover / join by IP, then both players pick fighters and the host starts.</summary>
    public sealed class LanScreen : GameScreen
    {
        enum Step { Menu, Hosting, Joining, EnterIp, Pick, Waiting }

        LanSession net;
        Step step = Step.Menu;
        MenuList menu;
        Text status, ipText, pickName, remoteText;
        Image pickPose;
        RectTransform pickRoot, menuRoot;
        string ip;
        int pickIndex;
        List<FighterDef> roster;
        float t0;

        public LanScreen() { }
        public LanScreen(LanSession existing)
        {
            net = existing;
            if (net != null && net.Connected) step = Step.Pick;
        }

        public override void Enter()
        {
            t0 = Time.unscaledTime;
            InputReader.SoloLayout = true;
            App.Audio.PlayMusic("select");
            Backdrop.Build(Root, "UI/select_bg", 0.6f);
            var title = Ui.Text(Root, "LAN BATTLE", 100, Ui.Gold).Outlined(5);
            title.rectTransform.Place(0.5f, 0.9f, 1200, 130);
            roster = App.Grimoire.Fighters.Where(f => !f.Boss || App.Save.BossUnlocked).ToList();
            ip = App.Save.LastIp;

            menuRoot = Ui.Rect(Root, "menuRoot").Fill();
            menu = new MenuList(menuRoot, 0.5f, 0.72f, 800, 74, 46);
            menu.Add("HOST A BATTLE", Host)
                .Add("FIND GAMES ON LAN", Join)
                .Add(() => "JOIN BY IP: " + ip, () => { step = Step.EnterIp; })
                .Add("BACK", Back);
            menu.OnMove = i => App.Click();

            status = Ui.Text(Root, "", 36, Color.white).Outlined(2);
            status.rectTransform.Place(0.5f, 0.3f, 1700, 200);
            ipText = Ui.Text(Root, "", 30, Ui.Cyan).Outlined(2);
            ipText.rectTransform.Place(0.5f, 0.18f, 1700, 60);
            ipText.text = "Your IP: " + LanSession.LocalIp() + "   (UDP ports " + LanSession.DiscoveryPort + "-" + LanSession.GamePort + ")";

            pickRoot = Ui.Rect(Root, "pick").Fill();
            pickPose = Ui.Image(pickRoot, null, Color.white);
            pickPose.preserveAspect = true;
            pickPose.rectTransform.Place(0.5f, 0.2f, 560, 560, 0.5f, 0f);
            pickName = Ui.Text(pickRoot, "", 64, Color.white).Outlined(3);
            pickName.rectTransform.Place(0.5f, 0.15f, 1200, 90);
            remoteText = Ui.Text(pickRoot, "", 34, Ui.Cyan).Outlined(2);
            remoteText.rectTransform.Place(0.5f, 0.78f, 1400, 60);
            var h = Ui.Text(pickRoot, "LEFT/RIGHT choose   CONFIRM lock in   BACK leave", 28, new Color(1, 1, 1, 0.7f));
            h.rectTransform.Place(0.5f, 0.05f, 1400, 50);
            pickRoot.gameObject.SetActive(false);
            if (step == Step.Pick) { net.ResetMatch(); EnterPick(); }
        }

        void Host()
        {
            try
            {
                net?.Close();
                net = LanSession.Host();
                step = Step.Hosting;
                App.Click("ui_confirm");
            }
            catch (System.Exception e) { status.text = "Could not host: " + e.Message; }
        }

        void Join()
        {
            try
            {
                net?.Close();
                net = LanSession.Client();
                step = Step.Joining;
                App.Click("ui_confirm");
            }
            catch (System.Exception e) { status.text = "Could not open socket: " + e.Message; }
        }

        void JoinIp()
        {
            if (!IPAddress.TryParse(ip, out var addr)) { status.text = "Not a valid IP address"; return; }
            App.Save.LastIp = ip;
            App.Save.Save();
            if (net == null || net.IsHost) { net?.Close(); net = LanSession.Client(); }
            net.Connect(new IPEndPoint(addr, LanSession.GamePort));
            step = Step.Joining;
        }

        void Back()
        {
            net?.Close();
            App.Click("ui_back");
            App.Go(new MainMenuScreen());
        }

        void EnterPick()
        {
            step = Step.Pick;
            menuRoot.gameObject.SetActive(false);
            pickRoot.gameObject.SetActive(true);
            ShowPick();
        }

        void ShowPick()
        {
            var f = roster[pickIndex];
            pickPose.sprite = AssetDb.Pose(f, "idle_0");
            pickName.text = "< " + f.Name.ToUpper() + " >";
        }

        public override void Tick()
        {
            net?.Poll();
            var nav = InputReader.AnyMenu(out _);
            switch (step)
            {
                case Step.Menu:
                    if (nav == InputReader.Nav.Back) { Back(); return; }
                    menu.Handle(nav);
                    menu.Refresh();
                    status.text = net?.LastError ?? "";
                    break;
                case Step.EnterIp:
                    TypeIp();
                    status.text = "Type the host's IP:  <color=#FFD45A>" + ip + "_</color>\nEnter = connect, Esc = cancel";
                    menu.Refresh();
                    break;
                case Step.Hosting:
                    status.text = "Hosting on " + LanSession.LocalIp() + " ... waiting for a challenger" + new string('.', (int)(Time.unscaledTime * 2) % 4);
                    if (nav == InputReader.Nav.Back) { net.Close(); net = null; step = Step.Menu; }
                    if (net != null && net.Connected) { App.Audio.Sfx("ui_confirm"); EnterPick(); }
                    break;
                case Step.Joining:
                {
                    if (net.Connected) { EnterPick(); break; }
                    var hosts = net.Discovered.Keys.ToList();
                    status.text = hosts.Count == 0 ? "Searching the LAN for hosts..." + (net.LastError != null ? "\n" + net.LastError : "")
                        : "Found: " + string.Join("   ", hosts.Select((h, i) => (i + 1) + ") " + h)) + "\nPress 1-9 to join";
                    for (int i = 0; i < hosts.Count && i < 9; i++)
                        if (InputReader.KeyDown(Key.Digit1 + i)) net.Connect(net.Discovered[hosts[i]]);
                    if (hosts.Count == 1 && nav == InputReader.Nav.Confirm) net.Connect(net.Discovered[hosts[0]]);
                    if (nav == InputReader.Nav.Back) { net.Close(); net = null; step = Step.Menu; }
                    break;
                }
                case Step.Pick:
                    if (nav == InputReader.Nav.Left) { pickIndex = (pickIndex - 1 + roster.Count) % roster.Count; ShowPick(); App.Click(); }
                    if (nav == InputReader.Nav.Right) { pickIndex = (pickIndex + 1) % roster.Count; ShowPick(); App.Click(); }
                    if (nav == InputReader.Nav.Confirm) { net.SendPick(roster[pickIndex].Id); step = Step.Waiting; App.Click("ui_confirm"); }
                    if (nav == InputReader.Nav.Back) { Back(); return; }
                    remoteText.text = net.RemotePick != null ? "Rival locked in " + App.Grimoire.Get(net.RemotePick)?.Name : "Rival is choosing...";
                    break;
                case Step.Waiting:
                    remoteText.text = net.RemotePick != null ? "Rival: " + App.Grimoire.Get(net.RemotePick)?.Name : "Waiting for rival...";
                    if (Time.unscaledTime % 0.5f < 0.02f && net.LocalPick != null) net.SendPick(net.LocalPick); // re-send in case it was lost
                    if (net.IsHost && net.RemotePick != null && !net.StartReceived)
                    {
                        var arenas = App.Grimoire.Arenas;
                        string arena = arenas[Random.Range(0, arenas.Count)].Id;
                        net.SendStart(net.LocalPick, net.RemotePick, arena, (uint)System.Environment.TickCount);
                    }
                    if (net.StartReceived) StartBattle();
                    if (net.Disconnected) { status.text = "Rival disconnected"; step = Step.Menu; menuRoot.gameObject.SetActive(true); pickRoot.gameObject.SetActive(false); }
                    break;
            }
        }

        void TypeIp()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int d = 0; d <= 9; d++)
                if (kb[Key.Digit0 + d].wasPressedThisFrame || kb[Key.Numpad0 + d].wasPressedThisFrame) ip += d.ToString();
            if (kb[Key.Period].wasPressedThisFrame || kb[Key.NumpadPeriod].wasPressedThisFrame) ip += ".";
            if (kb[Key.Backspace].wasPressedThisFrame && ip.Length > 0) ip = ip.Substring(0, ip.Length - 1);
            if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame) JoinIp();
            if (kb[Key.Escape].wasPressedThisFrame) step = Step.Menu;
        }

        void StartBattle()
        {
            var cfg = new MatchConfig
            {
                Mode = GameMode.Lan,
                P1 = net.StartP1,
                P2 = net.StartP2,
                Arena = net.StartArena,
                Seed = net.StartSeed,
                Net = net,
            };
            App.Go(new VsScreen(cfg));
        }
    }
}
