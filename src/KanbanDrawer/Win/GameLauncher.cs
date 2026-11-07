using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// Starts InterPot3 with the full-colour agent (<c>-javaagent</c>) and makes a desktop shortcut that does the same
    /// (docs/design/fullcolor.md「ゲームの起動とショートカット」). The game is not tied to this process: it keeps running
    /// when 看板屋 closes.
    /// </summary>
    internal static class GameLauncher
    {
        public const string DefaultGameJar = @"C:\Program Files (x86)\InterPot3\InterPot3.jar";
        public const string AgentResource = "KanbanDrawer.kanban-agent.jar";
        public const string ShortcutName = "InterPot3 (屋).lnk";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>
        /// InterPot3.jar (docs/design/fullcolor.md「InterPot3.jar の場所」): the configured path (setting <c>fullcolor.jar</c>),
        /// else the auto-detected install (<see cref="FindInstalled"/>, used silently and not saved), else the user picks it.
        /// Only existing files count; null when none (dialog cancelled). Saving a picked path is the caller's job.
        /// </summary>
        public static string LocateGame(string configured, IWin32Window owner)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            string found = FindInstalled();
            if (found != null) return found;
            using (var d = new OpenFileDialog { Title = "InterPot3.jar を選んでください", Filter = "InterPot3.jar|InterPot3.jar|jar|*.jar", FileName = "InterPot3.jar" })
                return d.ShowDialog(owner) == DialogResult.OK && File.Exists(d.FileName) ? d.FileName : null;
        }

        /// <summary>
        /// The installed InterPot3.jar: the uninstall entry named InterPot3 (64-bit then 32-bit registry view, read only),
        /// else the default install folder. Null when neither exists. Writes nothing.
        /// </summary>
        public static string FindInstalled()
        {
            string found = PickInstallLocation(UninstallEntries(), File.Exists);
            if (found != null) return found;
            return File.Exists(DefaultGameJar) ? DefaultGameJar : null;
        }

        /// <summary>
        /// The first entry whose DisplayName is InterPot3 (case-insensitive) and whose InstallLocation (quotes and trailing
        /// backslashes removed) holds an existing InterPot3.jar: that jar's path, or null.
        /// </summary>
        public static string PickInstallLocation(IEnumerable<(string DisplayName, string InstallLocation)> entries, Func<string, bool> exists)
        {
            foreach (var (name, location) in entries)
            {
                if (!string.Equals(name?.Trim(), "InterPot3", StringComparison.OrdinalIgnoreCase)) continue;
                string dir = location?.Trim().Trim('"').Trim().TrimEnd('\\');
                if (string.IsNullOrEmpty(dir)) continue;
                string jar;
                try { jar = Path.Combine(dir, "InterPot3.jar"); }
                catch (ArgumentException) { continue; } // invalid characters in a hand-edited value
                if (exists(jar)) return jar;
            }
            return null;
        }

        /// <summary>
        /// (DisplayName, InstallLocation) of HKLM\...\Uninstall\* in the 64-bit then the 32-bit view. Read only
        /// (<c>OpenSubKey(name, false)</c>), every key disposed, registry errors ignored. Collected eagerly so no key stays open.
        /// </summary>
        static List<(string, string)> UninstallEntries()
        {
            var list = new List<(string, string)>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstall = hklm.OpenSubKey(UninstallKey, false))
                    {
                        if (uninstall == null) continue;
                        foreach (string sub in uninstall.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = uninstall.OpenSubKey(sub, false))
                                {
                                    if (k?.GetValue("DisplayName") is string name && k.GetValue("InstallLocation") is string location)
                                        list.Add((name, location));
                                }
                            }
                            catch (Exception) { } // an unreadable entry
                        }
                    }
                }
                catch (Exception) { } // no such view / no access
            }
            return list;
        }

        /// <summary>True when this build carries the agent jar (it is made separately, Java/KanbanAgent.java).</summary>
        public static bool HasAgent()
        {
            using (var s = typeof(GameLauncher).Assembly.GetManifestResourceStream(AgentResource)) return s != null;
        }

        /// <summary>Writes the agent to %LOCALAPPDATA%\KanbanDrawer\kanban-agent.jar (only when it differs); null when not built in.</summary>
        public static string ExtractAgent() => HasAgent() ? JavaHelper.Extract(AgentResource, "kanban-agent.jar") : null;

        public static string Arguments(string agentJar, string gameJar)
            => $"-javaagent:\"{agentJar}\" -jar \"{gameJar}\"";

        /// <summary>「InterPot を起動」: javaw with the agent, working folder = the game's folder. Logs what happened.</summary>
        public static bool Launch(string gameJar, Action<string> log)
        {
            string java = JavaHelper.FindJavaw();
            if (java == null) { log("InterPot を動かす Java（.jar を開くプログラム）が見つかりません"); return false; }
            try
            {
                string agent = ExtractAgent();
                if (agent == null) { log(NoAgent); return false; }
                var psi = new ProcessStartInfo(java, Arguments(agent, gameJar))
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(gameJar),
                };
                Process.Start(psi)?.Dispose(); // not in JavaHelper's job: the game outlives 看板屋
            }
            catch (Exception e)
            {
                log("起動できませんでした: " + e.Message);
                return false;
            }
            log("InterPot を起動しました（フルカラー用）。ログインして看板の編集画面を開くと、状態が「接続中: エディタあり」になります");
            return true;
        }

        /// <summary>「ショートカットを作成」: the same command as <see cref="Launch"/> as a .lnk on the desktop (overwritten).</summary>
        public static bool CreateShortcut(string gameJar, Action<string> log)
        {
            string java = JavaHelper.FindJavaw();
            if (java == null) { log("InterPot を動かす Java（.jar を開くプログラム）が見つかりません"); return false; }
            object shell = null, link = null;
            try
            {
                string agent = ExtractAgent();
                if (agent == null) { log(NoAgent); return false; }
                string dir = Path.GetDirectoryName(gameJar);
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
                // WScript.Shell, late bound (no reference to add)
                var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell がありません");
                shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var lt = link.GetType();
                void Set(string name, string value) => lt.InvokeMember(name, BindingFlags.SetProperty, null, link, new object[] { value });
                Set("TargetPath", java);
                Set("Arguments", Arguments(agent, gameJar));
                Set("WorkingDirectory", dir);
                Set("IconLocation", IconFor(dir, java) + ",0");
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                log("デスクトップにIP起動用リンク「InterPot3 (屋)」を生成しました");
                return true;
            }
            catch (Exception e)
            {
                log("ショートカットを作れませんでした: " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException.Message : e.Message));
                return false;
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>
        /// The game's own icon: interpot3.ico in its folder (what the installer's shortcuts use), else the first .ico there,
        /// else the Java. Not an .exe: the folder's only one is the uninstaller.
        /// </summary>
        static string IconFor(string dir, string java)
        {
            try
            {
                string own = Path.Combine(dir, "interpot3.ico");
                if (File.Exists(own)) return own;
                return Directory.GetFiles(dir, "*.ico").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? java;
            }
            catch (Exception) { return java; }
        }

        const string NoAgent = "この看板屋にはフルカラー用の連携プログラム（kanban-agent.jar）が入っていません";
    }
}
