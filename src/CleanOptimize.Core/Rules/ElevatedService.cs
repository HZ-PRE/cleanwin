using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Rules;

public sealed class ElevatedResult
{
    public bool Ok { get; set; }
    public string TaskId { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// 按需提权：主界面进程保持普通权限，仅在需要时拉起带 UAC 的自身实例执行
/// 白名单内的管理任务（清传递优化缓存 / 刷新 DNS / 修改 HKLM 启动项 / 计划任务开关）。
/// 结果通过 %TEMP% 下的结果文件回传。
/// </summary>
public static class ElevatedService
{
    public static bool IsCurrentElevated()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static async Task<bool> RunTaskAsync(string taskId, string argsJson)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            var resultFile = Path.Combine(Path.GetTempPath(), $"CleanOptimize_elev_{Guid.NewGuid():N}.json");
            var psi = new ProcessStartInfo(exe)
            {
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var argument in new[] { "--elevated", taskId, argsJson, resultFile }) psi.ArgumentList.Add(argument);
            using var p = Process.Start(psi);
            if (p == null) return false;

            // 等待结果文件（最多 90 秒）
            for (int i = 0; i < 900; i++)
            {
                if (File.Exists(resultFile)) break;
                await Task.Delay(100);
                if (i == 899) return false;
            }
            await Task.Delay(200);
            var json = File.ReadAllText(resultFile);
            try { File.Delete(resultFile); } catch { }
            var result = JsonSerializer.Deserialize<ElevatedResult>(json);
            return result?.Ok == true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // 用户取消 UAC
        }
        catch
        {
            return false;
        }
    }

    /// <summary>被提权实例调用：执行任务并写出结果。白名单之外的任务一律拒绝。</summary>
    public static void ExecuteElevatedTask(string taskId, string argsJson, string resultFile)
    {
        if (!IsSafeResultPath(resultFile)) return;
        var result = new ElevatedResult { TaskId = taskId };
        try
        {
            if (!IsCurrentElevated())
                throw new InvalidOperationException("not elevated");

            switch (taskId)
            {
                case "clear-do-cache":
                {
                    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                        "-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; Delete-DeliveryOptimizationCache -Force\"")
                    { CreateNoWindow = true, UseShellExecute = false };
                    using var p = Process.Start(psi)!;
                    if (!p.WaitForExit(120000)) { p.Kill(); throw new TimeoutException(); }
                    result.Ok = p.ExitCode == 0;
                    break;
                }
                case "toggle-hklm-startup":
                {
                    var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
                    var sub = args!["sub"];            // e.g. Software\Microsoft\Windows\CurrentVersion\Run
                    var value = args["value"];
                    var disable = args["disable"] == "1";
                    // 白名单原则:仅允许系统启动项相关键,拒绝任意 HKLM 写入
                    if (!IsAllowedStartupKey(sub) || value.Contains('\0'))
                        throw new InvalidOperationException("subkey not allowed");
                    using var k = Registry.LocalMachine.OpenSubKey(sub, writable: true)
                        ?? throw new InvalidOperationException("cannot open key");
                    var data = k.GetValue(value) as byte[];
                    if (data == null || data.Length != 12) throw new InvalidOperationException("invalid startup value");
                    var nv = (byte[])data.Clone();
                    nv[0] = disable ? (byte)3 : (byte)2;
                    for (int i = 1; i < nv.Length; i++) nv[i] = 0;
                    k.SetValue(value, nv, RegistryValueKind.Binary);
                    result.Ok = true;
                    break;
                }
                case "schtasks-toggle":
                {
                    var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
                    var name = args!["task"];
                    var disable = args["disable"] == "1";
                    // 计划任务名拼入命令行,拒绝引号与控制字符防止注入
                    if (name.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                        throw new InvalidOperationException("invalid task name");
                    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                    foreach (var arg in new[] { "/Change", "/TN", name, disable ? "/DISABLE" : "/ENABLE" }) psi.ArgumentList.Add(arg);
                    using var p = Process.Start(psi)!;
                    if (!p.WaitForExit(30000)) { p.Kill(); throw new TimeoutException(); }
                    result.Ok = p.ExitCode == 0;
                    break;
                }
                case "clean-windows-temp":
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                    var (ok, fail, _) = FileUtil.DeleteDirectoryChildren(dir);
                    result.Ok = ok > 0;
                    result.Message = $"ok={ok},fail={fail}";
                    break;
                }
                default:
                    throw new InvalidOperationException("unknown task");
            }
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Message = ex.Message;
        }
        try
        {
            using var output = new FileStream(resultFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, result);
        }
        catch { }
    }

    internal static bool IsAllowedStartupKey(string sub) => new[]
    {
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
    }.Contains(sub, StringComparer.OrdinalIgnoreCase);

    internal static bool IsSafeResultPath(string path)
    {
        try
        {
            if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path)) return false;
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            var name = Path.GetFileName(path);
            const string prefix = "CleanOptimize_elev_";
            return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) &&
                Guid.TryParseExact(name[prefix.Length..^5], "N", out _) && !File.Exists(path) && !Directory.Exists(path);
        }
        catch { return false; }
    }
}
