using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AIris.Core.Config
{
    /// <summary>
    /// 精简版 INI 读写（UTF-8 字节级安全，无 Win32 API 依赖）。
    /// 只做 AIris 需要的三件事：Get/Set 字符串、Get/Set 开关（ON/OFF 口径）、节枚举。
    /// 对位 HuaJiVision Core IniFile 的口径（ReadFlag 语义 = null/空/ON 为开，仅显式 OFF 为关），
    /// 但剥离了它服务老版兼容的复杂段；AIris 配置项少，50 行内够用，后续要加再说。
    /// 线程安全：内部 lock，跨线程读写安全（写频低，无性能压力）。
    /// </summary>
    public class IniFile
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public IniFile(string path) { _path = path; }

        /// <summary>读字符串；节/键不存在返回 def。</summary>
        public string Read(string section, string key, string def = "")
        {
            lock (_sync)
            {
                var map = Load();
                string v;
                return map.TryGetValue(section + "\\" + key, out v) ? v : def;
            }
        }

        /// <summary>写字符串；文件不存在自动创建（含目录）。</summary>
        public void Write(string section, string key, string value)
        {
            lock (_sync)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var lines = File.Exists(_path) ? new List<string>(File.ReadAllLines(_path, Encoding.UTF8)) : new List<string>();
                // 定位节区间 [secStart, secEnd)：secEnd = 下一个 [ 节头行或 EOF
                int secStart = -1, secEnd = lines.Count;
                string header = "[" + section + "]";
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (secStart < 0) { if (string.Equals(t, header, StringComparison.OrdinalIgnoreCase)) secStart = i; }
                    else if (t.StartsWith("[") && t.EndsWith("]")) { secEnd = i; break; }
                }
                string pair = key + "=" + (value ?? "");
                if (secStart < 0)
                {
                    // 新节：追加空行 + 节头 + 键值
                    if (lines.Count > 0) lines.Add("");
                    lines.Add(header); lines.Add(pair);
                }
                else
                {
                    // 节内找键覆盖，找不到插在节末（保持节内顺序稳定）
                    bool done = false;
                    for (int i = secStart + 1; i < secEnd; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.StartsWith("[") && t.EndsWith("]")) break;
                        int eq = t.IndexOf('=');
                        if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        { lines[i] = pair; done = true; break; }
                    }
                    if (!done) lines.Insert(secEnd, pair);
                }
                File.WriteAllLines(_path, lines, Encoding.UTF8);
            }
        }

        /// <summary>开关口径：null/空/ON=开（defaultOn 控制 null/空 档），仅显式 OFF=关。</summary>
        public bool ReadFlag(string section, string key, bool defaultOn)
        {
            string v = Read(section, key, null);
            if (string.IsNullOrEmpty(v)) return defaultOn;
            return !string.Equals(v.Trim(), "OFF", StringComparison.OrdinalIgnoreCase);
        }

        public void WriteFlag(string section, string key, bool on)
        {
            Write(section, key, on ? "ON" : "OFF");
        }

        /// <summary>整文件装载为 "节\键" → 值 平铺字典（节头必须 [name]，键值用第一个 = 切分）。</summary>
        private Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_path)) return map;
            string sec = "";
            foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
                if (t.StartsWith("[") && t.EndsWith("]")) { sec = t.Substring(1, t.Length - 2).Trim(); continue; }
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                map[sec + "\\" + t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
            }
            return map;
        }
    }
}
