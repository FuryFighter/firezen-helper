// FireZEN Helper. Uses only Windows/.NET Framework libraries; no downloads.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FireZenHelper
{
    public sealed class BrowserInfo
    {
        public string Path { get; set; }
        public string Version { get; set; }
        public string Engine { get; set; }
        public string Architecture { get; set; }
    }

    public sealed class BrowserPair
    {
        public BrowserInfo Zen { get; set; }
        public BrowserInfo Firefox { get; set; }
        public int FileCount { get; set; }
        public long SourceBytes { get; set; }
        public string PreservedEngineHash { get; set; }
    }

    public sealed class ProgressUpdate
    {
        public string Stage { get; set; }
        public string Message { get; set; }
        public int Percent { get; set; }
    }

    public sealed class FileRecord
    {
        public string RelativePath { get; set; }
        public bool Existed { get; set; }
        public string OriginalHash { get; set; }
        public string AppliedHash { get; set; }
    }

    public sealed class BackupManifest
    {
        public int Format { get; set; }
        public string CreatedUtc { get; set; }
        public string Status { get; set; }
        public string FirefoxPath { get; set; }
        public string FirefoxExeHash { get; set; }
        public string Engine { get; set; }
        public bool PausedUpdates { get; set; }
        public bool BorderlessDefault { get; set; }
        public bool KeepFirefoxEngine { get; set; }
        public string PreservedEngineHash { get; set; }
        public int TouchedCount { get; set; }
        public List<FileRecord> Files { get; set; }
    }

    public static class Engine
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private static readonly string[] Roots = { "browser", "xul.dll", "omni.ja" };
        public const string BorderPreferencePath = @"browser\defaults\preferences\firezen-helper-defaults.js";

        public static string DefaultBackupRoot
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FireZenHelper", "Backups"); }
        }

        public static string NormalizeFolder(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Choose an installation folder.");
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (File.Exists(expanded) && expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                expanded = System.IO.Path.GetDirectoryName(expanded);
            string result = System.IO.Path.GetFullPath(expanded);
            if (result.Length > System.IO.Path.GetPathRoot(result).Length) result = result.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            if (!Directory.Exists(result)) throw new DirectoryNotFoundException("Folder not found: " + result);
            CheckPath(result);
            return result;
        }

        // Refuse junctions/symlinks anywhere along a path before reading or writing it.
        internal static void CheckPath(string path)
        {
            string current = System.IO.Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a real folder, not a symbolic link or junction: " + current);
                current = System.IO.Path.GetDirectoryName(current);
            }
        }

        internal static string Inside(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                throw new IOException("Invalid relative file path.");
            string fullRoot = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relative));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("File escapes its installation folder.");
            CheckPath(full);
            return full;
        }

        private static bool Overlaps(string a, string b)
        {
            a = System.IO.Path.GetFullPath(a).TrimEnd('\\') + "\\";
            b = System.IO.Path.GetFullPath(b).TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string IniValue(string path, string section, string key)
        {
            CheckPath(path);
            if (!File.Exists(path)) return null;
            string active = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { active = line.Trim('[', ']'); continue; }
                int equals = line.IndexOf('=');
                if (equals > 0 && active.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                    line.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(equals + 1).Trim();
            }
            return null;
        }

        private static string NormalVersion(string value)
        {
            if (String.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"^\d+(\.\d+){1,3}([ab]\d+)?$"))
                throw new InvalidOperationException("Cannot determine the exact Firefox engine version. Check platform.ini in the selected folder.");
            Match m = Regex.Match(value, @"^(?<version>[\d.]+)(?<suffix>[ab]\d+)?$");
            string[] parts = m.Groups["version"].Value.Split('.');
            int count = parts.Length;
            while (count > 2 && parts[count - 1] == "0") count--;
            return String.Join(".", parts.Take(count).ToArray()) + m.Groups["suffix"].Value;
        }

        private static string Architecture(string executable)
        {
            using (var file = File.OpenRead(executable))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new IOException("Not a Windows browser executable: " + executable);
                file.Position = 0x3c;
                int offset = reader.ReadInt32();
                if (offset < 64 || offset > file.Length - 6) throw new IOException("Invalid executable header: " + executable);
                file.Position = offset;
                if (reader.ReadUInt32() != 0x4550) throw new IOException("Invalid executable header: " + executable);
                switch (reader.ReadUInt16())
                {
                    case 0x8664: return "x64";
                    case 0x014c: return "x86";
                    case 0xaa64: return "ARM64";
                    default: throw new IOException("Unsupported browser architecture: " + executable);
                }
            }
        }

        public static BrowserInfo ReadBrowser(string path, bool zen)
        {
            path = NormalizeFolder(path);
            string exe = Inside(path, zen ? "zen.exe" : "firefox.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("Select the folder containing " + (zen ? "zen.exe" : "firefox.exe") + ".");
            string app = Inside(path, "application.ini");
            string name = IniValue(app, "App", "Name");
            if (String.IsNullOrEmpty(name) || (zen ? name.IndexOf("Zen", StringComparison.OrdinalIgnoreCase) < 0 : !name.Equals("Firefox", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This does not appear to be a " + (zen ? "Zen" : "standard Firefox") + " installation.");
            string milestone = IniValue(Inside(path, "platform.ini"), "Build", "Milestone");
            return new BrowserInfo { Path = path, Version = IniValue(app, "App", "Version") ?? "unknown", Engine = NormalVersion(milestone), Architecture = Architecture(exe) };
        }

        private static List<string> SourceFiles(string root, bool keepFirefoxEngine = false)
        {
            var result = new List<string>();
            foreach (string item in Roots)
            {
                if (keepFirefoxEngine && item == "xul.dll") continue;
                string path = Inside(root, item);
                if (item == "browser")
                {
                    if (!Directory.Exists(path)) throw new IOException("Zen's browser folder is missing.");
                    Walk(root, path, result);
                }
                else
                {
                    if (!File.Exists(path)) throw new IOException("Zen is missing " + item + ".");
                    result.Add(item);
                }
            }
            if (!File.Exists(Inside(root, @"browser\omni.ja"))) throw new IOException("Zen's browser\\omni.ja is missing. Choose a complete installation.");
            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void Walk(string root, string folder, List<string> result)
        {
            CheckPath(folder);
            foreach (string file in Directory.GetFiles(folder))
            {
                CheckPath(file);
                result.Add(file.Substring(root.Length).TrimStart('\\'));
            }
            foreach (string child in Directory.GetDirectories(folder)) Walk(root, child, result);
        }

        private static void RequireMozillaEngine(string firefoxPath, string expectedEngine)
        {
            string executable = Inside(firefoxPath, "firefox.exe"), engine = Inside(firefoxPath, "xul.dll");
            string guidance = "Keep Firefox engine requires original, valid Mozilla-signed Firefox files. Restore a backup made before FireZEN, or reinstall official Firefox, then check the setup again. A recent backup of an already-modified installation may still contain Zen's engine.";
            if (!File.Exists(engine)) throw new InvalidOperationException(guidance);
            if (NormalVersion(FileVersionInfo.GetVersionInfo(engine).FileVersion) != expectedEngine ||
                NormalVersion(FileVersionInfo.GetVersionInfo(executable).FileVersion) != expectedEngine || Architecture(engine) != Architecture(executable))
                throw new InvalidOperationException("Firefox's engine does not match its installation version or architecture. " + guidance);
            string executableHash = HashFile(executable), engineHash = HashFile(engine);
            // Use Windows PowerShell's Authenticode verifier, including trust and timestamp
            // validation. Reading a certificate alone would not validate the file signature.
            string command = "$ErrorActionPreference='Stop';try { foreach($file in @('" + executable.Replace("'", "''") + "','" + engine.Replace("'", "''") +
                "')) { $signature=Get-AuthenticodeSignature -LiteralPath $file; if($signature.Status -ne 'Valid' -or " +
                "$signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne 'Mozilla Corporation') { exit 3 } }; exit 0 } catch { exit 2 }";
            var start = new ProcessStartInfo {
                FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process verifier = Process.Start(start))
            {
                if (verifier == null) throw new IOException("Could not start the Windows signature verifier.");
                if (!verifier.WaitForExit(30000))
                {
                    try { verifier.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("Windows signature verification timed out. No browser files were changed; try Check setup again.");
                }
                if (verifier.ExitCode != 0) throw new InvalidOperationException(guidance);
            }
            if (HashFile(executable) != executableHash || HashFile(engine) != engineHash)
                throw new IOException("Firefox changed during signature verification. Check the setup again.");
        }

        public static BrowserPair Inspect(string zenPath, string firefoxPath)
        {
            return Inspect(zenPath, firefoxPath, false);
        }

        public static BrowserPair Inspect(string zenPath, string firefoxPath, bool keepFirefoxEngine)
        {
            BrowserInfo zen = ReadBrowser(zenPath, true), firefox = ReadBrowser(firefoxPath, false);
            if (Overlaps(zen.Path, firefox.Path)) throw new InvalidOperationException("Zen and Firefox must have separate installation folders.");
            if (zen.Engine != firefox.Engine)
                throw new InvalidOperationException("Version mismatch: Zen uses Firefox " + zen.Engine + ", but Firefox is " + firefox.Engine + ". Install matching versions before applying FireZEN.");
            if (zen.Architecture != firefox.Architecture)
                throw new InvalidOperationException("Architecture mismatch: Zen is " + zen.Architecture + " and Firefox is " + firefox.Architecture + ". Install matching editions.");
            string preservedHash = null;
            if (keepFirefoxEngine)
            {
                string engine = Inside(firefox.Path, "xul.dll");
                if (!File.Exists(engine)) throw new InvalidOperationException("Firefox's original engine is missing. Reinstall official Firefox before using Keep Firefox engine.");
                preservedHash = HashFile(engine);
                RequireMozillaEngine(firefox.Path, firefox.Engine);
                if (HashFile(engine) != preservedHash) throw new IOException("Firefox's engine changed while checking setup. Try again.");
            }
            List<string> files = SourceFiles(zen.Path, keepFirefoxEngine);
            foreach (string file in files)
            {
                string destination = Inside(firefox.Path, file);
                if (Directory.Exists(destination)) throw new IOException("A folder blocks this file: " + destination);
            }
            return new BrowserPair { Zen = zen, Firefox = firefox, FileCount = files.Count, SourceBytes = files.Sum(f => new FileInfo(Inside(zen.Path, f)).Length), PreservedEngineHash = preservedHash };
        }

        public static List<string> Detect(bool zen)
        {
            var candidates = new List<string>();
            string exe = zen ? "zen.exe" : "firefox.exe";
            string folder = zen ? "Zen Browser" : "Mozilla Firefox";
            foreach (string parent in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
            {
                if (String.IsNullOrEmpty(parent)) continue;
                candidates.Add(System.IO.Path.Combine(parent, folder));
                if (zen) candidates.Add(System.IO.Path.Combine(parent, "Zen"));
            }
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey basis = RegistryKey.OpenBaseKey(hive, view))
                    {
                        using (RegistryKey app = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        {
                            if (app != null) AddExecutable(candidates, app.GetValue(null) as string);
                        }
                        using (RegistryKey uninstall = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall != null)
                            foreach (string key in uninstall.GetSubKeyNames())
                            using (RegistryKey entry = uninstall.OpenSubKey(key))
                            {
                                if (entry == null) continue;
                                string name = entry.GetValue("DisplayName") as string ?? "";
                                if (name.IndexOf(zen ? "Zen" : "Mozilla Firefox", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string location = entry.GetValue("InstallLocation") as string;
                                if (!String.IsNullOrWhiteSpace(location)) candidates.Add(location.Trim('"'));
                                AddExecutable(candidates, entry.GetValue("DisplayIcon") as string);
                            }
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return candidates.Where(p => !String.IsNullOrEmpty(p) && File.Exists(System.IO.Path.Combine(p, exe))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddExecutable(List<string> list, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            Match match = Regex.Match(value, "^(?:\"(?<path>[^\"]+\\.exe)\"|(?<path>.*?\\.exe))", RegexOptions.IgnoreCase);
            if (match.Success) list.Add(System.IO.Path.GetDirectoryName(match.Groups["path"].Value));
        }

        internal static void CheckBrowsersClosed() { EnsureClosed(); }

        private static void EnsureClosed()
        {
            foreach (string name in new[] { "firefox", "zen" })
            {
                Process[] processes = Process.GetProcessesByName(name);
                try { if (processes.Length != 0) throw new InvalidOperationException("Close Firefox and Zen, including background windows, then try again. This app will not force them to close."); }
                finally { foreach (Process process in processes) process.Dispose(); }
            }
        }

        public static string HashFile(string file)
        {
            CheckPath(file);
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        internal static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static byte[] PolicyBytes(string firefox)
        {
            string path = Inside(firefox, @"distribution\policies.json");
            Dictionary<string, object> document;
            try
            {
                document = File.Exists(path) ? Json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object> : new Dictionary<string, object>();
                if (document == null) throw new FormatException();
                object policies;
                if (!document.TryGetValue("policies", out policies)) document["policies"] = policies = new Dictionary<string, object>();
                var map = policies as Dictionary<string, object>;
                if (map == null) throw new FormatException();
                map["DisableAppUpdate"] = true;
            }
            catch (Exception e) { throw new InvalidOperationException("The existing policies.json could not be read. Fix it or turn off Pause Firefox updates. " + e.Message); }
            return new UTF8Encoding(false).GetBytes(Json.Serialize(document));
        }

        internal static void AtomicBytes(string destination, byte[] bytes)
        {
            CheckPath(destination);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
            string temp = destination + ".firezen-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static void AtomicCopy(string source, string destination)
        {
            CheckPath(source);
            CheckPath(destination);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
            string temp = destination + ".firezen-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temp, false);
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void SaveManifest(string backup, BackupManifest manifest)
        {
            AtomicBytes(Inside(backup, "manifest.json"), new UTF8Encoding(false).GetBytes(Json.Serialize(manifest)));
        }

        private static void Report(Action<ProgressUpdate> callback, string stage, string message, int percent)
        {
            if (callback != null) callback(new ProgressUpdate { Stage = stage, Message = message, Percent = percent });
        }

        internal static Mutex OperationLock()
        {
            var mutex = new Mutex(false, @"Global\FireZenHelper.FileOperation.v1");
            try
            {
                bool taken;
                try { taken = mutex.WaitOne(0); } catch (AbandonedMutexException) { taken = true; }
                if (!taken) throw new InvalidOperationException("Another FireZEN operation is already running. Wait for it to finish.");
                return mutex;
            }
            catch { mutex.Dispose(); throw; }
        }

        public static string Apply(string zenPath, string firefoxPath, string backupRoot, bool pauseUpdates, Action<ProgressUpdate> progress)
        {
            return Apply(zenPath, firefoxPath, backupRoot, pauseUpdates, false, progress);
        }

        public static string Apply(string zenPath, string firefoxPath, string backupRoot, bool pauseUpdates, bool borderless, Action<ProgressUpdate> progress)
        {
            return Apply(zenPath, firefoxPath, backupRoot, pauseUpdates, borderless, false, progress);
        }

        public static string Apply(string zenPath, string firefoxPath, string backupRoot, bool pauseUpdates, bool borderless, bool keepFirefoxEngine, Action<ProgressUpdate> progress)
        {
            using (Mutex operation = OperationLock())
            try { return ApplyLocked(zenPath, firefoxPath, backupRoot, pauseUpdates, borderless, keepFirefoxEngine, progress); }
            finally { operation.ReleaseMutex(); }
        }

        private static string ApplyLocked(string zenPath, string firefoxPath, string backupRoot, bool pauseUpdates, bool borderless, bool keepFirefoxEngine, Action<ProgressUpdate> progress)
        {
            BrowserPair pair = Inspect(zenPath, firefoxPath, keepFirefoxEngine);
            EnsureClosed();
            backupRoot = System.IO.Path.GetFullPath(backupRoot);
            CheckPath(backupRoot);
            if (Overlaps(backupRoot, pair.Zen.Path) || Overlaps(backupRoot, pair.Firefox.Path))
                throw new IOException("Keep the backup folder outside both browser installations.");
            List<string> sourceFiles = SourceFiles(pair.Zen.Path, keepFirefoxEngine);
            var files = new List<string>(sourceFiles);
            var generated = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (pauseUpdates) generated[@"distribution\policies.json"] = PolicyBytes(pair.Firefox.Path);
            if (borderless) generated[BorderPreferencePath] = new UTF8Encoding(false).GetBytes(
                "// FireZEN Helper: no outer content frame by default.\n" +
                "// Explicit about:config/user.js settings still take precedence.\n" +
                "pref(\"zen.theme.content-element-separation\", 0);\n");
            foreach (string relative in generated.Keys)
                if (!files.Contains(relative, StringComparer.OrdinalIgnoreCase)) files.Add(relative);
            long sourceBytes = pair.SourceBytes + generated.Values.Sum(bytes => (long)bytes.Length);
            long originalBytes = files.Sum(f => File.Exists(Inside(pair.Firefox.Path, f)) ? new FileInfo(Inside(pair.Firefox.Path, f)).Length : 0L);
            // Staged source + originals live in the backup; target needs room for atomic replacement.
            var backupDrive = new DriveInfo(System.IO.Path.GetPathRoot(backupRoot));
            var targetDrive = new DriveInfo(System.IO.Path.GetPathRoot(pair.Firefox.Path));
            long reserve = 64L * 1024 * 1024;
            bool sameDrive = backupDrive.Name.Equals(targetDrive.Name, StringComparison.OrdinalIgnoreCase);
            long requiredBackup = originalBytes + sourceBytes + reserve + (sameDrive ? sourceBytes : 0);
            if (backupDrive.AvailableFreeSpace < requiredBackup || (!sameDrive && targetDrive.AvailableFreeSpace < sourceBytes + reserve))
                throw new IOException("Not enough free space for a verified backup and file replacement. Free about " + ((requiredBackup + 1048575) / 1048576) + " MB on the backup drive.");
            Directory.CreateDirectory(backupRoot);
            string backup = Inside(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(backup);
            var manifest = new BackupManifest
            {
                Format = 1, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "Preparing",
                FirefoxPath = pair.Firefox.Path, FirefoxExeHash = HashFile(Inside(pair.Firefox.Path, "firefox.exe")),
                Engine = pair.Firefox.Engine, PausedUpdates = pauseUpdates, BorderlessDefault = borderless,
                KeepFirefoxEngine = keepFirefoxEngine, PreservedEngineHash = pair.PreservedEngineHash, Files = new List<FileRecord>()
            };
            string zenExeHash = HashFile(Inside(pair.Zen.Path, "zen.exe"));
            string zenPlatformHash = HashFile(Inside(pair.Zen.Path, "platform.ini"));
            SaveManifest(backup, manifest);
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string relative = files[i], target = Inside(pair.Firefox.Path, relative);
                    string payload = Inside(backup, @"payload\" + relative), original = Inside(backup, @"original\" + relative);
                    Report(progress, "BackingUp", "Preparing and verifying " + relative, i * 45 / files.Count);
                    byte[] custom;
                    if (generated.TryGetValue(relative, out custom)) AtomicBytes(payload, custom);
                    else AtomicCopy(Inside(pair.Zen.Path, relative), payload);
                    var record = new FileRecord { RelativePath = relative, Existed = File.Exists(target), AppliedHash = HashFile(payload) };
                    string expected = custom != null ? HashBytes(custom) : HashFile(Inside(pair.Zen.Path, relative));
                    if (record.AppliedHash != expected) throw new IOException("Zen changed while staging files. Try again: " + relative);
                    if (record.Existed)
                    {
                        record.OriginalHash = HashFile(target);
                        AtomicCopy(target, original);
                        if (HashFile(original) != record.OriginalHash) throw new IOException("Backup verification failed: " + relative);
                    }
                    manifest.Files.Add(record);
                }
                manifest.Status = "Prepared";
                SaveManifest(backup, manifest);
                EnsureClosed();
                if (HashFile(Inside(pair.Zen.Path, "zen.exe")) != zenExeHash || HashFile(Inside(pair.Zen.Path, "platform.ini")) != zenPlatformHash ||
                    !SourceFiles(pair.Zen.Path, keepFirefoxEngine).SequenceEqual(sourceFiles, StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Zen changed during backup. Check the versions again before applying.");
                foreach (FileRecord record in manifest.Files.Where(f => !generated.ContainsKey(f.RelativePath)))
                    if (HashFile(Inside(pair.Zen.Path, record.RelativePath)) != record.AppliedHash)
                        throw new IOException("Zen changed during backup. Try again with both browsers closed.");
                if (HashFile(Inside(pair.Firefox.Path, "firefox.exe")) != manifest.FirefoxExeHash)
                    throw new IOException("Firefox updated while the backup was being made. Check the versions again.");
                if (keepFirefoxEngine && HashFile(Inside(pair.Firefox.Path, "xul.dll")) != manifest.PreservedEngineHash)
                    throw new IOException("Firefox's preserved engine changed during backup. No interface files were applied.");
                foreach (FileRecord record in manifest.Files)
                {
                    string target = Inside(pair.Firefox.Path, record.RelativePath);
                    if (File.Exists(target) != record.Existed || (record.Existed && HashFile(target) != record.OriginalHash))
                        throw new IOException("Firefox changed during backup. Try again with both browsers closed.");
                }
                manifest.Status = "Applying";
                SaveManifest(backup, manifest);
                for (int i = 0; i < manifest.Files.Count; i++)
                {
                    FileRecord record = manifest.Files[i];
                    // Journal BEFORE changing each file; interrupted operations remain recoverable.
                    manifest.TouchedCount = i + 1;
                    SaveManifest(backup, manifest);
                    AtomicCopy(Inside(backup, @"payload\" + record.RelativePath), Inside(pair.Firefox.Path, record.RelativePath));
                    if (HashFile(Inside(pair.Firefox.Path, record.RelativePath)) != record.AppliedHash)
                        throw new IOException("Replacement verification failed: " + record.RelativePath);
                    Report(progress, "AppliedFile", "Applied " + record.RelativePath, 45 + (i + 1) * 55 / manifest.Files.Count);
                }
                manifest.Status = "Applied";
                SaveManifest(backup, manifest);
                return backup;
            }
            catch (Exception failure)
            {
                try
                {
                    if (manifest.TouchedCount > 0) RestoreFiles(backup, manifest, null);
                    manifest.Status = "RolledBack";
                    SaveManifest(backup, manifest);
                }
                catch (Exception rollback)
                {
                    manifest.Status = "RecoveryRequired";
                    try { SaveManifest(backup, manifest); } catch { }
                    throw new IOException("The operation stopped and automatic restore could not finish. Keep Firefox closed. Use Restore latest backup. Backup: " + backup + "\r\n" + rollback.Message, failure);
                }
                throw new IOException("No transformation was completed. Any changed files were restored. " + failure.Message + "\r\nBackup: " + backup, failure);
            }
        }

        private static BackupManifest LoadManifest(string backup, string firefox)
        {
            backup = NormalizeFolder(backup);
            BackupManifest manifest = Json.Deserialize<BackupManifest>(File.ReadAllText(Inside(backup, "manifest.json")));
            if (manifest == null || manifest.Format != 1 || manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > 100000 ||
                manifest.TouchedCount < 0 || manifest.TouchedCount > manifest.Files.Count)
                throw new IOException("This is not a valid FireZEN Helper backup.");
            firefox = NormalizeFolder(firefox);
            if (!String.Equals(NormalizeFolder(manifest.FirefoxPath), firefox, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This backup belongs to a different Firefox installation.");
            manifest.FirefoxPath = firefox;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileRecord record in manifest.Files)
            {
                string rel = record.RelativePath;
                if (String.IsNullOrEmpty(rel) || rel.Contains("..") || rel.Contains("/") || !unique.Add(rel) ||
                    !(rel.StartsWith(@"browser\", StringComparison.OrdinalIgnoreCase) || rel == "xul.dll" || rel == "omni.ja" || rel == @"distribution\policies.json"))
                    throw new IOException("Backup contains an unexpected file path.");
                Inside(firefox, rel);
                if (record.Existed && (String.IsNullOrEmpty(record.OriginalHash) || HashFile(Inside(backup, @"original\" + rel)) != record.OriginalHash))
                    throw new IOException("Backup is incomplete or damaged: " + rel);
            }
            if (HashFile(Inside(firefox, "firefox.exe")) != manifest.FirefoxExeHash)
                throw new IOException("Firefox has been updated or replaced since this backup. Restoring old engine files would mix versions. Use a matching Firefox installation instead.");
            if (manifest.KeepFirefoxEngine && (String.IsNullOrEmpty(manifest.PreservedEngineHash) ||
                manifest.Files.Any(f => f.RelativePath.Equals("xul.dll", StringComparison.OrdinalIgnoreCase)) ||
                HashFile(Inside(firefox, "xul.dll")) != manifest.PreservedEngineHash))
                throw new IOException("Firefox's preserved engine changed since this backup, or its engine metadata is invalid. Restore later operations first or reinstall matching official Firefox files.");
            return manifest;
        }

        private static void RestoreFiles(string backup, BackupManifest manifest, Action<ProgressUpdate> progress)
        {
            // Check every original before changing anything; never restore a partial/corrupt backup.
            foreach (FileRecord record in manifest.Files.Take(manifest.TouchedCount))
                if (record.Existed && HashFile(Inside(backup, @"original\" + record.RelativePath)) != record.OriginalHash)
                    throw new IOException("Backup verification failed: " + record.RelativePath);
            for (int i = manifest.TouchedCount - 1; i >= 0; i--)
            {
                FileRecord record = manifest.Files[i];
                string target = Inside(manifest.FirefoxPath, record.RelativePath);
                if (record.Existed)
                {
                    AtomicCopy(Inside(backup, @"original\" + record.RelativePath), target);
                    if (HashFile(target) != record.OriginalHash) throw new IOException("Restore verification failed: " + record.RelativePath);
                }
                else if (File.Exists(target)) File.Delete(target);
                Report(progress, "Restoring", "Restored " + record.RelativePath, (manifest.TouchedCount - i) * 100 / Math.Max(1, manifest.TouchedCount));
            }
        }

        public static void Restore(string backup, string firefox, Action<ProgressUpdate> progress)
        {
            using (Mutex operation = OperationLock())
            try
            {
                EnsureClosed();
                BackupManifest manifest = LoadManifest(backup, firefox);
                if (!(manifest.Status == "Applied" || manifest.Status == "Applying" || manifest.Status == "RecoveryRequired"))
                    throw new IOException("This backup has already been restored or was never applied.");
                try
                {
                    RestoreFiles(backup, manifest, progress);
                    manifest.Status = "Restored";
                    SaveManifest(backup, manifest);
                }
                catch (Exception failure)
                {
                    manifest.Status = "RecoveryRequired";
                    try { SaveManifest(backup, manifest); } catch { }
                    throw new IOException("Restore could not finish. Keep Firefox closed and retry Restore latest backup. Keep this backup: " + backup + "\r\n" + failure.Message, failure);
                }
            }
            finally { operation.ReleaseMutex(); }
        }

        public static string LatestBackup(string backupRoot, string firefox)
        {
            if (!Directory.Exists(backupRoot) || String.IsNullOrWhiteSpace(firefox)) return null;
            CheckPath(backupRoot);
            string selected = System.IO.Path.GetFullPath(firefox).TrimEnd('\\');
            var found = new List<Tuple<string, DateTime>>();
            foreach (string dir in Directory.GetDirectories(backupRoot))
            {
                try
                {
                    string path = Inside(dir, "manifest.json");
                    if (!File.Exists(path)) continue;
                    BackupManifest data = Json.Deserialize<BackupManifest>(File.ReadAllText(path));
                    DateTime created;
                    if (data != null && !String.IsNullOrWhiteSpace(data.FirefoxPath) && String.Equals(data.FirefoxPath.TrimEnd('\\'), selected, StringComparison.OrdinalIgnoreCase) &&
                        (data.Status == "Applied" || data.Status == "Applying" || data.Status == "RecoveryRequired") &&
                        DateTime.TryParse(data.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created))
                        found.Add(Tuple.Create(dir, created.ToUniversalTime()));
                }
                catch (IOException) { }
                catch (ArgumentException) { }
            }
            return found.OrderByDescending(x => x.Item2).Select(x => x.Item1).FirstOrDefault();
        }
    }
}
