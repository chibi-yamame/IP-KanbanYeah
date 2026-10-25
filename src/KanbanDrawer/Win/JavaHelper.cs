using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// Finds the Java that runs InterPot3 and starts helper processes with it, tied to this process.
    /// InterPot3's shortcut points at InterPot3.jar, so the Java is whatever opens .jar files: looked up with
    /// AssocQueryString, which applies the same rules as Explorer (the user's own choice before the machine default).
    /// </summary>
    internal static class JavaHelper
    {
        /// <summary>java.exe (console, no window when redirected) or javaw.exe of the .jar association, or null.</summary>
        public static string FindJava()
        {
            string exe = AssocExecutable(".jar");
            if (exe == null || !File.Exists(exe)) return null;
            string name = Path.GetFileName(exe).ToLowerInvariant();
            if (name != "javaw.exe" && name != "java.exe") return null; // .jar opened by something else (an archiver)
            string java = Path.Combine(Path.GetDirectoryName(exe), "java.exe");
            return File.Exists(java) ? java : exe;
        }

        static string AssocExecutable(string ext)
        {
            const int ASSOCSTR_EXECUTABLE = 2;
            uint size = 1024;
            var sb = new StringBuilder((int)size);
            int hr = AssocQueryString(0, ASSOCSTR_EXECUTABLE, ext, "open", sb, ref size);
            return hr == 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Writes an embedded resource to %LOCALAPPDATA%\KanbanDrawer (only when it differs; another instance may use it).
        /// </summary>
        public static string Extract(string resource, string fileName)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KanbanDrawer");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            byte[] data;
            using (var res = typeof(JavaHelper).Assembly.GetManifestResourceStream(resource)
                             ?? throw new InvalidOperationException("resource missing: " + resource))
            using (var ms = new MemoryStream())
            {
                res.CopyTo(ms);
                data = ms.ToArray();
            }
            try
            {
                if (!File.Exists(path) || !Same(File.ReadAllBytes(path), data)) File.WriteAllBytes(path, data);
            }
            catch (IOException) when (File.Exists(path)) { }
            return path;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Starts a hidden process with redirected standard streams that ends when this process ends.</summary>
        public static Process Start(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.ASCII,
            };
            var p = Process.Start(psi);
            p.ErrorDataReceived += (s, e) => { };
            p.BeginErrorReadLine();
            try { AssignProcessToJobObject(Job, p.Handle); } catch (Win32Exception) { }
            return p;
        }

        // One job for all helpers: closing its last handle (this process exiting, even when killed) ends them.
        static readonly IntPtr Job = CreateKillOnCloseJob();

        static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr ptr = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)len);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder outStr, ref uint outLen);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint len);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
