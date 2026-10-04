using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using System.Threading;

namespace RouteFilterCleanup
{

public sealed class Mod : IMod
{
    public const string Id = "RouteFilterCleanup";
    public const string BuildId = "RFCLEAN-20260926-FULL-REFRESH-02";
    public const string CleanupAction = "RunSafeCleanup";
    public static readonly ILog Log = LogManager.GetLogger(Id).SetShowsErrorsInUI(false);
    public static Setting Settings { get; private set; } = null!;
    private static int s_CleanupRequested;
    private static int s_ScanRequested;
    private static int s_NetworkRefreshRequested;

    internal static void RequestScan()
    {
        if (GameManager.instance.gameMode == GameMode.Game)
            Interlocked.Exchange(ref s_ScanRequested, 1);
    }

    internal static bool ConsumeScanRequest() => Interlocked.Exchange(ref s_ScanRequested, 0) != 0;

    internal static void RequestNetworkRefresh()
    {
        if (GameManager.instance.gameMode == GameMode.Game)
            Interlocked.Exchange(ref s_NetworkRefreshRequested, 1);
    }

    internal static bool ConsumeNetworkRefreshRequest() => Interlocked.Exchange(ref s_NetworkRefreshRequested, 0) != 0;

    internal static void RequestCleanup()
    {
        if (GameManager.instance.gameMode == GameMode.Game)
            Interlocked.Exchange(ref s_CleanupRequested, 1);
    }

    internal static bool ConsumeCleanupRequest() => Interlocked.Exchange(ref s_CleanupRequested, 0) != 0;

    public void OnLoad(UpdateSystem updateSystem)
    {
        Log.Info($"OnLoad build={BuildId}; RouteFilter main mod must remain disabled");
        Settings = new Setting(this);
        AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));
        Settings.RegisterInOptionsUI();
        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
        GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleZH(Settings));
        GameManager.instance.localizationManager.AddSource("zh-CN", new LocaleZH(Settings));
        updateSystem.UpdateAt<CleanupSystem>(SystemUpdatePhase.UIUpdate);
        updateSystem.UpdateAt<RoadNetworkRefreshSystem>(SystemUpdatePhase.GameSimulation);
    }

    public void OnDispose()
    {
        Interlocked.Exchange(ref s_CleanupRequested, 0);
        Interlocked.Exchange(ref s_ScanRequested, 0);
        Interlocked.Exchange(ref s_NetworkRefreshRequested, 0);
        Settings?.UnregisterInOptionsUI();
        Log.Info("OnDispose");
    }
}
}
