using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using kcp2k;
using Mirror;
using TOP.Network;
using TOP.Services;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TOPMultiplayerValidation
{
    const string Request = "Tools/validate-multiplayer.request";
    static TOPMultiplayerValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/PKO/Validate Multiplayer Transport")]
    public static void Run()
    {
        string report;
        try
        {
            bool local = Connect("127.0.0.1", 7777, out string localError);
            bool configured = ApiConfig.TryGetGameServer(out string host, out ushort port);
            string publicError = "No configured public endpoint.";
            bool remote = configured && Connect(host, port, out publicError);
            report = $"Local KCP reliable round-trip (12 seconds): {local} {localError}\nPublic KCP reliable round-trip ({host}:{port}, 12 seconds): {remote} {publicError}\n"
                + "This checks sustained transport and Mirror message delivery, not JWT authentication or database persistence.";
            if (!local || !remote) UnityEngine.Debug.LogError(report); else UnityEngine.Debug.Log(report);
        }
        catch (Exception e) { UnityEngine.Debug.LogException(e); report = "FAILED " + e; }
        File.WriteAllText("Tools/multiplayer-validation-results.txt", report);
    }

    static bool Connect(string host, ushort port, out string error)
    {
        bool connected = false;
        bool disconnected = false;
        string failure = null;
        int replies = 0;
        int prematureSpawns = 0;
        var unbatcher = new Unbatcher();
        var client = new KcpClient(() => connected = true, (data, channel) =>
        {
            if (channel != KcpChannel.Reliable) return;
            if (!unbatcher.AddBatch(data))
            {
                failure = "Invalid reliable Mirror batch.";
                return;
            }
            while (unbatcher.GetNextMessage(out var message, out _))
            {
                using (var reader = NetworkReaderPool.Get(message))
                {
                    ushort id = reader.ReadUShort();
                    if (id == NetworkMessageId<SpawnMessage>.Id) prematureSpawns++;
                    if (id == NetworkMessageId<ServerPong>.Id)
                    {
                        reader.Read<ServerPong>();
                        replies++;
                    }
                }
            }
        }, () => disconnected = true,
            (code, message) => failure = code + ": " + message, new KcpConfig(Timeout: 8000));
        try
        {
            client.Connect(host, port);
            var watch = Stopwatch.StartNew();
            while (!connected && failure == null && watch.ElapsedMilliseconds < 8000)
            {
                client.TickIncoming();
                client.TickOutgoing();
                Thread.Sleep(10);
            }
            if (!connected)
            {
                error = failure ?? "Handshake timed out.";
                return false;
            }
            watch.Restart();
            long nextPing = 0;
            var batcher = new Batcher(1200);
            while (!disconnected && failure == null && watch.ElapsedMilliseconds < 12000)
            {
                client.TickIncoming();
                if (watch.ElapsedMilliseconds >= nextPing)
                {
                    using (var message = NetworkWriterPool.Get())
                    using (var batch = NetworkWriterPool.Get())
                    {
                        NetworkMessages.Pack(new ClientPing { ClientTime = watch.ElapsedMilliseconds / 1000f }, message);
                        batcher.AddMessage(message.ToArraySegment(), watch.Elapsed.TotalSeconds);
                        if (nextPing == 0)
                        {
                            message.Reset();
                            NetworkMessages.Pack(new ReadyMessage(), message);
                            batcher.AddMessage(message.ToArraySegment(), watch.Elapsed.TotalSeconds);
                        }
                        if (batcher.GetBatch(batch)) client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
                    }
                    nextPing += 1000;
                }
                client.TickOutgoing();
                Thread.Sleep(10);
            }
            bool success = !disconnected && failure == null && replies >= 10 && prematureSpawns == 0;
            error = $"Pongs={replies}; premature world spawns={prematureSpawns}; duration={watch.ElapsedMilliseconds}ms; disconnected={disconnected}; {failure}";
            return success;
        }
        finally { client.Disconnect(); client.TickOutgoing(); }
    }
}
