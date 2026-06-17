using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GUI_Library
{
    /// <summary>
    /// Traad-sikker audit-logger. En CSV-fil per dag i en fast mappe.
    /// Filnavn: audit_YYYY-MM-DD.csv. Kolonner:
    /// Timestamp, User, Category, Action, Detail, OldValue, NewValue, Result.
    ///
    /// ROBUSTHED (vigtigt for audit):
    /// - Normalt APPENDES kun til hovedfilen (hurtigt og sikkert).
    /// - Hvis filen er laast (fx aaben i Excel), gemmes posten i en fallback-fil
    ///   (audit_YYYY-MM-DD.pending.csv) saa intet tabes, og HasPending bliver true.
    /// - Naar filen er fri igen (Excel lukket), kan MergePending() flette de
    ///   ventende poster ind OG SORTERE hele filen efter tidsstempel, saa
    ///   kronologien altid er korrekt uanset hvornaar fletningen sker.
    ///
    /// YDELSE: timeren skal kun kalde MergePending() naar HasPending er true
    /// (en gratis bool-tjek). Saa laver loggeren ingen disk-I/O i drift naar der
    /// ikke er noget at flette.
    /// </summary>
    public static class AuditLogger
    {
        private static readonly string LogDir = @"C:\1349\AuditLog";
        private static readonly object _lock = new object();

        private const string Header =
            "Timestamp,User,Category,Action,Detail,OldValue,NewValue,Result";

        private const int MaxRetries = 5;
        private const int RetryDelayMs = 150;

        // True naar der ligger ventende poster i en fallback-fil der endnu ikke er
        // flettet ind. Timeren kan laese denne GRATIS hvert sekund og kun kalde
        // MergePending() naar den er true.
        public static bool HasPending { get; private set; }

        // UI kan abonnere for at vise en advarsel naar hovedfilen ikke kunne skrives.
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

                // Normal vej: APPEND til hovedfilen (hurtigt, sikkert).
                if (TryAppend(path, line, headerIfNew: true))
                    return;

                // Hovedfilen er laast (Excel). Gem i fallback saa intet tabes.
                string fallback = Path.Combine(LogDir, "audit_" + dateTag + ".pending.csv");
                bool fallbackOk = TryAppend(fallback, line, headerIfNew: true);
                if (fallbackOk) HasPending = true;

                string msg = fallbackOk
                    ? "Audit-loggen kunne ikke skrives (filen er maaske aaben i Excel). " +
                      "Posten er gemt midlertidigt og flettes ind naar filen lukkes."
                    : "KRITISK: audit-posten kunne hverken skrives til hovedfil eller fallback.";

                OnWriteFailed?.Invoke(msg);
            }
        }

        // Fletter ventende fallback-poster ind i hovedfilen OG sorterer hele filen
        // efter tidsstempel, saa kronologien er korrekt. Kaldes kun naar HasPending
        // er true OG hovedfilen er fri (ikke laast af Excel).
        // Returnerer true hvis fletning lykkedes (eller intet at flette).
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

                    // Tjek at hovedfilen er fri at skrive til FOER vi roerer noget.
                    // Hvis Excel stadig holder den, giver vi op (proeves naeste gang).
                    if (File.Exists(path) && IsLocked(path))
                        return false;

                    // Saml alle datalinjer (uden header) fra begge filer.
                    var rows = new List<string>();
                    if (File.Exists(path))
                        rows.AddRange(ReadDataLines(path));
                    rows.AddRange(ReadDataLines(fallback));

                    if (rows.Count == 0) { TryDelete(fallback); HasPending = false; return true; }

                    // Sortér efter tidsstempel (foerste felt, format yyyy-MM-dd HH:mm:ss).
                    // Stabilt: poster med samme tid beholder indbyrdes raekkefoelge.
                    var sorted = rows
                        .Select((text, idx) => new { text, idx, ts = ParseTimestamp(text) })
                        .OrderBy(r => r.ts).ThenBy(r => r.idx)
                        .Select(r => r.text)
                        .ToList();

                    // Skriv hele filen om: header + sorterede linjer.
                    var sb = new StringBuilder();
                    sb.AppendLine(Header);
                    foreach (var r in sorted) sb.AppendLine(r);

                    // Skriv via temp-fil og erstat, saa en halv skrivning aldrig
                    // efterlader filen oedelagt.
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
                    return false;  // proeves igen naeste gang HasPending tjekkes
                }
            }
        }

        // ---------- hjaelpere ----------

        private static IEnumerable<string> ReadDataLines(string file)
        {
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i] == Header) continue;   // spring header over
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    yield return lines[i];
            }
        }

        private static DateTime ParseTimestamp(string row)
        {
            // Foerste felt er tidsstemplet. Det er ikke escaped (ingen komma i datoen),
            // saa vi kan tage alt foer foerste komma.
            int comma = row.IndexOf(',');
            string ts = comma > 0 ? row.Substring(0, comma) : row;
            return DateTime.TryParse(ts, out var dt) ? dt : DateTime.MinValue;
        }

        private static bool IsLocked(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;  // kunne aabnes eksklusivt -> ikke laast
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