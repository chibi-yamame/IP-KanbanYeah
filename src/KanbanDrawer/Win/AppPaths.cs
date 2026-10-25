using System;
using System.IO;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// Where settings and libraries live: %APPDATA%\KanbanDrawer, or the folder in the KANBANDRAWER_DATA environment
    /// variable (tests and portable use).
    /// </summary>
    public static class AppPaths
    {
        public static string DataDir
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("KANBANDRAWER_DATA");
                return !string.IsNullOrEmpty(env)
                    ? env
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KanbanDrawer");
            }
        }
    }
}
