using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KanbanDrawer.Core.Board;

namespace KanbanDrawer.Win
{
    /// <summary>Where the link to the game's agent stands (docs/design/fullcolor.md「看板屋の状態」).</summary>
    public enum AgentStatus
    {
        /// <summary>The pipe is there, no agent connected.</summary>
        Disconnected,
        /// <summary>The pipe could not be made: another 看板屋 has it.</summary>
        Busy,
        /// <summary>Connected, the last HELLO said <c>none</c>.</summary>
        NoEditor,
        /// <summary>Connected, the last HELLO said <c>editor</c>: <see cref="AgentLink.Put"/> works.</summary>
        Editor,
        /// <summary>Connected, but the agent speaks another protocol version: nothing is sent.</summary>
        Mismatch,
    }

    public enum PutResult
    {
        /// <summary>Written (<c>OK</c>).</summary>
        Ok,
        /// <summary>Not sent: the status was not <see cref="AgentStatus.Editor"/>.</summary>
        NotReady,
        /// <summary><c>ERR none</c>: the editor was closed after the last HELLO.</summary>
        NoEditor,
        /// <summary><c>ERR &lt;reason&gt;</c>.</summary>
        Error,
        /// <summary>No (valid) answer in time: the connection was closed.</summary>
        Timeout,
        /// <summary>The connection broke while sending or waiting.</summary>
        Lost,
    }

    /// <summary>
    /// The protocol (version 1) on one connection, over any <see cref="Stream"/> (a pipe in the app, memory in the tests):
    /// reads the agent's lines (HELLO / OK / ERR) on the thread that calls <see cref="Run"/>, sends PUT from
    /// <see cref="Put"/> on another thread and waits for its answer. docs/design/fullcolor.md「パイプの手順」.
    /// </summary>
    public sealed class AgentSession : IDisposable
    {
        public const int Version = 1;
        public const int Width = 320, Height = 240, ImageBytes = Width * Height * 3;
        public const int MaxLine = 256; // bytes, '\n' included

        readonly Stream stream;
        readonly TimeSpan helloTimeout, putTimeout;
        readonly object writeGate = new object();
        readonly object gate = new object();
        readonly byte[] buf = new byte[512];
        int bufLen, bufPos;
        TaskCompletionSource<string> pending;
        AgentStatus status = AgentStatus.Disconnected;
        bool closed;

        public AgentSession(Stream stream, TimeSpan helloTimeout, TimeSpan putTimeout)
        {
            this.stream = stream;
            this.helloTimeout = helloTimeout;
            this.putTimeout = putTimeout;
        }

        /// <summary><see cref="AgentStatus.Disconnected"/> until the first valid HELLO and after the connection ends.</summary>
        public AgentStatus Status { get { lock (gate) return status; } }

        /// <summary>The agent's version when <see cref="Status"/> is <see cref="AgentStatus.Mismatch"/>.</summary>
        public int AgentVersion { get; private set; }

        /// <summary>The reason of the last <c>ERR</c> answer.</summary>
        public string LastError { get; private set; }

        /// <summary>Raised on the reading thread when <see cref="Status"/> changes.</summary>
        public event Action StatusChanged;

        /// <summary>
        /// Reads until the connection ends: first a valid HELLO within the hello timeout (else the connection is closed),
        /// then every line. Returns when the stream is closed, broken, or the agent sent something invalid.
        /// </summary>
        public void Run()
        {
            using (var timer = new Timer(_ => { if (Status == AgentStatus.Disconnected) Close(); }, null, helloTimeout, Timeout.InfiniteTimeSpan))
            {
                try
                {
                    string first = ReadLine();
                    if (first == null || !ParseHello(first, out int v, out bool editor)) return;
                    timer.Dispose();
                    AgentVersion = v;
                    SetStatus(v != Version ? AgentStatus.Mismatch : editor ? AgentStatus.Editor : AgentStatus.NoEditor);
                    for (string line; (line = ReadLine()) != null;)
                    {
                        if (Status == AgentStatus.Mismatch) continue; // another version: read and drop everything
                        if (ParseHello(line, out v, out editor))
                        {
                            if (v == Version) SetStatus(editor ? AgentStatus.Editor : AgentStatus.NoEditor);
                            continue;
                        }
                        TaskCompletionSource<string> p;
                        lock (gate) { p = pending; pending = null; }
                        if (p != null) p.TrySetResult(line); // OK / ERR (or something else: Put closes the connection)
                        // a line nobody waits for is dropped
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    Close();
                }
            }
        }

        /// <summary>
        /// Sends <c>PUT 320 240</c> + the 230,400 RGB bytes and waits for the answer (at most the put timeout; then the
        /// connection is closed). Nothing is sent unless the status is <see cref="AgentStatus.Editor"/>.
        /// </summary>
        public PutResult Put(byte[] rgb)
        {
            if (rgb == null || rgb.Length != ImageBytes) throw new ArgumentException("rgb must be " + ImageBytes + " bytes");
            lock (writeGate) // one PUT at a time: the next waits for the answer of the previous
            {
                var p = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate)
                {
                    if (closed || status != AgentStatus.Editor) return PutResult.NotReady;
                    pending = p;
                }
                try
                {
                    var head = Encoding.ASCII.GetBytes($"PUT {Width} {Height}\n");
                    stream.Write(head, 0, head.Length);
                    stream.Write(rgb, 0, rgb.Length);
                    stream.Flush();
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    Close();
                    return PutResult.Lost;
                }
                if (!p.Task.Wait(putTimeout))
                {
                    Close();
                    return PutResult.Timeout;
                }
                string answer = p.Task.Result;
                if (answer == null) return PutResult.Lost;
                if (answer == "OK") return PutResult.Ok;
                if (answer == "ERR none") return PutResult.NoEditor;
                if (answer.StartsWith("ERR ", StringComparison.Ordinal))
                {
                    LastError = answer.Substring(4);
                    return PutResult.Error;
                }
                Close(); // neither OK nor ERR: treated as no answer
                return PutResult.Timeout;
            }
        }

        /// <summary>Closes the stream (ends <see cref="Run"/> and any waiting <see cref="Put"/>).</summary>
        public void Close()
        {
            TaskCompletionSource<string> p;
            lock (gate)
            {
                if (closed) return;
                closed = true;
                p = pending;
                pending = null;
            }
            try { stream.Dispose(); } catch (Exception) { }
            p?.TrySetResult(null);
            SetStatus(AgentStatus.Disconnected);
        }

        public void Dispose() => Close();

        void SetStatus(AgentStatus s)
        {
            lock (gate)
            {
                if (status == s) return;
                if (closed && s != AgentStatus.Disconnected) return;
                status = s;
            }
            StatusChanged?.Invoke();
        }

        /// <summary><c>HELLO &lt;version&gt; editor|none</c>, single spaces, the version an unsigned integer.</summary>
        internal static bool ParseHello(string line, out int version, out bool editor)
        {
            version = 0;
            editor = false;
            var w = line.Split(' ');
            if (w.Length != 3 || w[0] != "HELLO" || w[1].Length == 0 || w[1].Length > 9) return false;
            foreach (char c in w[1]) if (c < '0' || c > '9') return false;
            version = int.Parse(w[1]);
            if (w[2] == "editor") editor = true;
            else if (w[2] != "none") return false;
            return true;
        }

        /// <summary>One line without its '\n'; null at the end of the stream. A line longer than MaxLine closes the connection.</summary>
        string ReadLine()
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (bufPos == bufLen)
                {
                    bufLen = stream.Read(buf, 0, buf.Length);
                    bufPos = 0;
                    if (bufLen <= 0) return null;
                }
                byte b = buf[bufPos++];
                if (b == (byte)'\n') return sb.ToString();
                if (sb.Length + 1 >= MaxLine) return null; // too long: the caller closes
                sb.Append(b < 0x80 ? (char)b : '?');
            }
        }
    }

    /// <summary>
    /// The pipe server for the agent inside the game (docs/design/fullcolor.md「名前付きパイプ」): one connection at a time,
    /// current user only, listening again after each connection; <see cref="AgentStatus.Busy"/> (retry every 5 s) while
    /// another 看板屋 owns the pipe. Only System.IO.Pipes, no WinForms.
    /// </summary>
    public sealed class AgentLink : IDisposable
    {
        public const string PipeEnv = "KANBANDRAWER_PIPE";

        readonly string pipeName;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly object gate = new object();
        Thread thread;
        AgentSession session;
        NamedPipeServerStream server;
        AgentStatus status = AgentStatus.Disconnected;
        int agentVersion;
        bool disposed;

        public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan PutTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan BusyRetry { get; set; } = TimeSpan.FromSeconds(5);

        /// <param name="pipeName">The name after <c>\\.\pipe\</c> (<see cref="DefaultPipeName"/>).</param>
        public AgentLink(string pipeName) => this.pipeName = pipeName;

        /// <summary><c>KANBANDRAWER_PIPE</c> when set (tests), else <c>KanbanYeah-</c> + the current user's SID.</summary>
        public static string DefaultPipeName()
        {
            string env = Environment.GetEnvironmentVariable(PipeEnv);
            if (!string.IsNullOrEmpty(env)) return env;
            using (var me = WindowsIdentity.GetCurrent()) return "KanbanYeah-" + me.User.Value;
        }

        public AgentStatus Status { get { lock (gate) return status; } }

        /// <summary>The agent's protocol version while <see cref="Status"/> is <see cref="AgentStatus.Mismatch"/>.</summary>
        public int AgentVersion { get { lock (gate) return agentVersion; } }

        /// <summary>The reason of the last <c>ERR</c> answer (<see cref="PutResult.Error"/>).</summary>
        public string LastError { get; private set; }

        /// <summary>Raised on any thread when <see cref="Status"/> changes.</summary>
        public event Action StatusChanged;

        /// <summary>Starts listening in the background (until <see cref="Dispose"/>).</summary>
        public void Start()
        {
            lock (gate)
            {
                if (thread != null || disposed) return;
                thread = new Thread(Serve) { IsBackground = true, Name = "AgentLink" };
                thread.Start();
            }
        }

        /// <summary>
        /// Writes a 320x240 RGB picture (<see cref="ToRgb"/>) into the game's editor. Blocks until the answer (at most
        /// <see cref="PutTimeout"/>); nothing is sent unless the status is <see cref="AgentStatus.Editor"/>.
        /// </summary>
        public PutResult Put(byte[] rgb)
        {
            AgentSession s;
            lock (gate) s = session;
            if (s == null) return PutResult.NotReady;
            var r = s.Put(rgb);
            if (r == PutResult.Error) LastError = s.LastError;
            return r;
        }

        /// <summary>Closes the connection and stops the server. The game keeps running.</summary>
        public void Dispose()
        {
            AgentSession s;
            NamedPipeServerStream p;
            Thread t;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                s = session;
                p = server;
                t = thread;
            }
            stop.Set();
            s?.Close();
            try { p?.Dispose(); } catch (Exception) { }
            if (t == null || t.Join(2000)) stop.Dispose(); // the loop is over: nothing waits on it any more
        }

        void Serve()
        {
            while (!stop.WaitOne(0))
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = Create(pipeName);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    SetStatus(AgentStatus.Busy, 0); // another 看板屋 made it first
                    if (stop.WaitOne(BusyRetry)) return;
                    continue;
                }
                lock (gate)
                {
                    if (disposed) { pipe.Dispose(); return; }
                    server = pipe;
                }
                SetStatus(AgentStatus.Disconnected, 0);
                try
                {
                    if (!WaitForConnection(pipe)) return;
                    var s = new AgentSession(pipe, HelloTimeout, PutTimeout);
                    s.StatusChanged += () => SetStatus(s.Status, s.AgentVersion);
                    lock (gate)
                    {
                        if (disposed) { s.Close(); return; }
                        session = s;
                    }
                    s.Run();
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException) { }
                finally
                {
                    lock (gate) { session = null; server = null; }
                    try { pipe.Dispose(); } catch (Exception) { }
                }
                SetStatus(AgentStatus.Disconnected, 0);
            }
        }

        /// <summary>False when stopped while waiting.</summary>
        bool WaitForConnection(NamedPipeServerStream pipe)
        {
            var ar = pipe.BeginWaitForConnection(null, null);
            try
            {
                if (WaitHandle.WaitAny(new[] { ar.AsyncWaitHandle, stop }) == 1)
                {
                    try { pipe.Dispose(); } catch (Exception) { }
                    try { pipe.EndWaitForConnection(ar); } catch (Exception) { }
                    return false;
                }
                pipe.EndWaitForConnection(ar);
                return true;
            }
            finally
            {
                ar.AsyncWaitHandle.Close(); // one event per wait: not left to the finalizer
            }
        }

        /// <summary>
        /// One instance, byte mode, overlapped (reading and writing at the same time), 64 KB buffers. ACL: read / write for
        /// the current user only, network logons denied. Throws IOException when the pipe already exists.
        /// </summary>
        internal static NamedPipeServerStream Create(string name)
        {
            var sec = new PipeSecurity();
            using (var me = WindowsIdentity.GetCurrent())
                sec.AddAccessRule(new PipeAccessRule(me.User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, sec);
        }

        void SetStatus(AgentStatus s, int version)
        {
            lock (gate)
            {
                if (disposed && s != AgentStatus.Disconnected) return;
                agentVersion = version;
                if (status == s) return;
                status = s;
            }
            StatusChanged?.Invoke();
        }

        /// <summary>
        /// The composite as the 230,400 bytes of a PUT: R, G, B per pixel, rows top to bottom, each left to right; floats
        /// rounded to 0..255. Fixed pixels (background colour, text) are taken as they are; no quantization.
        /// </summary>
        public static byte[] ToRgb(Composite c)
        {
            if (c.Width != AgentSession.Width || c.Height != AgentSession.Height)
                throw new ArgumentException($"the board must be {AgentSession.Width}x{AgentSession.Height}");
            var rgb = new byte[AgentSession.ImageBytes];
            for (int i = 0; i < rgb.Length; i++)
            {
                float v = c.Rgb[i];
                rgb[i] = (byte)(v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f));
            }
            return rgb;
        }
    }
}
