using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// Full colour, the 看板屋 side of the pipe (docs/design/fullcolor.md「パイプの手順」): <see cref="AgentSession"/> over an
    /// in-memory duplex stream playing the agent, the composite → RGB conversion, and a real named pipe with a test name.
    /// </summary>
    public class AgentLinkTests
    {
        static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);
        static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

        /// <summary>A session running on its own thread, with the agent's end of the stream.</summary>
        sealed class Harness : IDisposable
        {
            public readonly AgentSession Session;
            public readonly DuplexStream Agent;
            public readonly Task Running;
            readonly List<AgentStatus> seen = new List<AgentStatus>();

            public Harness(TimeSpan? hello = null, TimeSpan? put = null)
            {
                var (host, agent) = DuplexStream.Pair();
                Agent = agent;
                Session = new AgentSession(host, hello ?? Long, put ?? Long);
                Session.StatusChanged += () => { lock (seen) seen.Add(Session.Status); };
                Running = Task.Run(() => Session.Run());
            }

            public AgentStatus[] Seen { get { lock (seen) return seen.ToArray(); } }

            public void Send(string line) => Agent.WriteBytes(Encoding.ASCII.GetBytes(line + "\n"));

            public void WaitFor(AgentStatus s)
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (Session.Status != s)
                {
                    Assert.True(DateTime.UtcNow < until, $"status stays {Session.Status}, expected {s}");
                    Thread.Sleep(5);
                }
            }

            public void WaitEnded() => Assert.True(Running.Wait(5000), "the session did not end");

            public void Dispose() => Session.Close();
        }

        static byte[] Picture()
        {
            var rgb = new byte[AgentSession.ImageBytes];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7 + i / 960);
            return rgb;
        }

        /// <summary>Reads what the host sent for one PUT: the line and the picture.</summary>
        static (string Line, byte[] Data) ReadPut(DuplexStream agent)
        {
            var line = new StringBuilder();
            for (int b; (b = agent.ReadByteBlocking()) != '\n';)
            {
                Assert.True(b >= 0, "stream ended in the PUT line");
                line.Append((char)b);
            }
            var data = agent.ReadExactly(AgentSession.ImageBytes);
            return (line.ToString(), data);
        }

        // ---------- HELLO ----------

        [Fact]
        public void HelloEditorAndNoneAndTheirChanges()
        {
            using (var h = new Harness())
            {
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
                h.Send("HELLO 1 none");
                h.WaitFor(AgentStatus.NoEditor);
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                h.Send("HELLO 1 none");
                h.WaitFor(AgentStatus.NoEditor);
                Assert.Equal(new[] { AgentStatus.NoEditor, AgentStatus.Editor, AgentStatus.NoEditor }, h.Seen);
            }
        }

        [Fact]
        public void AnotherVersionIsAMismatchAndLaterHellosAreDropped()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 2 editor");
                h.WaitFor(AgentStatus.Mismatch);
                Assert.Equal(2, h.Session.AgentVersion);
                h.Send("HELLO 1 editor"); // read and dropped: the connection is kept, nothing changes
                h.Send("HELLO 2 none");
                Thread.Sleep(100);
                Assert.Equal(AgentStatus.Mismatch, h.Session.Status);
                Assert.False(h.Running.IsCompleted);
                Assert.Equal(PutResult.NotReady, h.Session.Put(Picture()));
                Assert.Equal(0, h.Agent.Available);
            }
        }

        [Theory]
        [InlineData("HELLO 1 maybe")]
        [InlineData("HELLO x editor")]
        [InlineData("HELLO  1 editor")]
        [InlineData("HELLO 1 editor ")]
        [InlineData("HELO 1 editor")]
        [InlineData("OK")]
        public void AnInvalidFirstLineClosesTheConnection(string line)
        {
            using (var h = new Harness())
            {
                h.Send(line);
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
                Assert.True(h.Agent.ClosedByPeer);
            }
        }

        [Fact]
        public void ALineLongerThan256BytesClosesTheConnection()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                h.Send(new string('x', 255)); // 256 bytes with '\n': still allowed (an unknown line, dropped)
                Thread.Sleep(100);
                Assert.False(h.Running.IsCompleted);
                h.Send(new string('x', 256)); // 257 bytes
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
            }
        }

        [Fact]
        public void NoHelloInTimeClosesTheConnection()
        {
            using (var h = new Harness(hello: Short))
            {
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
                Assert.True(h.Agent.ClosedByPeer);
            }
        }

        [Fact]
        public void AValidHelloStopsTheHelloTimeout()
        {
            using (var h = new Harness(hello: Short))
            {
                h.Send("HELLO 1 none");
                h.WaitFor(AgentStatus.NoEditor);
                Thread.Sleep(600);
                Assert.Equal(AgentStatus.NoEditor, h.Session.Status);
            }
        }

        // ---------- PUT ----------

        [Fact]
        public void PutSendsTheLineAndExactlyThePictureThenOk()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var pic = Picture();
                var put = Task.Run(() => h.Session.Put(pic));
                var (line, data) = ReadPut(h.Agent);
                Assert.Equal("PUT 320 240", line);
                Assert.Equal(pic, data);
                Assert.Equal(0, h.Agent.Available); // nothing after the picture
                h.Send("OK");
                Assert.Equal(PutResult.Ok, put.Result);
                Assert.Equal(AgentStatus.Editor, h.Session.Status);
            }
        }

        [Fact]
        public void ErrNoneAndErrWithAReason()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                h.Send("ERR none");
                Assert.Equal(PutResult.NoEditor, put.Result);

                put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                h.Send("ERR unexpected canvas size");
                Assert.Equal(PutResult.Error, put.Result);
                Assert.Equal("unexpected canvas size", h.Session.LastError);
                Assert.False(h.Running.IsCompleted); // ERR keeps the connection
            }
        }

        [Fact]
        public void AHelloWhileWaitingIsTakenAndTheAnswerStillCounts()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                h.Send("HELLO 1 none");
                h.Send("ERR none");
                Assert.Equal(PutResult.NoEditor, put.Result);
                Assert.Equal(AgentStatus.NoEditor, h.Session.Status);
            }
        }

        [Fact]
        public void NoAnswerInTimeClosesTheConnection()
        {
            using (var h = new Harness(put: Short))
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                Assert.Equal(PutResult.Timeout, put.Result);
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
                Assert.True(h.Agent.ClosedByPeer);
            }
        }

        [Fact]
        public void AnAnswerThatIsNeitherOkNorErrClosesTheConnection()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                h.Send("WHAT");
                Assert.Equal(PutResult.Timeout, put.Result);
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
            }
        }

        [Fact]
        public void DisconnectingWhileWaitingIsLost()
        {
            using (var h = new Harness())
            {
                h.Send("HELLO 1 editor");
                h.WaitFor(AgentStatus.Editor);
                var put = Task.Run(() => h.Session.Put(Picture()));
                ReadPut(h.Agent);
                h.Agent.Dispose(); // the game went away
                Assert.Equal(PutResult.Lost, put.Result);
                h.WaitEnded();
                Assert.Equal(AgentStatus.Disconnected, h.Session.Status);
                Assert.Equal(PutResult.NotReady, h.Session.Put(Picture()));
            }
        }

        [Fact]
        public void NothingIsSentWithoutAnEditor()
        {
            using (var h = new Harness())
            {
                Assert.Equal(PutResult.NotReady, h.Session.Put(Picture())); // before HELLO
                h.Send("HELLO 1 none");
                h.WaitFor(AgentStatus.NoEditor);
                Assert.Equal(PutResult.NotReady, h.Session.Put(Picture()));
                Thread.Sleep(50);
                Assert.Equal(0, h.Agent.Available);
            }
        }

        // ---------- composite → RGB ----------

        [Fact]
        public void TheCompositeBecomesRoundedRgbInRowOrderWithFixedPixelsAsTheyAre()
        {
            var c = new Composite(320, 240);
            c.Blend(0, 10.4, 10.5, 254.6, 1);         // (0, 0)
            c.Blend(1, -3, 300, 127.49, 1);           // (1, 0): clamped
            c.SetFixed(320, 0);                       // (0, 1): a fixed palette pixel, its exact colour
            c.Blend(320 * 240 - 1, 1, 2, 3, 1);      // (319, 239): the last pixel
            var rgb = AgentLink.ToRgb(c);
            Assert.Equal(230400, rgb.Length);
            Assert.Equal(new byte[] { 10, 11, 255 }, Slice(rgb, 0));
            Assert.Equal(new byte[] { 0, 255, 127 }, Slice(rgb, 1));
            var p = Palette.All[0];
            Assert.Equal(new[] { (byte)p.R, (byte)p.G, (byte)p.B }, Slice(rgb, 320));
            Assert.Equal(new byte[] { 1, 2, 3 }, Slice(rgb, 320 * 240 - 1));
            Assert.Equal(new byte[] { 0, 0, 0 }, Slice(rgb, 2));
        }

        static byte[] Slice(byte[] rgb, int pixel) => new[] { rgb[pixel * 3], rgb[pixel * 3 + 1], rgb[pixel * 3 + 2] };

        [Fact]
        public void OnlyA320x240CompositeConverts()
        {
            Assert.Throws<ArgumentException>(() => AgentLink.ToRgb(new Composite(10, 10)));
        }

        // ---------- the real named pipe ----------

        [Fact]
        public void RealPipeHelloPutOkAndASecondServerIsBusy()
        {
            string name = "KanbanYeah-test-" + Guid.NewGuid().ToString("N");
            using (var link = new AgentLink(name))
            using (var second = new AgentLink(name) { BusyRetry = TimeSpan.FromMilliseconds(100) })
            {
                link.Start();
                WaitUntil(() => link.Status == AgentStatus.Disconnected && PipeExists(name));
                second.Start();
                WaitUntil(() => second.Status == AgentStatus.Busy);

                var pic = Picture();
                string gotLine = null;
                byte[] gotData = null;
                var agent = new Thread(() =>
                {
                    using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut))
                    {
                        c.Connect(5000);
                        var hello = Encoding.ASCII.GetBytes("HELLO 1 editor\n");
                        c.Write(hello, 0, hello.Length);
                        c.Flush();
                        var line = new StringBuilder();
                        for (int b; (b = c.ReadByte()) != '\n' && b >= 0;) line.Append((char)b);
                        gotLine = line.ToString();
                        gotData = new byte[AgentSession.ImageBytes];
                        for (int n = 0; n < gotData.Length;)
                        {
                            int r = c.Read(gotData, n, gotData.Length - n);
                            if (r <= 0) break;
                            n += r;
                        }
                        var ok = Encoding.ASCII.GetBytes("OK\n");
                        c.Write(ok, 0, ok.Length);
                        c.Flush();
                        Thread.Sleep(300); // keep the connection a moment
                    }
                }) { IsBackground = true };
                agent.Start();
                WaitUntil(() => link.Status == AgentStatus.Editor);
                Assert.Equal(PutResult.Ok, link.Put(pic));
                Assert.Equal("PUT 320 240", gotLine);
                Assert.Equal(pic, gotData);
                Assert.True(agent.Join(5000));
                WaitUntil(() => link.Status == AgentStatus.Disconnected); // and listening again
                Assert.Equal(AgentStatus.Busy, second.Status);
            }
        }

        [Fact]
        public void ClosingTheLinkEndsTheConnectionAndTheServer()
        {
            string name = "KanbanYeah-test-" + Guid.NewGuid().ToString("N");
            var link = new AgentLink(name);
            link.Start();
            WaitUntil(() => PipeExists(name));
            using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            {
                c.Connect(5000);
                var hello = Encoding.ASCII.GetBytes("HELLO 1 none\n");
                c.Write(hello, 0, hello.Length);
                WaitUntil(() => link.Status == AgentStatus.NoEditor);
                var closing = Task.Run(() => link.Dispose());
                Assert.True(closing.Wait(3000), "Dispose did not return");
                Assert.Equal(-1, c.ReadByte()); // the game sees the end of the connection
            }
            Assert.Equal(AgentStatus.Disconnected, link.Status);
            Assert.False(PipeExists(name));
        }

        [Fact]
        public void RepeatedStartStopAndConnectDisconnectLeaveNoServerBehind()
        {
            string name = "KanbanYeah-test-" + Guid.NewGuid().ToString("N");
            for (int i = 0; i < 20; i++)
            {
                var link = new AgentLink(name);
                link.Start();
                WaitUntil(() => PipeExists(name));
                Assert.Equal(AgentStatus.Disconnected, link.Status); // not Busy: the previous one left nothing open
                link.Dispose();
                Assert.False(PipeExists(name));
            }
            using (var link = new AgentLink(name))
            {
                link.Start();
                for (int i = 0; i < 20; i++)
                {
                    WaitUntil(() => link.Status == AgentStatus.Disconnected && PipeExists(name));
                    using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut))
                    {
                        c.Connect(5000);
                        var hello = Encoding.ASCII.GetBytes("HELLO 1 editor\n");
                        c.Write(hello, 0, hello.Length);
                        WaitUntil(() => link.Status == AgentStatus.Editor);
                    }
                    WaitUntil(() => link.Status == AgentStatus.Disconnected);
                }
            }
            Assert.False(PipeExists(name)); // a new server can be made again
            using (var s = AgentLink.Create(name)) Assert.NotNull(s);
        }

        static bool PipeExists(string name)
        {
            try { return Directory.GetFiles(@"\\.\pipe\").Contains(@"\\.\pipe\" + name); }
            catch (Exception) { return true; } // listing unsupported: rely on the status
        }

        static void WaitUntil(Func<bool> done, int ms = 5000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (!done())
            {
                if (DateTime.UtcNow >= until)
                {
                    Assert.Fail("timed out");
                }
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>
    /// One end of an in-memory duplex byte stream: reads block until the other end writes or closes (0 = closed).
    /// Disposing either end closes both directions, like a pipe.
    /// </summary>
    internal sealed class DuplexStream : Stream
    {
        sealed class Channel
        {
            public readonly Queue<byte> Bytes = new Queue<byte>();
            public bool Closed;
        }

        readonly Channel inbound, outbound;
        readonly object gate;

        DuplexStream(Channel inbound, Channel outbound, object gate)
        {
            this.inbound = inbound;
            this.outbound = outbound;
            this.gate = gate;
        }

        public static (DuplexStream, DuplexStream) Pair()
        {
            var a = new Channel();
            var b = new Channel();
            var gate = new object();
            return (new DuplexStream(a, b, gate), new DuplexStream(b, a, gate));
        }

        /// <summary>True once the other end closed the connection.</summary>
        public bool ClosedByPeer { get { lock (gate) return inbound.Closed && outbound.Closed; } }

        /// <summary>Bytes waiting to be read.</summary>
        public int Available { get { lock (gate) return inbound.Bytes.Count; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (gate)
            {
                while (inbound.Bytes.Count == 0 && !inbound.Closed) Monitor.Wait(gate);
                int n = 0;
                while (n < count && inbound.Bytes.Count > 0) buffer[offset + n++] = inbound.Bytes.Dequeue();
                return n;
            }
        }

        public int ReadByteBlocking()
        {
            var b = new byte[1];
            return Read(b, 0, 1) == 1 ? b[0] : -1;
        }

        public byte[] ReadExactly(int count)
        {
            var data = new byte[count];
            for (int n = 0; n < count;)
            {
                int r = Read(data, n, count - n);
                if (r == 0) throw new EndOfStreamException();
                n += r;
            }
            return data;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (gate)
            {
                if (outbound.Closed) throw new IOException("closed");
                for (int i = 0; i < count; i++) outbound.Bytes.Enqueue(buffer[offset + i]);
                Monitor.PulseAll(gate);
            }
        }

        public void WriteBytes(byte[] data) => Write(data, 0, data.Length);

        protected override void Dispose(bool disposing)
        {
            lock (gate)
            {
                inbound.Closed = outbound.Closed = true;
                Monitor.PulseAll(gate);
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
