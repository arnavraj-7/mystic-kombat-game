using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using MysticKombat.Core;
using UnityEngine;

namespace MysticKombat.Net
{
    /// <summary>
    /// "Astral Link": delay-based lockstep over UDP. Only inputs travel; both peers run the same
    /// deterministic <see cref="BattleSim"/>. Each input packet repeats the last frames so lost packets
    /// heal themselves; state checksums every 30 frames detect desyncs. Host = player 1.
    /// </summary>
    public sealed class LanSession
    {
        public const int GamePort = 47778, DiscoveryPort = 47777, Delay = 3;
        const byte HELLO = 1, WELCOME = 2, PICK = 3, START = 4, INPUT = 5, CHECK = 6, BYE = 7, PING = 8, PONG = 9, BEACON = 10, READY = 11;

        UdpClient sock, beaconSock;
        IPEndPoint remote;
        public bool IsHost { get; private set; }
        public int LocalIndex => IsHost ? 0 : 1;
        public bool Connected => remote != null && !Disconnected;
        public bool Disconnected { get; private set; }
        public string RemotePick, LocalPick;
        public bool RemoteReady;
        public string StartP1, StartP2, StartArena;
        public uint StartSeed;
        public bool StartReceived;
        public bool Desynced { get; private set; }
        public int DesyncFrame { get; private set; }
        public string LastError;
        public readonly Dictionary<string, IPEndPoint> Discovered = new Dictionary<string, IPEndPoint>();

        readonly Dictionary<int, Btn> localIn = new Dictionary<int, Btn>();
        readonly Dictionary<int, Btn> remoteIn = new Dictionary<int, Btn>();
        readonly Dictionary<int, uint> localSums = new Dictionary<int, uint>();
        readonly Dictionary<int, uint> remoteSums = new Dictionary<int, uint>();
        int localMax;
        float lastRecv, lastSend, lastBeacon, lastPing;
        float rtt;
        public Func<Btn> InputOverride;    // autotest: drive the local side with a CPU

        public static LanSession Host()
        {
            var s = new LanSession { IsHost = true };
            s.sock = new UdpClient(new IPEndPoint(IPAddress.Any, GamePort));
            s.sock.EnableBroadcast = true;
            s.lastRecv = Time.realtimeSinceStartup;
            return s;
        }

        public static LanSession Client()
        {
            var s = new LanSession { IsHost = false };
            s.sock = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                s.beaconSock = new UdpClient();
                s.beaconSock.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                s.beaconSock.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            }
            catch (Exception e) { s.LastError = "discovery unavailable: " + e.Message; s.beaconSock = null; }
            s.lastRecv = Time.realtimeSinceStartup;
            return s;
        }

        public void Connect(IPEndPoint host)
        {
            remote = host;
            lastRecv = Time.realtimeSinceStartup;
            Send(new[] { HELLO });
        }

        public static string LocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !ua.Address.ToString().StartsWith("169.254"))
                            return ua.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        // ------------------------------------------------------------------ transport

        void Send(byte[] data)
        {
            if (remote == null || sock == null) return;
            try { sock.Send(data, data.Length, remote); lastSend = Time.realtimeSinceStartup; }
            catch (Exception e) { LastError = e.Message; }
        }

        public void Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (sock == null) return;
            try
            {
                while (sock.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] d = sock.Receive(ref from);
                    if (d.Length == 0) continue;
                    if (remote == null && IsHost && d[0] == HELLO) { remote = from; }
                    if (remote == null || !from.Equals(remote)) continue;
                    lastRecv = now;
                    Handle(d);
                }
            }
            catch (SocketException) { /* ICMP port unreachable on Windows when the peer isn't up yet */ }
            catch (Exception e) { LastError = e.Message; }

            if (beaconSock != null)
            {
                try
                {
                    while (beaconSock.Available > 0)
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] d = beaconSock.Receive(ref from);
                        if (d.Length > 1 && d[0] == BEACON)
                        {
                            string name = Encoding.UTF8.GetString(d, 1, d.Length - 1);
                            Discovered[name + " (" + from.Address + ")"] = new IPEndPoint(from.Address, GamePort);
                        }
                    }
                }
                catch { }
            }

            // host advertises itself until someone joins
            if (IsHost && remote == null && now - lastBeacon > 1f)
            {
                lastBeacon = now;
                var b = Encoding.UTF8.GetBytes(Environment.MachineName);
                var pkt = new byte[b.Length + 1];
                pkt[0] = BEACON;
                Buffer.BlockCopy(b, 0, pkt, 1, b.Length);
                try { sock.Send(pkt, pkt.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); } catch { }
            }
            if (remote != null)
            {
                if (now - lastPing > 1f) { lastPing = now; var p = new byte[5]; p[0] = PING; WriteFloat(p, 1, now); Send(p); }
                if (now - lastSend > 0.05f && localMax > 0) SendInputs();
                if (now - lastRecv > 6f) Disconnected = true;
            }
        }

        void Handle(byte[] d)
        {
            switch (d[0])
            {
                case HELLO:
                    if (IsHost) Send(new[] { WELCOME });
                    break;
                case WELCOME:
                    break;
                case PICK:
                    RemotePick = Encoding.UTF8.GetString(d, 1, d.Length - 1);
                    break;
                case READY:
                    RemoteReady = true;
                    break;
                case START:
                {
                    var parts = Encoding.UTF8.GetString(d, 1, d.Length - 1).Split('|');
                    StartP1 = parts[0]; StartP2 = parts[1]; StartArena = parts[2]; StartSeed = uint.Parse(parts[3]);
                    StartReceived = true;
                    break;
                }
                case INPUT:
                {
                    int start = BitConverter.ToInt32(d, 1);
                    int n = d[5];
                    for (int i = 0; i < n; i++) remoteIn[start + i] = (Btn)BitConverter.ToUInt16(d, 6 + i * 2);
                    break;
                }
                case CHECK:
                {
                    int f = BitConverter.ToInt32(d, 1);
                    uint sum = BitConverter.ToUInt32(d, 5);
                    remoteSums[f] = sum;
                    CompareSums(f);
                    break;
                }
                case PING:
                    d[0] = PONG;
                    Send(d);
                    break;
                case PONG:
                    rtt = Mathf.Lerp(rtt, Time.realtimeSinceStartup - BitConverter.ToSingle(d, 1), rtt == 0 ? 1f : 0.3f);
                    break;
                case BYE:
                    Disconnected = true;
                    break;
            }
        }

        static void WriteFloat(byte[] b, int at, float v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, at, 4);

        // ------------------------------------------------------------------ lobby

        public void SendPick(string fighter)
        {
            LocalPick = fighter;
            var b = Encoding.UTF8.GetBytes(fighter);
            var p = new byte[b.Length + 1];
            p[0] = PICK;
            Buffer.BlockCopy(b, 0, p, 1, b.Length);
            Send(p);
        }

        public void SendReady() => Send(new[] { READY });

        public void SendStart(string p1, string p2, string arena, uint seed)
        {
            StartP1 = p1; StartP2 = p2; StartArena = arena; StartSeed = seed;
            var b = Encoding.UTF8.GetBytes(p1 + "|" + p2 + "|" + arena + "|" + seed);
            var p = new byte[b.Length + 1];
            p[0] = START;
            Buffer.BlockCopy(b, 0, p, 1, b.Length);
            for (int i = 0; i < 3; i++) Send(p);
            StartReceived = true;
        }

        /// <summary>Clear per-match state (keeps the connection) before a new battle.</summary>
        public void ResetMatch()
        {
            localIn.Clear(); remoteIn.Clear(); localSums.Clear(); remoteSums.Clear();
            for (int f = 1; f <= Delay; f++) { localIn[f] = Btn.None; remoteIn[f] = Btn.None; }
            localMax = Delay;
            Desynced = false;
            StartReceived = false;
            RemotePick = LocalPick = null;
            RemoteReady = false;
        }

        // ------------------------------------------------------------------ lockstep

        public void SendLocalUpTo(int frame, Func<Btn> read)
        {
            bool added = false;
            while (localMax < frame + Delay)
            {
                localMax++;
                localIn[localMax] = InputOverride != null ? InputOverride() : read();
                added = true;
            }
            if (added) SendInputs();
        }

        void SendInputs()
        {
            int n = Math.Min(12, localMax);
            int start = localMax - n + 1;
            var p = new byte[6 + n * 2];
            p[0] = INPUT;
            Buffer.BlockCopy(BitConverter.GetBytes(start), 0, p, 1, 4);
            p[5] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                localIn.TryGetValue(start + i, out Btn b);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)b), 0, p, 6 + i * 2, 2);
            }
            Send(p);
            // drop history that both sides are long past
            if (localMax % 120 == 0)
            {
                var old = new List<int>();
                foreach (var k in localIn.Keys) if (k < localMax - 240) old.Add(k);
                foreach (var k in old) { localIn.Remove(k); remoteIn.Remove(k); }
            }
        }

        public bool TryGet(int frame, out Btn p1, out Btn p2)
        {
            p1 = p2 = Btn.None;
            if (!localIn.TryGetValue(frame, out Btn mine) || !remoteIn.TryGetValue(frame, out Btn theirs)) return false;
            if (IsHost) { p1 = mine; p2 = theirs; } else { p1 = theirs; p2 = mine; }
            return true;
        }

        public void SendChecksum(int frame, uint sum)
        {
            localSums[frame] = sum;
            var p = new byte[9];
            p[0] = CHECK;
            Buffer.BlockCopy(BitConverter.GetBytes(frame), 0, p, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(sum), 0, p, 5, 4);
            Send(p);
            CompareSums(frame);
        }

        public void CheckChecksum(int frame, uint sum)
        {
            if (frame % 30 == 0) localSums[frame] = sum;
        }

        void CompareSums(int f)
        {
            if (localSums.TryGetValue(f, out uint a) && remoteSums.TryGetValue(f, out uint b) && a != b && !Desynced)
            {
                Desynced = true;
                DesyncFrame = f;
                Debug.LogWarning($"LAN desync at frame {f}: local {a:X8} remote {b:X8}");
            }
        }

        public string Status
        {
            get
            {
                if (remote == null) return "Waiting for opponent...";
                return $"LAN  {remote.Address}   ping {Mathf.RoundToInt(rtt * 1000)} ms   input delay {Delay}f";
            }
        }

        public void Close()
        {
            try { Send(new[] { BYE }); } catch { }
            sock?.Close();
            beaconSock?.Close();
            sock = null;
            beaconSock = null;
        }
    }
}
