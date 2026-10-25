using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DicomVUZB
{
    public static class ZipArchiveService
    {
        private static readonly string BaseTempDirectory = Path.Combine(Path.GetTempPath(), "DicomVUZB_TempZip");

        public static bool IsZipFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 4)
                        return false;

                    return fs.ReadByte() == 0x50 &&
                           fs.ReadByte() == 0x4B &&
                           fs.ReadByte() == 0x03 &&
                           fs.ReadByte() == 0x04;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string ExtractZip(string zipFilePath, Action<string> progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
                throw new FileNotFoundException("Файл архива не найден.", zipFilePath);

            if (!Directory.Exists(BaseTempDirectory))
            {
                Directory.CreateDirectory(BaseTempDirectory);
            }

            var safePrefix = SanitizeFolderName(Path.GetFileNameWithoutExtension(zipFilePath));
            if (string.IsNullOrWhiteSpace(safePrefix))
                safePrefix = "study";

            var targetDir = Path.Combine(BaseTempDirectory, string.Format("{0}_{1}", safePrefix, Guid.NewGuid().ToString("N").Substring(0, 8)));
            Directory.CreateDirectory(targetDir);

            Encoding encoding;
            try
            {
                encoding = Encoding.GetEncoding(866);
            }
            catch
            {
                encoding = Encoding.Default;
            }

            using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Read, encoding))
            {
                var totalEntries = archive.Entries.Count;
                var current = 0;

                foreach (var entry in archive.Entries)
                {
                    current++;

                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Пропуск служебных файлов macOS / Windows
                    if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Защита от Zip Slip (Directory Traversal)
                    var destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!destPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        Directory.CreateDirectory(parentDir);

                    entry.ExtractToFile(destPath, true);

                    if (progressCallback != null && (current % 25 == 0 || current == totalEntries))
                    {
                        progressCallback(string.Format("Распаковка архива: {0}/{1} файлов...", current, totalEntries));
                    }
                }
            }

            // Автоматическая распаковка вложенных ZIP-архивов (актуально для экспорта из PACS)
            ExtractNestedZips(targetDir, 0, progressCallback);

            return targetDir;
        }

        private static void ExtractNestedZips(string rootDir, int depth, Action<string> progressCallback)
        {
            if (depth >= 3 || !Directory.Exists(rootDir))
                return;

            var nestedZips = Directory.GetFiles(rootDir, "*.zip", SearchOption.AllDirectories).ToList();
            foreach (var nestedZip in nestedZips)
            {
                try
                {
                    var parent = Path.GetDirectoryName(nestedZip) ?? rootDir;
                    var name = Path.GetFileNameWithoutExtension(nestedZip);
                    var subTarget = Path.Combine(parent, name);
                    if (!Directory.Exists(subTarget))
                        Directory.CreateDirectory(subTarget);

                    using (var archive = ZipFile.OpenRead(nestedZip))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            var dest = Path.GetFullPath(Path.Combine(subTarget, entry.FullName));
                            if (!dest.StartsWith(Path.GetFullPath(subTarget), StringComparison.OrdinalIgnoreCase))
                                continue;

                            var entryParent = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(entryParent) && !Directory.Exists(entryParent))
                                Directory.CreateDirectory(entryParent);

                            entry.ExtractToFile(dest, true);
                        }
                    }

                    try { File.Delete(nestedZip); } catch { }
                }
                catch
                {
                    // Игнорируем ошибки распаковки отдельных вложенных архивов
                }
            }

            // Проверяем также вложенные RAR
            var nestedRars = Directory.GetFiles(rootDir, "*.rar", SearchOption.AllDirectories).ToList();
            foreach (var rar in nestedRars)
            {
                try
                {
                    var parent = Path.GetDirectoryName(rar) ?? rootDir;
                    var name = Path.GetFileNameWithoutExtension(rar);
                    var subTarget = Path.Combine(parent, name);
                    if (!Directory.Exists(subTarget))
                        Directory.CreateDirectory(subTarget);

                    string extractorType;
                    var extractorExe = RarArchiveService.FindExtractorExecutable(out extractorType);
                    if (!string.IsNullOrEmpty(extractorExe))
                    {
                        string args = extractorType == "UnRAR"
                            ? string.Format("x -y -o+ \"{0}\" \"{1}\\\"", rar, subTarget)
                            : string.Format("x -ibck -inul -o+ -y \"{0}\" \"{1}\\\"", rar, subTarget);

                        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = extractorExe,
                            Arguments = args,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        }))
                        {
                            p?.WaitForExit(60000);
                        }
                    }
                    try { File.Delete(rar); } catch { }
                }
                catch { }
            }

            if (nestedZips.Count > 0 || nestedRars.Count > 0)
            {
                ExtractNestedZips(rootDir, depth + 1, progressCallback);
            }
        }

        public static void CleanupDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch { }
                }

                Directory.Delete(directoryPath, true);
            }
            catch
            {
                // Заблокированные файлы будут удалены при следующей очистке
            }
        }

        public static void CleanupOrphanedTempDirectories()
        {
            try
            {
                if (!Directory.Exists(BaseTempDirectory))
                    return;

                var dirs = Directory.GetDirectories(BaseTempDirectory);
                foreach (var dir in dirs)
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if (DateTime.Now - info.CreationTime > TimeSpan.FromMinutes(30))
                        {
                            CleanupDirectory(dir);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "study";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (Array.IndexOf(invalidChars, ch) < 0 && ch != ' ')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }
    }
}

