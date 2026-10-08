using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace TOP.Diagnostics
{
    // Production trace for the dev team: active in the Editor, development builds, the dedicated server (--server)
    // or with --trace. Release player builds keep it off (every call returns immediately). Use --no-trace to disable.
    // Output: <project>/Logs/Trace (Editor) or <build>/Trace; summarize with "node Tools/trace-report.js <file>".
    public static class GameTrace
    {
        public const double HitchMs = 100, SlowScopeMs = 50, SlowHandlerMs = 20, SlowHttpMs = 500, StallMs = 1000;
        const float SummaryInterval = 10f;
        const int MaxDepth = 32, KeepFiles = 40, BigMessageBytes = 16 * 1024, MaxLogChars = 500;
        const long MaxFileBytes = 20L * 1024 * 1024, MaxFolderBytes = 200L * 1024 * 1024;

        public static bool Enabled { get; private set; }
        public static string FilePath { get; private set; }
        public static string Folder { get; private set; }

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static BlockingCollection<string> queue;
        static Thread writer, watchdog;
        static volatile bool clearRequested;
        static string role;
        static readonly object logGate = new object();
        static string lastLog; static int lastLogRepeats;
        static int mainThreadId;
        static volatile int frame;
        static long heartbeatTicks;
        static volatile bool focused = true, quitting;
        static volatile string sceneName = "";
        static readonly string[] scopeNames = new string[MaxDepth];
        static volatile int depth;
        static readonly StringBuilder frameScopes = new StringBuilder();

        // ms since the trace started (thread-safe).
        public static double Now => clock.Elapsed.TotalMilliseconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Shutdown();
            var args = Environment.GetCommandLineArgs();
            Enabled = !args.Contains("--no-trace")
                && (Application.isEditor || Debug.isDebugBuild || args.Contains("--trace") || args.Contains("--server"));
            if (!Enabled) return;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            depth = 0; frame = 0; quitting = false;
            Interlocked.Exchange(ref heartbeatTicks, clock.ElapsedTicks);
            try { OpenFile(args); }
            catch (Exception e) { Enabled = false; Debug.LogWarning("[GameTrace] desativado: " + e.Message); return; }

            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += Shutdown;
            SceneManager.sceneLoaded += (s, m) => { sceneName = SceneManager.GetActiveScene().name; Write("SCENE", $"loaded {s.name} mode={m}"); };
            SceneManager.sceneUnloaded += s => Write("SCENE", "unloaded " + s.name);
            SceneManager.activeSceneChanged += (a, b) => { sceneName = b.name; Write("SCENE", "active " + b.name); };

            var go = new GameObject("[GameTrace]") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameTraceRunner>();

            watchdog = new Thread(Watchdog) { IsBackground = true, Name = "GameTrace.Watchdog" };
            watchdog.Start();
            if (args.Contains("--trace-profiler") || Environment.GetEnvironmentVariable("TOP_TRACE_PROFILER") == "1")
                StartProfilerCapture();
        }

        static void OpenFile(string[] args)
        {
            role = Application.isEditor ? "editor" : args.Contains("--server") ? "server" : "client";
            string dir = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "Trace")
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Trace");
            try { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, ".write-test"), ""); }
            catch { dir = Path.Combine(Application.persistentDataPath, "Trace"); Directory.CreateDirectory(dir); }
            Folder = dir;

            var stream = NewFile();
            queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), 100000);
            writer = new Thread(() =>
            {
                try
                {
                    foreach (var line in queue.GetConsumingEnumerable())
                    {
                        if (clearRequested)
                        {
                            clearRequested = false;
                            stream.Flush(); stream.BaseStream.SetLength(0);
                            stream.WriteLine($"{Now / 1000.0:F3}\t{frame}\tSESSION\ttrace limpo");
                        }
                        else if (stream.BaseStream.Length >= MaxFileBytes)
                        {
                            stream.WriteLine($"{Now / 1000.0:F3}\t{frame}\tSESSION\tlimite de {MaxFileBytes / 1048576} MB, continua no proximo arquivo");
                            stream.Dispose();
                            stream = NewFile();
                        }
                        stream.WriteLine(line);
                        if (queue.Count == 0) stream.Flush();
                    }
                }
                catch (Exception) { }
                finally { stream.Dispose(); }
            }) { IsBackground = true, Name = "GameTrace.Writer" };
            writer.Start();

            string flags = string.Join(" ", args.Where(a => a.StartsWith("--")));
            Write("SESSION", $"start {DateTime.Now:yyyy-MM-dd HH:mm:ss} role={role} version={Application.version} unity={Application.unityVersion} "
                + $"platform={Application.platform} dev={Debug.isDebugBuild} batch={Application.isBatchMode} cpu={SystemInfo.processorType.Trim()} "
                + $"cores={SystemInfo.processorCount} ramMB={SystemInfo.systemMemorySize} gpu={SystemInfo.graphicsDeviceName} flags=[{flags}]");
        }

        // Starts a new file and prunes the oldest ones so the folder never exceeds KeepFiles / MaxFolderBytes.
        static StreamWriter NewFile()
        {
            long total = 0;
            int kept = 0;
            foreach (var f in new DirectoryInfo(Folder).GetFiles("trace-*.*").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                total += f.Length; kept++;
                if (kept >= KeepFiles || total >= MaxFolderBytes - MaxFileBytes) try { f.Delete(); } catch { }
            }
            FilePath = Path.Combine(Folder, $"trace-{DateTime.Now:yyyyMMdd-HHmmss}-{role}.log");
            return new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                new UTF8Encoding(false));
        }

        // Empties the file currently being written (the Tools menu deletes the older ones).
        public static void Clear() { if (Enabled) { clearRequested = true; Write("SESSION", "clear"); } }

        static void StartProfilerCapture()
        {
            string raw = Path.ChangeExtension(FilePath, ".raw");
            Profiler.logFile = raw;
            Profiler.enableBinaryLog = true;
            Profiler.maxUsedMemory = 256 * 1024 * 1024;
            Profiler.enabled = true;
            Write("SESSION", "profiler capture " + raw + " (abrir no Unity Profiler: Load)");
        }

        public static void Shutdown()
        {
            if (queue == null) return;
            quitting = true;
            Application.logMessageReceivedThreaded -= OnLog;
            Write("SESSION", "end");
            queue.CompleteAdding();
            writer?.Join(2000);
            queue = null; writer = null; watchdog = null;
            if (Profiler.enableBinaryLog) { Profiler.enabled = false; Profiler.enableBinaryLog = false; Profiler.logFile = ""; }
        }

        // Thread-safe raw line: elapsed seconds, frame, category, message (tab separated).
        public static void Write(string category, string message)
        {
            var q = queue;
            if (q == null || q.IsAddingCompleted) return;
            try { q.TryAdd(FormattableString.Invariant($"{Now / 1000.0:F3}\t{frame}\t{category}\t") + message); } catch (InvalidOperationException) { }
        }

        // Free-form marker for important moments (enter world, character selected...).
        public static void Mark(string message) { if (Enabled) Write("MARK", message); }

        // using (GameTrace.Measure("Nome")) { ... } — logged when slow, shown in the Unity Profiler and named in freezes.
        // Do not keep a scope open across a coroutine yield or await.
        public static Scope Measure(string name, int arg = int.MinValue, double slowMs = SlowScopeMs)
        {
            if (!Enabled || Thread.CurrentThread.ManagedThreadId != mainThreadId) return default;
            return new Scope(name, arg, slowMs);
        }

        public readonly struct Scope : IDisposable
        {
            readonly string name;
            readonly int arg, level;
            readonly double started, slowMs;

            internal Scope(string name, int arg, double slowMs)
            {
                this.name = name; this.arg = arg; this.slowMs = slowMs;
                level = depth;
                if (level < MaxDepth) scopeNames[level] = name;
                depth = level + 1;
                Profiler.BeginSample(name);
                started = Now;
            }

            public void Dispose()
            {
                if (name == null) return;
                double ms = Now - started;
                Profiler.EndSample();
                depth = level;
                string label = arg == int.MinValue ? name : name + "(" + arg + ")";
                if (ms >= 5 && level == 0 && frameScopes.Length < 600) frameScopes.Append(label).Append('=').Append(ms.ToString("F0")).Append("ms ");
                if (ms >= slowMs) Write("SLOW", $"{label} ms={ms:F1} depth={level}");
            }
        }

        static string OpenScopes()
        {
            int d = Math.Min(depth, MaxDepth);
            if (d == 0) return "none (codigo da Unity: render/animacao/fisica/carregamento)";
            var sb = new StringBuilder();
            for (int i = 0; i < d; i++) { if (i > 0) sb.Append(" > "); sb.Append(scopeNames[i]); }
            return sb.ToString();
        }

        // ---- HTTP ----
        public static void Http(UnityWebRequest req, double startedMs)
        {
            if (!Enabled || req == null) return;
            double ms = Now - startedMs;
            string path = req.url ?? "";
            int q = path.IndexOf('?'); if (q >= 0) path = path.Substring(0, q);
            int scheme = path.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) { int slash = path.IndexOf('/', scheme + 3); path = slash >= 0 ? path.Substring(slash) : "/"; }
            ulong bytes = req.downloadedBytes;
            string error = req.result == UnityWebRequest.Result.Success ? "" : " error=" + Clean(req.error);
            Write(ms >= SlowHttpMs ? "HTTP-SLOW" : "HTTP", $"{req.method} {path} status={req.responseCode} ms={ms:F1} kb={bytes / 1024.0:F1}{error}");
            GameTraceRunner.CountHttp(ms, req.result != UnityWebRequest.Result.Success || req.responseCode >= 400);
        }

        // ---- Unity log (any thread) ----
        static void OnLog(string message, string stack, LogType type)
        {
            if (message != null && message.StartsWith("[GameTrace]", StringComparison.Ordinal)) return;
            string cat = type switch { LogType.Warning => "WARN", LogType.Error or LogType.Assert => "ERROR", LogType.Exception => "EXC", _ => "LOG" };
            string text = Clean(message);
            if (type == LogType.Exception || type == LogType.Error)
            {
                var lines = (stack ?? "").Split('\n').Where(l => l.Trim().Length > 0).Take(3).Select(l => l.Trim());
                text += " @ " + string.Join(" | ", lines);
            }
            GameTraceRunner.CountLog(type);
            // Identical consecutive messages (spam) are collapsed into one "repetido Nx" line.
            lock (logGate)
            {
                string key = cat + text;
                if (key == lastLog) { lastLogRepeats++; return; }
                FlushRepeats();
                lastLog = key;
            }
            Write(cat, text);
        }

        static void FlushRepeats()
        {
            if (lastLogRepeats > 0) Write("LOG", $"(mensagem anterior repetida {lastLogRepeats}x)");
            lastLogRepeats = 0;
        }

        internal static void FlushLogRepeats() { lock (logGate) { FlushRepeats(); lastLog = null; } }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            return s.Length > MaxLogChars ? s.Substring(0, MaxLogChars) + "..." : s;
        }

        // ---- main-thread heartbeat + freeze watchdog ----
        internal static void Heartbeat(int frameCount, bool isFocused)
        {
            frame = frameCount; focused = isFocused;
            Interlocked.Exchange(ref heartbeatTicks, clock.ElapsedTicks);
        }

        internal static string TakeFrameScopes()
        {
            if (frameScopes.Length == 0) return "";
            string s = frameScopes.ToString(); frameScopes.Clear(); return s;
        }

        static void Watchdog()
        {
            long reportedFor = -1; double nextReport = 0;
            while (!quitting && queue != null)
            {
                Thread.Sleep(250);
                long beat = Interlocked.Read(ref heartbeatTicks);
                double blocked = (clock.ElapsedTicks - beat) * 1000.0 / Stopwatch.Frequency;
                if (blocked < StallMs) { reportedFor = -1; continue; }
                if (reportedFor != beat) { reportedFor = beat; nextReport = 0; }
                if (blocked < nextReport) continue;
                nextReport = blocked < 5000 ? 5000 : blocked + 5000;
                Write("STALL", $"thread principal travada ha {blocked:F0} ms scene={sceneName} focused={focused} aberto={OpenScopes()}");
            }
        }

        internal static void SetScene(string name) { sceneName = name; }
        internal static int MainThreadId => mainThreadId;
        internal static int BigMessage => BigMessageBytes;
        internal static float Interval => SummaryInterval;
    }

    [DefaultExecutionOrder(-32000)]
    sealed class GameTraceRunner : MonoBehaviour
    {
        sealed class NetStat { public int count; public long bytes; public double handlerMs, handlerMax; }

        static int httpCount, httpErrors, logErrors, logWarnings, logExceptions;
        static double httpTotal, httpMax;
        readonly Dictionary<Type, NetStat> netIn = new Dictionary<Type, NetStat>();
        readonly Dictionary<Type, NetStat> netOut = new Dictionary<Type, NetStat>();
        double lastFrame, nextSummary, worstFrame, frameSum;
        int frames, over33, over100, gcStart, gcFrame;

        internal static void CountHttp(double ms, bool error)
        {
            Interlocked.Increment(ref httpCount);
            if (error) Interlocked.Increment(ref httpErrors);
            lock (typeof(GameTraceRunner)) { httpTotal += ms; if (ms > httpMax) httpMax = ms; }
        }

        internal static void CountLog(LogType type)
        {
            if (type == LogType.Warning) Interlocked.Increment(ref logWarnings);
            else if (type == LogType.Exception) Interlocked.Increment(ref logExceptions);
            else if (type == LogType.Error || type == LogType.Assert) Interlocked.Increment(ref logErrors);
        }

        void Start()
        {
            lastFrame = GameTrace.Now;
            nextSummary = lastFrame + GameTrace.Interval * 1000;
            gcStart = gcFrame = GC.CollectionCount(0);
            GameTrace.SetScene(SceneManager.GetActiveScene().name);
            // Mirror resets these events after scene load, so subscribe here (first frame).
            NetworkDiagnostics.InMessageEvent += OnIn;
            NetworkDiagnostics.OutMessageEvent += OnOut;
            NetworkDiagnostics.HandlerEvent += OnHandled;
        }

        void OnDestroy()
        {
            NetworkDiagnostics.InMessageEvent -= OnIn;
            NetworkDiagnostics.OutMessageEvent -= OnOut;
            NetworkDiagnostics.HandlerEvent -= OnHandled;
            Summary(true);
        }

        static NetStat Stat(Dictionary<Type, NetStat> map, Type t)
        {
            if (!map.TryGetValue(t, out var s)) map[t] = s = new NetStat();
            return s;
        }

        void OnIn(NetworkDiagnostics.MessageInfo m)
        {
            if (m.message == null) return;
            var s = Stat(netIn, m.message.GetType()); s.count++; s.bytes += m.bytes;
            if (m.bytes >= GameTrace.BigMessage) GameTrace.Write("NET", $"in {m.message.GetType().Name} kb={m.bytes / 1024.0:F1}");
        }

        void OnOut(NetworkDiagnostics.MessageInfo m)
        {
            if (m.message == null) return;
            var s = Stat(netOut, m.message.GetType()); s.count += m.count; s.bytes += (long)m.bytes * m.count;
            if (m.bytes >= GameTrace.BigMessage) GameTrace.Write("NET", $"out {m.message.GetType().Name} kb={m.bytes / 1024.0:F1} x{m.count}");
        }

        void OnHandled(Type type, double ms)
        {
            var s = Stat(netIn, type); s.handlerMs += ms; if (ms > s.handlerMax) s.handlerMax = ms;
            if (ms >= GameTrace.SlowHandlerMs) GameTrace.Write("HANDLER", $"{type.Name} ms={ms:F1}");
        }

        void Update()
        {
            double now = GameTrace.Now, dt = now - lastFrame;
            lastFrame = now;
            GameTrace.Heartbeat(Time.frameCount, Application.isFocused);
            frames++; frameSum += dt;
            if (dt > worstFrame) worstFrame = dt;
            if (dt > 33) over33++;
            int gc = GC.CollectionCount(0);
            if (dt >= GameTrace.HitchMs)
            {
                over100++;
                GameTrace.Write(dt >= GameTrace.StallMs ? "FREEZE" : "HITCH",
                    $"ms={dt:F0} scene={SceneManager.GetActiveScene().name} gc={gc - gcFrame} scopes={GameTrace.TakeFrameScopes()}");
            }
            else GameTrace.TakeFrameScopes();
            gcFrame = gc;
            if (now >= nextSummary) { nextSummary = now + GameTrace.Interval * 1000; Summary(false); }
        }

        void Summary(bool final)
        {
            var sb = new StringBuilder();
            double avg = frames > 0 ? frameSum / frames : 0;
            sb.Append($"fps={(avg > 0 ? 1000 / avg : 0):F0} frameAvg={avg:F1}ms worst={worstFrame:F0}ms >33ms={over33} >100ms={over100}");
            int gc = GC.CollectionCount(0);
            sb.Append($" | gc={gc - gcStart} monoMB={Profiler.GetMonoUsedSizeLong() / 1048576} allocMB={Profiler.GetTotalAllocatedMemoryLong() / 1048576}");
            if (NetworkClient.active) sb.Append($" | client={(NetworkClient.isConnected ? "on" : "off")} rtt={NetworkTime.rtt * 1000:F0}ms");
            if (NetworkServer.active) sb.Append($" | server conns={NetworkServer.connections.Count} spawned={NetworkServer.spawned.Count}");
            AppendNet(sb, "in", netIn, true);
            AppendNet(sb, "out", netOut, false);
            double httpAvg; int hc; double hmax; int herr;
            lock (typeof(GameTraceRunner)) { hc = httpCount; httpAvg = hc > 0 ? httpTotal / hc : 0; hmax = httpMax; herr = httpErrors; httpCount = httpErrors = 0; httpTotal = httpMax = 0; }
            if (hc > 0) sb.Append($" | http n={hc} avg={httpAvg:F0}ms max={hmax:F0}ms err={herr}");
            int le = Interlocked.Exchange(ref logErrors, 0), lx = Interlocked.Exchange(ref logExceptions, 0), lw = Interlocked.Exchange(ref logWarnings, 0);
            if (le + lx + lw > 0) sb.Append($" | logs exc={lx} err={le} warn={lw}");
            GameTrace.FlushLogRepeats();
            GameTrace.Write(final ? "SUMMARY-END" : "SUMMARY", sb.ToString());
            frames = over33 = over100 = 0; frameSum = worstFrame = 0; gcStart = gc;
            netIn.Clear(); netOut.Clear();
        }

        static void AppendNet(StringBuilder sb, string dir, Dictionary<Type, NetStat> map, bool handlers)
        {
            if (map.Count == 0) return;
            int count = 0; long bytes = 0;
            foreach (var s in map.Values) { count += s.count; bytes += s.bytes; }
            sb.Append($" | net-{dir} msgs={count} kb={bytes / 1024.0:F1} top=");
            foreach (var kv in map.OrderByDescending(k => handlers ? k.Value.handlerMs : k.Value.bytes).Take(4))
            {
                sb.Append(kv.Key.Name).Append('(').Append(kv.Value.count).Append(',').Append((kv.Value.bytes / 1024.0).ToString("F1")).Append("kb");
                if (handlers && kv.Value.handlerMs > 0) sb.Append(',').Append(kv.Value.handlerMs.ToString("F1")).Append("ms max ").Append(kv.Value.handlerMax.ToString("F1"));
                sb.Append(") ");
            }
        }
    }
}
