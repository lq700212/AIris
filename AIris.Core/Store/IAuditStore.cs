using System;
using System.Collections.Generic;
using System.Drawing;
using AIris.Core.Model;

namespace AIris.Core.Store
{
    /// <summary>
    /// 审计存储接入面（接口隔离：当前 JSONL 实现，将来要上 SQLite/KxSqliteStore 时
    /// 换实现不动 RunMonitor 与 UI）。
    /// </summary>
    public interface IAuditStore : IDisposable
    {
        /// <summary>追加一条判定记录（判定最终结论时由 RunMonitor 调）。</summary>
        void Append(AuditRecord record);

        /// <summary>保存截图 PNG，返回文件名（存档记录引用；失败返回空串不抛）。</summary>
        string SaveShot(Bitmap image);

        /// <summary>查询记录（全部为空串/null = 不过滤该维；按时间倒序）。</summary>
        List<AuditRecord> Query(string dateYmd, string verdict, string recipeName, int maxRows);
    }
}
