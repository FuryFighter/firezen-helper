using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FireZenHelper
{
    public sealed class SineFileRecord
    {
        public string Scope { get; set; }
        public string RelativePath { get; set; }
        public bool Existed { get; set; }
        public string OriginalHash { get; set; }
        public string AppliedHash { get; set; }
    }

    public sealed class SineBackup
    {
        public string Kind { get; set; }
        public int Format { get; set; }
        public string CreatedUtc { get; set; }
        public string FirefoxPath { get; set; }
        public string ProfilePath { get; set; }
        public string Status { get; set; }
        public int TouchedCount { get; set; }
        public List<SineFileRecord> Files { get; set; }
    }

    public sealed class ProfileDiscovery
    {
        public List<string> Profiles { get; set; }
        public string RecommendedProfile { get; set; }
        public string RecommendationReason { get; set; }
    }

    public static class SineInstaller
    {
        public const string Version = "2.3.3";
        public const string ModId = "ag7cgog8f-vatc068tj-pfwnzowlv";
        private const string ModCommit = "123f4bfbaff29e4a5fc61a8b132735038b708f91";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private static readonly string[] Names = { "program.zip", "profile.zip", "engine.zip", "theme.json", "preferences.json", "userChrome.css", "fire.png" };
        private static readonly string[] Hashes = {
            "55cc14ebd141a32d746cfe93fc8ad9856eb0d553cc3727d3e408617e73d4ac4d",
            "285b3d589cc979f11f01c9c77410b717694ccc4f32cc1cb08bd6d8909fb98e00",
            "c98be8e0234e8c4d5b41dc277bd201a365a2dcc418dcba82d4749d27a64a3d65",
            "1e4c41aa50d284d1689a062c4ad63c286da6916a84c3210d98f3d91721d8f002",
            "0e47b8bdcd3082980f969cc5ecac68feacabc3daa28c2134d22f49d8d6919160",
            "3b81b1347480298ed95c391000d473fbde929df410d27e8378fa6aba94833dec",
            "6ecbef59c805195c6e7705be65312ab05e155e05860cb7297627ff77c2fac7ba"
        };

        private static void Report(Action<ProgressUpdate> progress, string stage, string message, int percent)
        {
            if (progress != null) progress(new ProgressUpdate { Stage = stage, Message = message, Percent = percent });
        }

        public static List<string> DetectProfiles(string firefox)
        {
            return DiscoverProfiles(firefox).Profiles;
        }

        private sealed class ProfileSection
        {
            public string Name;
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<ProfileSection> ProfileIni(string file)
        {
            var result = new List<ProfileSection>();
            if (!File.Exists(file)) return result;
            Engine.CheckPath(file);
            ProfileSection section = null;
            foreach (string original in File.ReadAllLines(file))
            {
                string line = original.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = new ProfileSection { Name = line.Substring(1, line.Length - 2).Trim() };
                    result.Add(section);
                }
                else if (section != null && line.IndexOf('=') > 0)
                {
                    int at = line.IndexOf('=');
                    section.Values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                }
            }
            return result;
        }

        private static string ProfileValue(ProfileSection section, string key)
        {
            string value;
            return section != null && section.Values.TryGetValue(key, out value) ? value : null;
        }

        private static string ProfileFolder(string root, string value, bool absoluteOnly = false)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            try
            {
                value = value.Replace('/', '\\');
                if (absoluteOnly && !Path.IsPathRooted(value)) return null;
                string full = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
                return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd('\\') : full;
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static string RegisteredInstallHash(string firefox)
        {
            // Mozilla's Windows installer records the same installation identity
            // used by the per-install profile mapping. No registry values are changed.
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey basis = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = basis.OpenSubKey(@"SOFTWARE\Mozilla\Firefox\TaskBarIDs"))
                    {
                        string value = key == null ? null : key.GetValue(firefox) as string;
                        if (!String.IsNullOrEmpty(value) && Regex.IsMatch(value, "^[a-fA-F0-9]{1,16}$")) return value.ToUpperInvariant();
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return null;
        }

        public static ProfileDiscovery DiscoverProfiles(string firefox)
        {
            firefox = Engine.NormalizeFolder(firefox);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox");
            return DiscoverProfiles(firefox, root, RegisteredInstallHash(firefox));
        }

        // Explicit inputs also permit deterministic tests without reading a user's
        // registry or profile registration. Discovery never changes profiles/defaults.
        public static ProfileDiscovery DiscoverProfiles(string firefox, string profileRoot, string installHash)
        {
            firefox = Engine.NormalizeFolder(firefox);
            string root = Path.GetFullPath(profileRoot);
            List<ProfileSection> sections = ProfileIni(Path.Combine(root, "profiles.ini"));
            var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProfileSection section in sections.Where(s => Regex.IsMatch(s.Name, "^Profile[0-9]+$", RegexOptions.IgnoreCase)))
            {
                string path = ProfileFolder(root, ProfileValue(section, "Path"), ProfileValue(section, "IsRelative") == "0");
                if (path == null || !Directory.Exists(path)) continue;
                try
                {
                    Engine.CheckPath(path);
                    if (File.Exists(Engine.Inside(path, "prefs.js")) || File.Exists(Engine.Inside(path, "compatibility.ini"))) profiles.Add(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var installSections = sections.Where(s => s.Name.StartsWith("Install", StringComparison.OrdinalIgnoreCase)).ToList();
            // Firefox imports installs.ini only for the older profiles.ini format.
            ProfileSection general = sections.LastOrDefault(s => s.Name.Equals("General", StringComparison.OrdinalIgnoreCase));
            if (ProfileValue(general, "Version") == null)
                foreach (ProfileSection legacy in ProfileIni(Path.Combine(root, "installs.ini")))
                {
                    string name = "Install" + legacy.Name;
                    if (!installSections.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        installSections.Add(new ProfileSection { Name = name, Values = legacy.Values });
                }
            var result = new ProfileDiscovery {
                Profiles = profiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                RecommendationReason = "No matching default was identified for this Firefox installation. Choose its active profile from about:support."
            };
            ProfileSection selected = String.IsNullOrWhiteSpace(installHash) ? null : installSections.LastOrDefault(s => s.Name.Equals("Install" + installHash.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                string mapped = ProfileFolder(root, ProfileValue(selected, "Default"));
                if (mapped != null && profiles.Contains(mapped))
                {
                    result.RecommendedProfile = mapped;
                    result.RecommendationReason = "Default profile for the selected Firefox installation.";
                }
                else result.RecommendationReason = "This Firefox installation's registered default is missing or uninitialized. Start Firefox once, or select its active profile manually.";
            }
            else
            {
                var owned = new List<string>();
                foreach (string profile in profiles)
                {
                    try
                    {
                        ProfileSection compatibility = ProfileIni(Engine.Inside(profile, "compatibility.ini")).LastOrDefault(s => s.Name.Equals("Compatibility", StringComparison.OrdinalIgnoreCase));
                        string platform = ProfileFolder(root, ProfileValue(compatibility, "LastPlatformDir"), true);
                        string app = ProfileFolder(root, ProfileValue(compatibility, "LastAppDir"), true);
                        if ((platform != null && platform.Equals(firefox, StringComparison.OrdinalIgnoreCase)) ||
                            (platform == null && app != null && app.Equals(Path.Combine(firefox, "browser"), StringComparison.OrdinalIgnoreCase))) owned.Add(profile);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                var defaults = new HashSet<string>(installSections.Select(s => ProfileFolder(root, ProfileValue(s, "Default"))).Where(p => p != null), StringComparer.OrdinalIgnoreCase);
                var candidates = owned.Count == 1 ? owned : owned.Where(defaults.Contains).ToList();
                if (candidates.Count == 1)
                {
                    result.RecommendedProfile = candidates[0];
                    result.RecommendationReason = "Last used by the selected Firefox installation. Confirm this is the active profile in about:support.";
                }
                else if (owned.Count > 1) result.RecommendationReason = "Several profiles were used by this Firefox installation. Select the active one shown in about:support.";
            }
            result.Profiles = result.Profiles.OrderByDescending(p => String.Equals(p, result.RecommendedProfile, StringComparison.OrdinalIgnoreCase)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        public static string ValidateProfile(string profile)
        {
            profile = Engine.NormalizeFolder(profile);
            if (!File.Exists(Engine.Inside(profile, "prefs.js")) && !File.Exists(Engine.Inside(profile, "compatibility.ini")))
                throw new IOException("Choose an initialized Firefox profile folder. Find it in Firefox's about:support > Profile Folder.");
            return profile;
        }

        private static string AssetUrl(int i)
        {
            if (i < 2) return "https://github.com/sineorg/bootloader/releases/download/v0.1.4/" + Names[i];
            if (i == 2) return "https://github.com/CosmoCreeper/Sine/releases/download/v2.3.3/engine.zip";
            return "https://raw.githubusercontent.com/CosmoCreeper/FireZen/" + ModCommit + "/" + Names[i];
        }

        public static void PrepareDownloads(string directory, bool includeMod, Action<ProgressUpdate> progress)
        {
            directory = Path.GetFullPath(directory);
            Engine.CheckPath(directory);
            Directory.CreateDirectory(directory);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            int count = includeMod ? Names.Length : 3;
            for (int i = 0; i < count; i++)
            {
                string destination = Engine.Inside(directory, Names[i]);
                if (File.Exists(destination) && Engine.HashFile(destination).Equals(Hashes[i], StringComparison.OrdinalIgnoreCase)) continue;
                Report(progress, "Downloading", "Downloading official " + Names[i], i * 15 / count);
                var request = (HttpWebRequest)WebRequest.Create(AssetUrl(i));
                request.UserAgent = "FireZEN-Helper/3";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var bytes = new MemoryStream())
                {
                    if (response.ContentLength > 5 * 1024 * 1024) throw new IOException("Unexpected download size for " + Names[i]);
                    byte[] buffer = new byte[16384];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        bytes.Write(buffer, 0, read);
                        if (bytes.Length > 5 * 1024 * 1024) throw new IOException("Download exceeds the expected size.");
                    }
                    Engine.AtomicBytes(destination, bytes.ToArray());
                }
                if (!Engine.HashFile(destination).Equals(Hashes[i], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Official download checksum did not match: " + Names[i] + ". No browser files were changed.");
            }
        }

        private static Dictionary<string, byte[]> ReadZip(string path)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using (var archive = ZipFile.OpenRead(path))
            {
                if (archive.Entries.Count > 1000) throw new IOException("Too many files in a Sine package.");
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('/', '\\').TrimEnd('\\');
                    if (String.IsNullOrEmpty(relative)) continue;
                    if (relative.Contains("..") || relative.Contains(":") || Path.IsPathRooted(relative) || relative.IndexOf('\0') >= 0)
                        throw new IOException("Unsafe path in Sine package.");
                    if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0)
                        throw new IOException("Links are not supported in Sine packages.");
                    if (entry.FullName.EndsWith("/")) continue;
                    if (files.ContainsKey(relative) || entry.Length > 5 * 1024 * 1024 || (total += entry.Length) > 20 * 1024 * 1024)
                        throw new IOException("Unexpected or oversized Sine package.");
                    using (var stream = entry.Open())
                    using (var output = new MemoryStream())
                    { stream.CopyTo(output); files.Add(relative, output.ToArray()); }
                }
            }
            return files;
        }

        private static void CheckAutoconfig(string firefox)
        {
            string config = Engine.Inside(firefox, "config.js");
            if (File.Exists(config) && !File.ReadAllText(config).Contains("sine.sys.mjs"))
                throw new IOException("Firefox already has a different config.js. Use the official Sine installer to resolve the AutoConfig conflict; your existing configuration was preserved.");
            string prefs = Engine.Inside(firefox, @"defaults\pref");
            if (Directory.Exists(prefs))
                foreach (string file in Directory.GetFiles(prefs, "*.js"))
                {
                    Engine.CheckPath(file);
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "pref\\(\\s*['\"]general\\.config\\.filename['\"]\\s*,\\s*['\"]([^'\"]+)['\"]"))
                        if (m.Groups[1].Value != "config.js") throw new IOException("Another AutoConfig loader is configured in Firefox. Your configuration was preserved. Use the official Sine installer for this setup.");
                }
        }

        private static Dictionary<string, byte[]> BuildFiles(string firefox, string profile, string packages, bool includeMod)
        {
            int count = includeMod ? Names.Length : 3;
            for (int i = 0; i < count; i++)
                if (!Engine.HashFile(Engine.Inside(packages, Names[i])).Equals(Hashes[i], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Package checksum mismatch: " + Names[i]);
            CheckAutoconfig(firefox);
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in ReadZip(Engine.Inside(packages, "program.zip")))
            {
                if (file.Key != "config.js" && file.Key != @"defaults\pref\config-prefs.js") throw new IOException("Unexpected program file in Sine package.");
                result.Add(@"Program\" + file.Key, file.Value);
            }
            foreach (string name in new[] { "profile.zip", "engine.zip" })
                foreach (var file in ReadZip(Engine.Inside(packages, name)))
                {
                    string prefix = name == "profile.zip" ? @"utils\" : @"JS\";
                    if (!file.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected profile file in Sine package.");
                    result.Add(@"Profile\chrome\" + file.Key, file.Value);
                }
            if (!result.ContainsKey(@"Program\config.js") || !result.ContainsKey(@"Profile\chrome\JS\sine.sys.mjs") || !result.ContainsKey(@"Profile\chrome\utils\chrome.manifest"))
                throw new IOException("A Sine package is incomplete.");
            result.Add(@"Profile\compatibility.ini", CacheInvalidationBytes(profile));
            string registry = Engine.Inside(profile, @"chrome\sine-mods\mods.json");
            Dictionary<string, object> mods;
            try
            {
                mods = File.Exists(registry) ? Json.DeserializeObject(File.ReadAllText(registry)) as Dictionary<string, object> : new Dictionary<string, object>();
                if (mods == null) throw new FormatException();
                foreach (object value in mods.Values) if (!(value is Dictionary<string, object>)) throw new FormatException();
            }
            catch (Exception e) { throw new IOException("Existing Sine mods.json is invalid. It was preserved. " + e.Message); }
            if (!includeMod && !File.Exists(registry)) result.Add(@"Profile\chrome\sine-mods\mods.json", new UTF8Encoding(false).GetBytes("{}"));
            if (includeMod)
            {
                var metadata = Json.DeserializeObject(File.ReadAllText(Engine.Inside(packages, "theme.json"))) as Dictionary<string, object>;
                if (metadata == null || !metadata.ContainsKey("id") || (string)metadata["id"] != ModId) throw new IOException("Unexpected FireZEN mod ID.");
                object previous;
                if (mods.TryGetValue(ModId, out previous))
                {
                    var old = previous as Dictionary<string, object>;
                    object home;
                    if (old != null && old.TryGetValue("homepage", out home) && !String.Equals(home as string, "CosmoCreeper/FireZen", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The FireZEN mod ID is already used by another mod. No files were changed.");
                }
                foreach (string id in mods.Keys.ToList())
                {
                    var mod = (Dictionary<string, object>)mods[id];
                    object home;
                    if (id != ModId && mod.TryGetValue("homepage", out home) && String.Equals(home as string, "CosmoCreeper/FireZen", StringComparison.OrdinalIgnoreCase)) mods.Remove(id);
                }
                metadata["homepage"] = "CosmoCreeper/FireZen";
                metadata["style"] = new Dictionary<string, object> { { "chrome", "userChrome.css" }, { "content", "" } };
                metadata["preferences"] = "preferences.json";
                metadata["enabled"] = true;
                object noUpdates;
                var existing = previous as Dictionary<string, object>;
                metadata["no-updates"] = existing != null && existing.TryGetValue("no-updates", out noUpdates) && noUpdates is bool ? (bool)noUpdates : false;
                mods[ModId] = metadata;
                result.Add(@"Profile\chrome\sine-mods\mods.json", new UTF8Encoding(false).GetBytes(Json.Serialize(mods)));
                for (int i = 3; i < Names.Length; i++) result.Add(@"Profile\chrome\sine-mods\" + ModId + "\\" + Names[i], File.ReadAllBytes(Engine.Inside(packages, Names[i])));
            }
            return result;
        }

        private static byte[] CacheInvalidationBytes(string profile)
        {
            return Engine.CacheInvalidationBytes(profile);
        }

        private static string Resolve(SineBackup manifest, SineFileRecord record)
        {
            string rel = record.RelativePath;
            if (String.IsNullOrEmpty(rel) || rel.Contains("..") || rel.Contains("/") || Path.IsPathRooted(rel)) throw new IOException("Unexpected Sine backup path.");
            if (record.Scope == "Program" && (rel == "config.js" || rel == @"defaults\pref\config-prefs.js")) return Engine.Inside(manifest.FirefoxPath, rel);
            if (record.Scope == "Profile" && (rel == "compatibility.ini" || rel.StartsWith(@"chrome\JS\", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(@"chrome\utils\", StringComparison.OrdinalIgnoreCase) ||
                rel == @"chrome\sine-mods\mods.json" || rel.StartsWith(@"chrome\sine-mods\" + ModId + "\\", StringComparison.OrdinalIgnoreCase)))
                return Engine.Inside(manifest.ProfilePath, rel);
            throw new IOException("Unexpected file scope in Sine backup.");
        }

        private static bool Overlaps(string a, string b)
        {
            a = Path.GetFullPath(a).TrimEnd('\\') + "\\";
            b = Path.GetFullPath(b).TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static void Save(string backup, SineBackup manifest)
        {
            Engine.AtomicBytes(Engine.Inside(backup, "sine-manifest.json"), new UTF8Encoding(false).GetBytes(Json.Serialize(manifest)));
        }

        public static void ValidateSetup(string firefox, string profile, string backupRoot)
        {
            firefox = Engine.ReadBrowser(firefox, false).Path;
            profile = ValidateProfile(profile);
            backupRoot = Path.GetFullPath(backupRoot);
            Engine.CheckPath(backupRoot);
            if (Overlaps(firefox, profile) || Overlaps(backupRoot, firefox) || Overlaps(backupRoot, profile)) throw new IOException("Keep the profile, installation and backups in separate folders.");
            CheckAutoconfig(firefox);
            Engine.CheckBrowsersClosed();
        }

        public static string InstallFromPackages(string firefox, string profile, string backupRoot, string packages, bool includeMod, Action<ProgressUpdate> progress)
        {
            using (Mutex mutex = Engine.OperationLock())
            try
            {
                firefox = Engine.ReadBrowser(firefox, false).Path;
                profile = ValidateProfile(profile);
                packages = Engine.NormalizeFolder(packages);
                backupRoot = Path.GetFullPath(backupRoot);
                Engine.CheckPath(backupRoot);
                if (Overlaps(firefox, profile) || Overlaps(backupRoot, firefox) || Overlaps(backupRoot, profile)) throw new IOException("Keep the profile, installation and backups in separate folders.");
                Engine.CheckBrowsersClosed();
                Dictionary<string, byte[]> files = BuildFiles(firefox, profile, packages, includeMod);
                string backup = Engine.Inside(backupRoot, @"Sine\" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var manifest = new SineBackup { Kind = "Sine", Format = 1, CreatedUtc = DateTime.UtcNow.ToString("o"), FirefoxPath = firefox, ProfilePath = profile, Status = "Preparing", Files = new List<SineFileRecord>() };
                // Preflight all destinations before creating the backup or writing browser files.
                foreach (var file in files)
                {
                    int at = file.Key.IndexOf('\\');
                    var record = new SineFileRecord { Scope = file.Key.Substring(0, at), RelativePath = file.Key.Substring(at + 1) };
                    string target = Resolve(manifest, record);
                    if (Directory.Exists(target)) throw new IOException("A directory blocks the Sine file: " + target);
                    record.Existed = File.Exists(target);
                    if (record.Existed) record.OriginalHash = Engine.HashFile(target);
                    manifest.Files.Add(record);
                }
                Directory.CreateDirectory(backup);
                Save(backup, manifest);
                try
                {
                    for (int i = 0; i < manifest.Files.Count; i++)
                    {
                        SineFileRecord record = manifest.Files[i];
                        string key = record.Scope + "\\" + record.RelativePath;
                        Report(progress, "SineBackup", "Backing up " + key, 15 + i * 35 / manifest.Files.Count);
                        string payload = Engine.Inside(backup, @"payload\" + key);
                        Engine.AtomicBytes(payload, files[key]);
                        record.AppliedHash = Engine.HashFile(payload);
                        if (record.AppliedHash != Engine.HashBytes(files[key])) throw new IOException("Sine staging verification failed.");
                        if (record.Existed)
                        {
                            string original = Engine.Inside(backup, @"original\" + key);
                            Engine.AtomicCopy(Resolve(manifest, record), original);
                            if (Engine.HashFile(original) != record.OriginalHash) throw new IOException("Sine backup verification failed.");
                        }
                    }
                    Engine.CheckBrowsersClosed();
                    foreach (SineFileRecord record in manifest.Files)
                    {
                        string path = Resolve(manifest, record);
                        if (File.Exists(path) != record.Existed || (record.Existed && Engine.HashFile(path) != record.OriginalHash)) throw new IOException("Firefox or its profile changed while preparing the Sine backup.");
                    }
                    manifest.Status = "Applying";
                    Save(backup, manifest);
                    for (int i = 0; i < manifest.Files.Count; i++)
                    {
                        SineFileRecord record = manifest.Files[i];
                        manifest.TouchedCount = i + 1;
                        Save(backup, manifest);
                        Engine.AtomicCopy(Engine.Inside(backup, @"payload\" + record.Scope + "\\" + record.RelativePath), Resolve(manifest, record));
                        if (Engine.HashFile(Resolve(manifest, record)) != record.AppliedHash) throw new IOException("Sine file verification failed.");
                        Report(progress, "SineAppliedFile", "Installed " + record.RelativePath, 50 + (i + 1) * 50 / manifest.Files.Count);
                    }
                    manifest.Status = "Applied";
                    Save(backup, manifest);
                    return backup;
                }
                catch (Exception error)
                {
                    try { RestoreFiles(backup, manifest, null); manifest.Status = "RolledBack"; Save(backup, manifest); }
                    catch (Exception recovery)
                    {
                        manifest.Status = "RecoveryRequired";
                        try { Save(backup, manifest); } catch { }
                        throw new IOException("Sine installation stopped and automatic recovery could not finish. Keep Firefox closed and use Restore Sine backup. Backup: " + backup + "\r\n" + recovery.Message, error);
                    }
                    throw new IOException("Sine installation stopped. Changed files were restored. " + error.Message, error);
                }
            }
            finally { mutex.ReleaseMutex(); }
        }

        private static void RestoreFiles(string backup, SineBackup manifest, Action<ProgressUpdate> progress)
        {
            foreach (SineFileRecord record in manifest.Files.Take(manifest.TouchedCount))
                if (record.Existed && Engine.HashFile(Engine.Inside(backup, @"original\" + record.Scope + "\\" + record.RelativePath)) != record.OriginalHash)
                    throw new IOException("Sine backup is incomplete or damaged.");
            for (int i = manifest.TouchedCount - 1; i >= 0; i--)
            {
                SineFileRecord record = manifest.Files[i];
                string path = Resolve(manifest, record);
                if (record.Existed)
                {
                    Engine.AtomicCopy(Engine.Inside(backup, @"original\" + record.Scope + "\\" + record.RelativePath), path);
                    if (Engine.HashFile(path) != record.OriginalHash) throw new IOException("Sine restore verification failed.");
                }
                else if (File.Exists(path)) File.Delete(path);
                Report(progress, "SineRestoring", "Restored " + record.RelativePath, (manifest.TouchedCount - i) * 100 / Math.Max(1, manifest.TouchedCount));
            }
        }

        public static string LatestBackup(string root, string firefox, string profile)
        {
            string folder = Engine.Inside(root, "Sine");
            if (!Directory.Exists(folder)) return null;
            var candidates = new List<Tuple<string, DateTime>>();
            foreach (string child in Directory.GetDirectories(folder))
                try
                {
                    SineBackup m = Json.Deserialize<SineBackup>(File.ReadAllText(Engine.Inside(child, "sine-manifest.json")));
                    DateTime time;
                    if (m != null && m.Kind == "Sine" && String.Equals(m.FirefoxPath, firefox, StringComparison.OrdinalIgnoreCase) && String.Equals(m.ProfilePath, profile, StringComparison.OrdinalIgnoreCase) &&
                        (m.Status == "Applied" || m.Status == "Applying" || m.Status == "RecoveryRequired") && DateTime.TryParse(m.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) candidates.Add(Tuple.Create(child, time));
                }
                catch (IOException) { }
                catch (ArgumentException) { }
            return candidates.OrderByDescending(t => t.Item2).Select(t => t.Item1).FirstOrDefault();
        }

        public static void Restore(string backup, string firefox, string profile, Action<ProgressUpdate> progress)
        {
            using (Mutex mutex = Engine.OperationLock())
            try
            {
                Engine.CheckBrowsersClosed();
                firefox = Engine.NormalizeFolder(firefox); profile = ValidateProfile(profile); backup = Engine.NormalizeFolder(backup);
                SineBackup m = Json.Deserialize<SineBackup>(File.ReadAllText(Engine.Inside(backup, "sine-manifest.json")));
                if (m == null || m.Kind != "Sine" || m.Format != 1 || m.Files == null || m.Files.Count == 0 || m.Files.Count > 10000 || m.TouchedCount < 0 || m.TouchedCount > m.Files.Count ||
                    !String.Equals(m.FirefoxPath, firefox, StringComparison.OrdinalIgnoreCase) || !String.Equals(m.ProfilePath, profile, StringComparison.OrdinalIgnoreCase) ||
                    !(m.Status == "Applied" || m.Status == "Applying" || m.Status == "RecoveryRequired")) throw new IOException("This is not an applied Sine backup for the selected installation and profile.");
                m.FirefoxPath = firefox; m.ProfilePath = profile;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SineFileRecord record in m.Files) if (!paths.Add(Resolve(m, record))) throw new IOException("Duplicate file in Sine backup.");
                try { RestoreFiles(backup, m, progress); m.Status = "Restored"; Save(backup, m); }
                catch (Exception failure)
                {
                    m.Status = "RecoveryRequired"; try { Save(backup, m); } catch { }
                    throw new IOException("Sine restore could not finish. Keep Firefox closed and retry Restore Sine backup. " + failure.Message, failure);
                }
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
