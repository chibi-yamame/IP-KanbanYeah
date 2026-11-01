using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        public const string ShortcutName = "InterPot3 (フルカラー).lnk";

        /// <summary>
        /// InterPot3.jar: the configured path (setting <c>fullcolor.jar</c>), else the default install, else the user picks
        /// it. Only existing files count; null when none (dialog cancelled).
        /// </summary>
        public static string LocateGame(string configured, IWin32Window owner)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            if (File.Exists(DefaultGameJar)) return DefaultGameJar;
            using (var d = new OpenFileDialog { Title = "InterPot3.jar を選んでください", Filter = "InterPot3.jar|InterPot3.jar|jar|*.jar", FileName = "InterPot3.jar" })
                return d.ShowDialog(owner) == DialogResult.OK && File.Exists(d.FileName) ? d.FileName : null;
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
                log("ショートカットを作りました: " + path);
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
