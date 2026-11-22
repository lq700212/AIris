using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using AIris.Core.Config;
using AIris.Core.Model;
using Newtonsoft.Json;

namespace AIris.Core.Store
{
    /// <summary>
    /// JSONL 审计存储：Data\Audit\audit_yyyyMMdd.jsonl 每天一文件、一行一条 JSON（UTF-8）；
    /// 截图 PNG 在 Data\Audit\Shots\。零原生依赖、文件可直接人工查——后续要上 SQLite
    /// （Kaleidoscope KxSqliteStore）时实现 IAuditStore 换掉即可，调用方无感。
    /// 线程安全：内部 lock（检测线程 Append / 审计页 Query 并发）。
    /// </summary>
    public class JsonAuditStore : IAuditStore
    {
        private readonly object _sync = new object();

        public void Append(AuditRecord record)
        {
            if (record == null) return;
            lock (_sync)
            {
                if (!Directory.Exists(AppPaths.AuditDir)) Directory.CreateDirectory(AppPaths.AuditDir);
                var path = Path.Combine(AppPaths.AuditDir, "audit_" + record.Time.ToString("yyyyMMdd") + ".jsonl");
                File.AppendAllText(path,
                    JsonConvert.SerializeObject(record, Formatting.None) + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
        }

        public string SaveShot(Bitmap image)
        {
            try
            {
                lock (_sync)
                {
                    if (!Directory.Exists(AppPaths.AuditShotDir)) Directory.CreateDirectory(AppPaths.AuditShotDir);
                    var name = "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                    var path = Path.Combine(AppPaths.AuditShotDir, name);
                    image.Save(path, ImageFormat.Png);
                    return name;
                }
            }
            catch { return ""; }
        }

        public List<AuditRecord> Query(string dateYmd, string verdict, string recipeName, int maxRows)
        {
            var result = new List<AuditRecord>();
            lock (_sync)
            {
                if (!Directory.Exists(AppPaths.AuditDir)) return result;
                var files = Directory.GetFiles(AppPaths.AuditDir, "audit_*.jsonl");
                // 指定日期只读当天；否则读全部（倒序：先扫新文件）
                if (!string.IsNullOrEmpty(dateYmd))
                    files = files.Where(f => Path.GetFileName(f).Contains(dateYmd)).ToArray();
                Array.Sort(files); Array.Reverse(files);

                foreach (var f in files)
                {
                    foreach (var line in File.ReadAllLines(f, System.Text.Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        AuditRecord r;
                        try { r = JsonConvert.DeserializeObject<AuditRecord>(line); } catch { continue; }
                        if (r == null) continue;
                        if (!string.IsNullOrEmpty(verdict) && verdict != "全部" && !string.Equals(r.Verdict, verdict, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrEmpty(recipeName) && recipeName != "全部配方" && !string.Equals(r.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase)) continue;
                        result.Add(r);
                        if (result.Count >= maxRows) return result;
                    }
                }
            }
            return result;
        }

        public void Dispose() { }
    }
}
