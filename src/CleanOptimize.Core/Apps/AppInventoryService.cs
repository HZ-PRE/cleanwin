using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Apps;

public sealed class InstalledApp
{
    public string DisplayName { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public long EstimatedSize { get; set; }
    public DateTime InstallDate { get; set; }
    public bool IsStoreApp { get; set; }
    public string StorePackageFullName { get; set; } = "";
    /// <summary>用于图标提取的 exe 路径。</summary>
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; }
    public string RegistryKey { get; set; } = "";
    public bool IsSystemComponent { get; set; }

}

/// <summary>应用卸载后可能残留的文件目录 / 注册表键。</summary>
public sealed class LeftoverItem
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public bool IsRegistry { get; set; }
    public bool Selected { get; set; } = true;
    public string Description { get; set; } = "";
}

/// <summary>
/// 已安装应用清单：注册表卸载表（含 WOW6432Node）+ Microsoft Store 应用（PowerShell 按需枚举）。
/// </summary>
public sealed class AppInventoryService
{
    private List<InstalledApp>? _storeCache;
    private DateTime _storeCacheTime = DateTime.MinValue;

    private static readonly string[] UninstallKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public List<InstalledApp> EnumerateWin32()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        void ReadRoot(RegistryKey root, string sub)
        {
            try
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) return;
                foreach (var keyName in k.GetSubKeyNames())
                {
                    try
                    {
                        using var app = k.OpenSubKey(keyName);
                        if (app == null) continue;
                        var name = app.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if ((app.GetValue("SystemComponent") as int?) == 1) continue;
                        var releaseType = app.GetValue("ReleaseType") as string;
                        if (releaseType is "Hotfix" or "Security Update" or "Update Rollup") continue;
                        if (name.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && releaseType != null) continue;

                        var fullKey = root.Name + "\\" + sub + "\\" + keyName;
                        if (apps.ContainsKey(fullKey)) continue;

                        var sizeKb = app.GetValue("EstimatedSize") as int? ?? 0;
                        var item = new InstalledApp
                        {
                            DisplayName = name,
                            DisplayVersion = app.GetValue("DisplayVersion") as string ?? "",
                            Publisher = app.GetValue("Publisher") as string ?? "",
                            InstallLocation = app.GetValue("InstallLocation") as string ?? "",
                            UninstallString = app.GetValue("UninstallString") as string ?? "",
                            QuietUninstallString = app.GetValue("QuietUninstallString") as string ?? "",
                            EstimatedSize = sizeKb * 1024L,
                            RegistryKey = fullKey,
                        };
                        item.IsSystemComponent = (app.GetValue("WindowsInstaller") as int?) == 1 &&
                                                 string.Equals(item.Publisher, "Microsoft Corporation", StringComparison.OrdinalIgnoreCase) &&
                                                 name.Contains("Microsoft Visual C++");
                        var dateStr = app.GetValue("InstallDate") as string;
                        item.InstallDate = ParseInstallDate(dateStr);
                        var iconRaw = app.GetValue("DisplayIcon") as string;
                        if (TryResolveIcon(iconRaw, out var iconPath, out var iconIndex) ||
                            TryResolveIcon(item.UninstallString, out iconPath, out iconIndex))
                        {
                            item.IconPath = iconPath;
                            item.IconIndex = iconIndex;
                        }
                        apps[fullKey] = item;
                    }
                    catch { }
                }
            }
            catch { }
        }

        ReadRoot(Registry.CurrentUser, UninstallKeys[0]);
        ReadRoot(Registry.LocalMachine, UninstallKeys[0]);
        ReadRoot(Registry.LocalMachine, UninstallKeys[1]);
        return apps.Values.OrderBy(a => a.DisplayName).ToList();
    }

    public List<InstalledApp> EnumerateStoreApps()
    {
        if (_storeCache != null && (DateTime.Now - _storeCacheTime).TotalMinutes < 30) return _storeCache;
        try
        {
            var script = "Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' } | Select-Object Name,PackageFullName,Version,Publisher | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command " + script)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var json = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            if (p.ExitCode != 0) return _storeCache ?? new();
            json = json.Trim();
            var items = new List<InstalledApp>();
            if (json.StartsWith('['))
            {
                var list = JsonSerializer.Deserialize<List<StorePkg>>(json);
                foreach (var i in list ?? new()) items.Add(ToApp(i));
            }
            else if (json.StartsWith('{'))
            {
                var i = JsonSerializer.Deserialize<StorePkg>(json);
                if (i != null) items.Add(ToApp(i));
            }
            _storeCache = items;
            _storeCacheTime = DateTime.Now;
            return items;
        }
        catch
        {
            return _storeCache ?? new();
        }
    }

    private sealed class StorePkg
    {
        public string Name { get; set; } = "";
        public string PackageFullName { get; set; } = "";
        public string Version { get; set; } = "";
        public string Publisher { get; set; } = "";
    }

    private static InstalledApp ToApp(StorePkg p) => new()
    {
        DisplayName = p.Name.StartsWith("Microsoft.") ? p.Name[(p.Name.LastIndexOf('.') + 1)..] : p.Name,
        DisplayVersion = p.Version,
        Publisher = p.Publisher,
        IsStoreApp = true,
        StorePackageFullName = p.PackageFullName,
        IconPath = "",
    };

    /// <summary>启动卸载流程。返回 null 表示已启动；否则错误说明。</summary>
    public string? LaunchUninstall(InstalledApp app)
    {
        try
        {
            if (app.IsStoreApp)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(app.StorePackageFullName, @"\A[A-Za-z0-9_.-]+\z"))
                    return "无效的应用包标识";
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{app.StorePackageFullName}'\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                };
                Process.Start(psi);
                return null;
            }
            if (string.IsNullOrWhiteSpace(app.UninstallString)) return Store.CoreLocalizer.T("apps.no.uninstaller", "该应用未提供卸载程序");
            Process.Start(BuildUninstallProcess(app.UninstallString));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    internal static ProcessStartInfo BuildUninstallProcess(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command).Trim();
        var pattern = text.StartsWith('"') ? "^\"([^\"]+\\.exe)\"(?:\\s+(.*))?$" : @"^(.+?\.exe)(?:\s+(.*))?$";
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || text.Any(char.IsControl)) throw new InvalidOperationException("不支持的卸载命令，请从 Windows 设置卸载");
        var executable = match.Groups[1].Value;
        if (executable.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            executable = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        var blocked = new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe" };
        if (!PathUtil.IsLocalPath(executable) || PathUtil.HasReparseAncestor(executable) ||
            blocked.Contains(Path.GetFileName(executable), StringComparer.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new InvalidOperationException("卸载程序必须是本地可执行文件，不能调用命令解释器");
        return new ProcessStartInfo(executable, match.Groups[2].Value) { UseShellExecute = true };
    }

    public bool IsStillInstalled(InstalledApp app)
    {
        try
        {
            if (app.IsStoreApp)
                return EnumerateStoreApps().Any(a => a.StorePackageFullName == app.StorePackageFullName);
            var parts = SplitRegistryPath(app.RegistryKey);
            if (parts == null) return true;
            using var root = parts.Value.root == RegistryHive.LocalMachine
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var k = root.OpenSubKey(parts.Value.sub);
            return k?.GetSubKeyNames().Contains(parts.Value.leaf, StringComparer.OrdinalIgnoreCase) == true;
        }
        catch { return true; }
    }

    private static (RegistryHive root, string sub, string leaf)? SplitRegistryPath(string full)
    {
        if (string.IsNullOrEmpty(full)) return null;
        RegistryHive hive;
        string rest;
        if (full.StartsWith("HKEY_LOCAL_MACHINE\\")) { hive = RegistryHive.LocalMachine; rest = full["HKEY_LOCAL_MACHINE\\".Length..]; }
        else if (full.StartsWith("HKEY_CURRENT_USER\\")) { hive = RegistryHive.CurrentUser; rest = full["HKEY_CURRENT_USER\\".Length..]; }
        else return null;
        var leafStart = rest.LastIndexOf('\\');
        if (leafStart < 0) return null;
        return (hive, rest[..leafStart], rest[(leafStart + 1)..]);
    }

    // ————— 卸载残留 —————

    /// <summary>基于应用名/安装目录的确定性残留检测（不做模糊全局搜索）。</summary>
    public List<LeftoverItem> FindLeftovers(InstalledApp app)
    {
        var result = new List<LeftoverItem>();
        var tokens = NameTokens(app);
        if (tokens.Count == 0) return result;

        var userLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var token in tokens)
        {
            var candidates = new[]
            {
                Path.Combine(userLocal, token),
                Path.Combine(userRoaming, token),
                Path.Combine(programData, token),
                Path.Combine(pf, token),
                Path.Combine(pf86, token),
            };
            foreach (var c in candidates)
            {
                try
                {
                    if (!PathUtil.IsLocalPath(c) || PathUtil.HasReparseAncestor(c) ||
                        Security.SafetyGuard.FindDenyReason(c) != null || !Directory.Exists(c)) continue;
                    // InstallLocation 本身只有在注册表卸载条目已消失时才算残留（调用方负责时机）
                    result.Add(new LeftoverItem
                    {
                        Path = c,
                        Size = FileUtil.DirSize(c),
                        Description = "数据目录",
                        Selected = true,
                    });
                }
                catch { }
            }
            // HKCU\Software\<Token>
            try
            {
                var regPath = Path.Combine("Software", token);
                using var k = Registry.CurrentUser.OpenSubKey(regPath);
                if (k != null)
                {
                    result.Add(new LeftoverItem
                    {
                        Path = "reg://HKCU\\" + regPath,
                        IsRegistry = true,
                        Description = "设置注册表键",
                        Selected = true,
                    });
                }
            }
            catch { }
        }

        // InstallLocation 残留
        try
        {
            if (PathUtil.IsLocalPath(app.InstallLocation) && !PathUtil.HasReparseAncestor(app.InstallLocation) &&
                Security.SafetyGuard.FindDenyReason(app.InstallLocation) == null && Directory.Exists(app.InstallLocation) &&
                SafetyCheck.InstallLocationRemovable(app.InstallLocation))
            {
                result.Add(new LeftoverItem
                {
                    Path = app.InstallLocation,
                    Size = FileUtil.DirSize(app.InstallLocation),
                    Description = "安装目录",
                    Selected = true,
                });
            }
        }
        catch { }

        return result.DistinctBy(r => r.Path.TrimEnd('\\')).Where(r => r.Size > 0 || r.IsRegistry).ToList();
    }

    private static List<string> NameTokens(InstalledApp app)
    {
        var tokens = new List<string>();
        void Add(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return;
            t = t.Trim();
            if (!IsSafeLeftoverToken(t)) return;
            if (tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) return;
            tokens.Add(t);
        }
        Add(app.DisplayName.Split(" v")[0].Split(" for ")[0].Trim());
        // 从 InstallLocation 取目录名
        try
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                var norm = app.InstallLocation.TrimEnd('\\');
                Add(Path.GetFileName(norm));
            }
        }
        catch { }
        // 从卸载命令路径推断
        try
        {
            if (!string.IsNullOrWhiteSpace(app.UninstallString))
            {
                var exe = PathUtil.Normalize(app.UninstallString.Trim('"').Split(" -")[0]);
                if (PathUtil.IsLocalPath(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                    Add(Path.GetFileName(Path.GetDirectoryName(exe)));
            }
        }
        catch { }
        return tokens;
    }

    /// <summary>删除残留：目录进隔离区，注册表键直接删除（先导出备份 json）。</summary>
    public (int ok, int fail) RemoveLeftovers(IEnumerable<LeftoverItem> items)
    {
        int ok = 0, fail = 0;
        foreach (var item in items.Where(i => i.Selected))
        {
            if (item.IsRegistry)
            {
                try
                {
                    var sub = item.Path["reg://".Length..];
                    var hiveIdx = sub.IndexOf('\\');
                    var hive = sub[..hiveIdx];
                    var keyPath = sub[(hiveIdx + 1)..];
                    if (hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) &&
                        keyPath.StartsWith("Software\\", StringComparison.OrdinalIgnoreCase) && IsSafeLeftoverToken(keyPath[9..]))
                    {
                        if (!BackupRegistryKey("HKCU\\" + keyPath)) { fail++; continue; }
                        using var k = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                        if (k != null)
                        {
                            // 删除键下所有值与子键后删除自身
                            foreach (var child in k.GetSubKeyNames())
                                try { k.DeleteSubKeyTree(child); } catch { }
                            foreach (var val in k.GetValueNames())
                                try { k.DeleteValue(val, false); } catch { }
                        }
                        DeleteKeySelf(Registry.CurrentUser, keyPath);
                        ok++;
                    }
                    else fail++;
                }
                catch { fail++; }
            }
            else
            {
                if (Security.SafetyGuard.ValidateForClean(item.Path, new[] { item.Path },
                    SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase)) == null &&
                    QuarantineService.Instance.MoveIntoQuarantine(item.Path, "leftover", out _)) ok++;
                else fail++;
            }
        }
        if (ok > 0)
            HistoryService.Add("Clean", string.Format(Store.CoreLocalizer.T("leftover.cleaned.log", "清理了 {0} 项卸载残留"), ok));
        return (ok, fail);
    }

    private static void DeleteKeySelf(RegistryKey root, string keyPath)
    {
        var parentIdx = keyPath.LastIndexOf('\\');
        if (parentIdx < 0) { try { root.DeleteSubKeyTree(keyPath); } catch { } return; }
        var parent = keyPath[..parentIdx];
        var leaf = keyPath[(parentIdx + 1)..];
        try
        {
            using var pk = root.OpenSubKey(parent, writable: true);
            pk?.DeleteSubKeyTree(leaf);
        }
        catch { }
    }

    internal static bool IsSafeLeftoverToken(string token) => token.Length is >= 2 and <= 60 &&
        token is not "." and not ".." && token.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !new[] { "Microsoft", "Windows", "Classes", "Policies", "Clients", "RegisteredApplications", "Common Files", "Programs" }
            .Contains(token, StringComparer.OrdinalIgnoreCase);

    private static bool BackupRegistryKey(string fullPath)
    {
        try
        {
            var dir = Path.Combine(SettingsService.DataDir, "RegistryBackups");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"reg_{DateTime.Now:yyyyMMdd_HHmmss}_{Math.Abs(fullPath.GetHashCode()):X8}.reg");
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            foreach (var argument in new[] { "export", fullPath, file, "/y" }) psi.ArgumentList.Add(argument);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(15000)) { p.Kill(); return false; }
            return p.ExitCode == 0 && File.Exists(file);
        }
        catch { return false; }
    }

    private static DateTime ParseInstallDate(string? s)
    {
        if (!string.IsNullOrEmpty(s) && s.Length == 8 &&
            DateTime.TryParseExact(s, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
            return d;
        return default;
    }

    internal static bool TryResolveIcon(string? raw, out string path, out int index)
    {
        path = ""; index = 0;
        if (string.IsNullOrWhiteSpace(raw) || raw.Any(char.IsControl)) return false;
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(Environment.ExpandEnvironmentVariables(raw).Trim(),
                "^(?:\"(?<path>[^\"]+)\"|(?<path>.+?\\.(?:exe|dll|ico|png)))\\s*(?:,\\s*(?<index>-?\\d+)|\\s+.*)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var candidate = match.Groups["path"].Value;
            if (!new[] { ".exe", ".dll", ".ico", ".png" }.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase) ||
                !PathUtil.IsLocalPath(candidate) || PathUtil.HasReparseAncestor(candidate) || !File.Exists(candidate)) return false;
            if (match.Groups["index"].Success && !int.TryParse(match.Groups["index"].Value, out index)) return false;
            path = candidate;
            return true;
        }
        catch { return false; }
    }
}

/// <summary>安装目录是否可被当作残留移除（防御性检查）。</summary>
internal static class SafetyCheck
{
    public static bool InstallLocationRemovable(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.TrimEnd('\\'));
            // 不在 Program Files / Windows / 根目录下
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            })
            {
                if (full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (full.Length <= 3) return false;
            return true;
        }
        catch { return false; }
    }
}
