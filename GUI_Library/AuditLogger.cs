using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GUI_Library
{
    /// <summary>
    /// Thread-safe audit logger. One CSV file per day in a fixed folder.
    /// File name: audit_YYYY-MM-DD.csv. Columns:
    /// Timestamp, User, Category, Action, Detail, OldValue, NewValue, Result.
    ///
    /// ROBUSTNESS (important for audit):
    /// - Normally only APPENDS to the main file (fast and safe).
    /// - If the file is locked (e.g. open in Excel), the entry is saved to a
    ///   fallback file (audit_YYYY-MM-DD.pending.csv) so nothing is lost, and
    ///   HasPending becomes true.
    /// - When the file is free again (Excel closed), MergePending() can merge the
    ///   pending entries in AND SORT the whole file by timestamp, so the
    ///   chronology is always correct regardless of when the merge happens.
    ///
    /// PERFORMANCE: the timer should only call MergePending() when HasPending is
    /// true (a free bool check). That way the logger does no disk I/O during
    /// operation when there is nothing to merge.
    /// </summary>
    public static class AuditLogger
    {
        private static readonly string LogDir = @"C:\1349\AuditLog";
        private static readonly object _lock = new object();

        private const string Header =
            "Timestamp,User,Category,Action,Detail,OldValue,NewValue,Result";

        private const int MaxRetries = 5;
        private const int RetryDelayMs = 150;

        // True when there are pending entries in a fallback file that have not yet
        // been merged in. The timer can read this for FREE every second and only
        // call MergePending() when it is true.
        public static bool HasPending { get; private set; }

        // The UI can subscribe to show a warning when the main file could not be written.
        public static event Action<string> OnWriteFailed;

        public static void Log(
            string user,
            string category,
            string action,
            string detail = "",
            string oldValue = "",
            string newValue = "",
            string result = "")
        {
            string line =
                Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," +
                Escape(user) + "," +
                Escape(category) + "," +
                Escape(action) + "," +
                Escape(detail) + "," +
                Escape(oldValue) + "," +
                Escape(newValue) + "," +
                Escape(result) + Environment.NewLine;

            lock (_lock)
            {
                try { Directory.CreateDirectory(LogDir); } catch { }

                string dateTag = DateTime.Now.ToString("yyyy-MM-dd");
                string path = Path.Combine(LogDir, "audit_" + dateTag + ".csv");

                // Normal path: APPEND to the main file (fast, safe).
                if (TryAppend(path, line, headerIfNew: true))
                    return;

                // The main file is locked (Excel). Save to fallback so nothing is lost.
                string fallback = Path.Combine(LogDir, "audit_" + dateTag + ".pending.csv");
                bool fallbackOk = TryAppend(fallback, line, headerIfNew: true);
                if (fallbackOk) HasPending = true;

                string msg = fallbackOk
                    ? "The audit log could not be written (the file may be open in Excel). " +
                      "The entry has been saved temporarily and will be merged in when the file is closed."
                    : "CRITICAL: the audit entry could not be written to either the main file or the fallback.";

                OnWriteFailed?.Invoke(msg);
            }
        }

        // Merges pending fallback entries into the main file AND sorts the whole
        // file by timestamp, so the chronology is correct. Called only when
        // HasPending is true AND the main file is free (not locked by Excel).
        // Returns true if the merge succeeded (or there was nothing to merge).
        public static bool MergePending()
        {
            lock (_lock)
            {
                try
                {
                    string dateTag = DateTime.Now.ToString("yyyy-MM-dd");
                    string path = Path.Combine(LogDir, "audit_" + dateTag + ".csv");
                    string fallback = Path.Combine(LogDir, "audit_" + dateTag + ".pending.csv");

                    if (!File.Exists(fallback)) { HasPending = false; return true; }

                    // Check that the main file is free to write to BEFORE we touch
                    // anything. If Excel still holds it, we give up (retried next time).
                    if (File.Exists(path) && IsLocked(path))
                        return false;

                    // Collect all data lines (without header) from both files.
                    var rows = new List<string>();
                    if (File.Exists(path))
                        rows.AddRange(ReadDataLines(path));
                    rows.AddRange(ReadDataLines(fallback));

                    if (rows.Count == 0) { TryDelete(fallback); HasPending = false; return true; }

                    // Sort by timestamp (first field, format yyyy-MM-dd HH:mm:ss).
                    // Stable: entries with the same time keep their relative order.
                    var sorted = rows
                        .Select((text, idx) => new { text, idx, ts = ParseTimestamp(text) })
                        .OrderBy(r => r.ts).ThenBy(r => r.idx)
                        .Select(r => r.text)
                        .ToList();

                    // Rewrite the whole file: header + sorted lines.
                    var sb = new StringBuilder();
                    sb.AppendLine(Header);
                    foreach (var r in sorted) sb.AppendLine(r);

                    // Write via temp file and replace, so a half-written file never
                    // leaves the file corrupted.
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);

                    TryDelete(fallback);
                    HasPending = false;
                    return true;
                }
                catch
                {
                    return false;  // retried next time HasPending is checked
                }
            }
        }

        // ---------- helpers ----------

        private static IEnumerable<string> ReadDataLines(string file)
        {
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i] == Header) continue;   // skip header
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    yield return lines[i];
            }
        }

        private static DateTime ParseTimestamp(string row)
        {
            // The first field is the timestamp. It is not escaped (no comma in the
            // date), so we can take everything before the first comma.
            int comma = row.IndexOf(',');
            string ts = comma > 0 ? row.Substring(0, comma) : row;
            return DateTime.TryParse(ts, out var dt) ? dt : DateTime.MinValue;
        }

        private static bool IsLocked(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;  // could be opened exclusively -> not locked
            }
            catch (IOException) { return true; }
            catch { return true; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static bool TryAppend(string path, string content, bool headerIfNew)
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    bool newFile = !File.Exists(path);
                    using (var fs = new FileStream(path, FileMode.Append,
                                                   FileAccess.Write, FileShare.Read))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        if (newFile && headerIfNew)
                            sw.WriteLine(Header);
                        sw.Write(content);
                    }
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            bool needsQuotes = field.Contains(",") || field.Contains("\"")
                               || field.Contains("\n") || field.Contains("\r");
            if (!needsQuotes) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}