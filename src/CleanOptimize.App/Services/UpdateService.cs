using System.Reflection;

namespace CleanOptimize.App.Services;

/// <summary>离线版本信息；本版本不执行联网更新检查。</summary>
public static class UpdateService
{
    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0";

    public static string OfflineMessage => Loc.T("privacy.offline");
}
