using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KanbanDrawer.UI
{
    /// <summary>key=value settings in AppPaths.DataDir\settings.txt (missing / broken file = defaults).</summary>
    internal sealed class Settings
    {
        readonly Dictionary<string, string> map = new Dictionary<string, string>();

        static string PathOf() => Path.Combine(Win.AppPaths.DataDir, "settings.txt");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                foreach (var line in File.ReadAllLines(PathOf(), Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) s.map[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                var path = PathOf();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                foreach (var kv in map) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public string Get(string key, string def) => map.TryGetValue(key, out var v) ? v : def;
        public int Get(string key, int def) => int.TryParse(Get(key, null), out int v) ? v : def;
        public void Set(string key, object value) => map[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
