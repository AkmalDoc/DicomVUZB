using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace DicomVUZB
{
    public static class RarArchiveService
    {
        private static readonly string BaseTempDirectory = Path.Combine(Path.GetTempPath(), "DicomVUZB_TempZip");

        public static bool IsRarFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            if (string.Equals(Path.GetExtension(path), ".rar", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 7)
                        return false;

                    // RAR 4.x сигнатура: 52 61 72 21 1A 07 00
                    // RAR 5.x сигнатура: 52 61 72 21 1A 07 01 00
                    var b0 = fs.ReadByte();
                    var b1 = fs.ReadByte();
                    var b2 = fs.ReadByte();
                    var b3 = fs.ReadByte();
                    var b4 = fs.ReadByte();
                    var b5 = fs.ReadByte();
                    var b6 = fs.ReadByte();

                    return b0 == 0x52 && b1 == 0x61 && b2 == 0x72 && b3 == 0x21 &&
                           b4 == 0x1A && b5 == 0x07 && (b6 == 0x00 || b6 == 0x01);
                }
            }
            catch
            {
                return false;
            }
        }

        public static string FindExtractorExecutable(out string extractorType)
        {
            extractorType = null;

            // 1. Поиск UnRAR.exe рядом с исполняемым файлом приложения
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localCandidates = new[]
            {
                Path.Combine(baseDir, "UnRAR.exe"),
                Path.Combine(baseDir, "unrar.exe"),
                Path.Combine(baseDir, "WinRAR.exe"),
                Path.Combine(baseDir, "winrar.exe"),
                Path.Combine(baseDir, "..", "UnRAR.exe"),
                Path.Combine(baseDir, "..", "..", "UnRAR.exe")
            };

            foreach (var loc in localCandidates)
            {
                try
                {
                    var full = Path.GetFullPath(loc);
                    if (File.Exists(full))
                    {
                        extractorType = full.IndexOf("winrar", StringComparison.OrdinalIgnoreCase) >= 0 ? "WinRAR" : "UnRAR";
                        return full;
                    }
                }
                catch { }
            }

            // 2. Стандартные пути установки WinRAR и 7-Zip
            var standardPaths = new[]
            {
                new { Path = @"C:\Program Files (x86)\WinRAR\UnRAR.exe", Type = "UnRAR" },
                new { Path = @"C:\Program Files\WinRAR\UnRAR.exe", Type = "UnRAR" },
                new { Path = @"C:\Program Files (x86)\WinRAR\WinRAR.exe", Type = "WinRAR" },
                new { Path = @"C:\Program Files\WinRAR\WinRAR.exe", Type = "WinRAR" },
                new { Path = @"C:\Program Files\7-Zip\7z.exe", Type = "7z" },
                new { Path = @"C:\Program Files (x86)\7-Zip\7z.exe", Type = "7z" }
            };

            foreach (var sp in standardPaths)
            {
                if (File.Exists(sp.Path))
                {
                    extractorType = sp.Type;
                    return sp.Path;
                }
            }

            // 3. Поиск в реестре Windows App Paths (32-bit и 64-bit)
            var regWinrar = CheckRegistryAppPath("WinRAR.exe");
            if (!string.IsNullOrEmpty(regWinrar) && File.Exists(regWinrar))
            {
                extractorType = "WinRAR";
                return regWinrar;
            }

            var reg7z = CheckRegistryAppPath("7z.exe");
            if (!string.IsNullOrEmpty(reg7z) && File.Exists(reg7z))
            {
                extractorType = "7z";
                return reg7z;
            }

            // 4. Поиск в системной переменной PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var searchExes = new[] { "unrar.exe", "winrar.exe", "7z.exe" };
                foreach (var folder in pathEnv.Split(';'))
                {
                    var cleanFolder = folder.Trim().Trim('\"');
                    if (string.IsNullOrEmpty(cleanFolder) || !Directory.Exists(cleanFolder))
                        continue;

                    foreach (var exe in searchExes)
                    {
                        var full = Path.Combine(cleanFolder, exe);
                        if (File.Exists(full))
                        {
                            extractorType = exe.IndexOf("7z", StringComparison.OrdinalIgnoreCase) >= 0 ? "7z" :
                                           exe.IndexOf("unrar", StringComparison.OrdinalIgnoreCase) >= 0 ? "UnRAR" : "WinRAR";
                            return full;
                        }
                    }
                }
            }

            return null;
        }

        private static string CheckRegistryAppPath(string appName)
        {
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
            var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + appName))
                        {
                            if (key != null)
                            {
                                var val = key.GetValue(null) as string;
                                if (!string.IsNullOrEmpty(val))
                                {
                                    val = val.Trim('\"');
                                    if (File.Exists(val))
                                        return val;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        public static string ExtractRar(string rarFilePath, Action<string> progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(rarFilePath) || !File.Exists(rarFilePath))
                throw new FileNotFoundException("Файл RAR-архива не найден.", rarFilePath);

            string extractorType;
            var extractorExe = FindExtractorExecutable(out extractorType);
            if (string.IsNullOrEmpty(extractorExe))
            {
                throw new FileNotFoundException(
                    "Для распаковки RAR-архива не найден распаковщик.\nУбедитесь, что установлена программа WinRAR, 7-Zip или скопирован UnRAR.exe в папку программы.");
            }

            if (!Directory.Exists(BaseTempDirectory))
            {
                Directory.CreateDirectory(BaseTempDirectory);
            }

            var safePrefix = SanitizeFolderName(Path.GetFileNameWithoutExtension(rarFilePath));
            if (string.IsNullOrWhiteSpace(safePrefix))
                safePrefix = "study";

            var targetDir = Path.Combine(BaseTempDirectory, string.Format("rar_{0}_{1}", safePrefix, Guid.NewGuid().ToString("N").Substring(0, 8)));
            Directory.CreateDirectory(targetDir);

            if (progressCallback != null)
                progressCallback("Распаковка RAR-архива: " + Path.GetFileName(rarFilePath) + "...");

            // ВАЖНО: целевой путь не должен оканчиваться на обратный слэш внутри кавычек,
            // чтобы избежать превращения \" в литеральную кавычку в аргументах
            var cleanTarget = targetDir.TrimEnd('\\');

            string arguments;
            if (extractorType == "UnRAR")
            {
                arguments = string.Format("x -y -o+ -kb -inul \"{0}\" \"{1}\"", rarFilePath, cleanTarget);
            }
            else if (extractorType == "WinRAR")
            {
                arguments = string.Format("x -ibck -inul -o+ -kb -y \"{0}\" \"{1}\"", rarFilePath, cleanTarget);
            }
            else // 7z
            {
                arguments = string.Format("x -y -bso0 -bsp0 -o\"{0}\" \"{1}\"", cleanTarget, rarFilePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = extractorExe,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new InvalidOperationException("Не удалось запустить процесс распаковщика: " + extractorExe);

                if (!process.WaitForExit(180000)) // 3 минуты таймаут
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Превышено время ожидания распаковки RAR-архива (более 3 минут).");
                }
            }

            var hasExtractedFiles = Directory.Exists(targetDir) &&
                Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Any();

            if (!hasExtractedFiles)
            {
                throw new InvalidOperationException(
                    "Не удалось извлечь файлы из RAR-архива.\nВозможно, архив повреждён, защищён паролем или пуст.");
            }

            if (progressCallback != null)
                progressCallback("Распаковка завершена. Поиск DICOM-исследований...");

            // Распаковываем любые вложенные архивы (RAR / ZIP)
            ExtractNestedArchives(targetDir, 0, progressCallback);

            return targetDir;
        }

        private static void ExtractNestedArchives(string rootDir, int depth, Action<string> progressCallback)
        {
            if (depth >= 3 || !Directory.Exists(rootDir))
                return;

            // 1. Вложенные ZIP
            var nestedZips = Directory.GetFiles(rootDir, "*.zip", SearchOption.AllDirectories).ToList();
            foreach (var zip in nestedZips)
            {
                try
                {
                    var parent = Path.GetDirectoryName(zip) ?? rootDir;
                    var name = Path.GetFileNameWithoutExtension(zip);
                    var subTarget = Path.Combine(parent, name);
                    if (!Directory.Exists(subTarget))
                        Directory.CreateDirectory(subTarget);

                    using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
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
                    try { File.Delete(zip); } catch { }
                }
                catch { }
            }

            // 2. Вложенные RAR
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
                    var extractorExe = FindExtractorExecutable(out extractorType);
                    if (!string.IsNullOrEmpty(extractorExe))
                    {
                        var cleanSub = subTarget.TrimEnd('\\');
                        string args = extractorType == "UnRAR"
                            ? string.Format("x -y -o+ -kb -inul \"{0}\" \"{1}\"", rar, cleanSub)
                            : string.Format("x -ibck -inul -o+ -kb -y \"{0}\" \"{1}\"", rar, cleanSub);

                        using (var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = extractorExe,
                            Arguments = args,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
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
                ExtractNestedArchives(rootDir, depth + 1, progressCallback);
            }
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

