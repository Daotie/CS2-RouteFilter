using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using RouteFilter.Components;
using RouteFilter.Systems;
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Entities;

namespace RouteFilter;

public sealed class Mod : IMod
{
    public const string Id = "RouteFilter";
    public const string Version = "2.0.0";
    // Bump this for every deployable build so the in-game panel and log identify
    // exactly which compiled payload is loaded by the active playset.
    public const string BuildId = "RF2-20261004-ROAD-SIGNS-24";
    public const string ToggleToolAction = "ToggleRestrictionTool";
    public const string ApplyAction = "ApplyRestriction";
    public const string ClearAction = "ClearRestriction";

    public static readonly ILog Log = LogManager
        .GetLogger($"{Id}.{nameof(Mod)}")
        .SetShowsErrorsInUI(false);

    public static Setting Settings { get; private set; } = null!;
    public static ProxyAction ToggleTool { get; private set; } = null!;
    public static ProxyAction Apply { get; private set; } = null!;
    public static ProxyAction Clear { get; private set; } = null!;
    public static HashSet<Entity> SelectedVehicleAssets { get; } = new();
    public static RestrictionTargetMode SelectedTargetMode { get; set; } = RestrictionTargetMode.Node;

    /// <summary>Set whenever restriction data changes so cached indexes can be rebuilt.</summary>
    public static bool RestrictionsDirty { get; set; }
    private static int s_ResetRequested;
    private static int s_DiagnosticsRequested;
    private static int s_NativeProtocolRequested;
    private World m_RuntimeWorld;
    internal static void RequestDiagnosticsReport() => Interlocked.Exchange(ref s_DiagnosticsRequested, 1);
    internal static bool ConsumeDiagnosticsRequest() => Interlocked.Exchange(ref s_DiagnosticsRequested, 0) != 0;
    internal static void RequestNativeProtocolTest() => Interlocked.Exchange(ref s_NativeProtocolRequested, 1);
    internal static bool ConsumeNativeProtocolRequest() => Interlocked.Exchange(ref s_NativeProtocolRequested, 0) != 0;

    internal static void RequestReset()
    {
        if (GameManager.instance.gameMode == GameMode.Game)
            Interlocked.Exchange(ref s_ResetRequested, 1);
    }
    internal static bool ConsumeResetRequest() => Interlocked.Exchange(ref s_ResetRequested, 0) != 0;

    /// <summary>Optional read-only development trace selector used by Phase 1C diagnostics.</summary>
    public static Entity DebugVehicle = Entity.Null;

    /// <summary>
    /// True while key binding registration is waiting for a main-thread retry. Game 1.6.0f1
    /// can run mod OnLoad on a thread-pool continuation where the Input System's Temp
    /// allocator fails inside AddActionMap (ArgumentNullException: destination); on the main
    /// thread the same registration succeeds.
    /// </summary>
    public static bool KeyBindingsPending { get; private set; }

    public void OnLoad(UpdateSystem updateSystem)
    {
        m_RuntimeWorld = updateSystem.World;
        Log.Info($"{nameof(OnLoad)} build={BuildId}");
        try { RestrictionPathfindHook.Install(m_RuntimeWorld); }
        catch (Exception error) { Log.Error($"[RouteFilter.QueryExclusion] hook installation FAILED: {error}"); }

        try
        {
            Log.Info("[RouteFilter.Settings] Creating settings");
            Settings = new Setting(this);
            Log.Info("[RouteFilter.Settings] instance created");
            AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));
            Log.Info("[RouteFilter.Settings] LoadSettings success");
            // Options registration is performed in a live UI world after locales exist.
        }
        catch (Exception exception)
        {
            Log.Error($"[RouteFilter.Settings] Registration failed: {exception}");
            throw;
        }
        RegisterKeyBindingsSafe();

        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
        GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleZH(Settings));
        GameManager.instance.localizationManager.AddSource("zh-CN", new LocaleZH(Settings));
        Log.Info("[RouteFilter.Settings] locale registered en-US / zh-HANS / zh-CN");

        updateSystem.UpdateAt<RouteFilterSettingsUISystem>(SystemUpdatePhase.UIUpdate);
        updateSystem.UpdateAt<RestrictionShortcutSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAfter<RestrictionOverlaySystem, RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.Serialize);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.Deserialize);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.ModificationEnd);
        updateSystem.UpdateAt<RestrictionIndexSystem>(SystemUpdatePhase.GameSimulation);
        // UI updates also run while paused. Reset completes candidate/safety readers first.
        updateSystem.UpdateBefore<RouteFilterResetSystem, RouteFilterUISystem>(SystemUpdatePhase.UIUpdate);
        // Candidate collection must observe LaneObject buffers only after the vanilla
        // LaneObjectUpdater.Apply job in CarNavigationSystem.Actions.
        updateSystem.UpdateAfter<RestrictionCandidateSystem, Game.Simulation.CarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<RestrictionCandidateSystem, RestrictionIndexSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<RestrictionSafetySystem, RestrictionCandidateSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<RoadEnforcementCoordinator, RestrictionSafetySystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<RailEnforcementBackend, RestrictionSafetySystem>(SystemUpdatePhase.GameSimulation);
        // PathOwner serializes its flags. Owned Obsolete undo and query completion must precede the
        // game's SerializerSystem runs, not merely before RouteFilter's own callback.
        updateSystem.UpdateBefore<RoadEnforcementCoordinator, Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
        updateSystem.UpdateBefore<RailEnforcementBackend, Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
        updateSystem.UpdateAfter<RouteFilterDiagnosticsSystem, RoadEnforcementCoordinator>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAt<RouteFilterUISystem>(SystemUpdatePhase.UIUpdate);
        // Register static visuals before Objects.SearchSystem (Modification5).
        // UIUpdate is too late: next-frame cleanup strips Created/Updated first.
        updateSystem.UpdateAt<RoadRestrictionVisualSignsSystem>(SystemUpdatePhase.Modification4);
        updateSystem.UpdateBefore<RoadRestrictionSignSaveGuardSystem, Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
        updateSystem.UpdateAt<RoadRestrictionSignGeometryChangedSystem>(SystemUpdatePhase.ModificationEnd);
        updateSystem.UpdateAfter<RoadRestrictionSignSaveFinishSystem, Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
    }

    /// <summary>
    /// Attempts key binding registration and never lets it kill the mod: when the game
    /// initializes mods off the main thread (1.6.0f1), the Input System throws inside
    /// RegisterKeyBindings and the attempt is deferred to a main-thread retry instead.
    /// </summary>
    private static void RegisterKeyBindingsSafe()
    {
        try
        {
            Settings.RegisterKeyBindings();
            CacheActions();
        }
        catch (Exception exception)
        {
            KeyBindingsPending = true;
            Log.Warn($"Key binding registration failed during OnLoad ({exception.GetType().Name}: {exception.Message}); retrying on the main thread");
        }
    }

    /// <summary>Main-thread retry, called from the UI system once until it succeeds or is abandoned.</summary>
    internal static void RetryKeyBindings()
    {
        if (!KeyBindingsPending) return;
        KeyBindingsPending = false;
        try
        {
            Settings.RegisterKeyBindings();
            CacheActions();
            Log.Info("Key binding registration retried on the main thread and succeeded");
        }
        catch (Exception exception)
        {
            Log.Warn($"Key binding registration retry failed ({exception.GetType().Name}: {exception.Message}); the shortcut key stays disabled, the top-left panel button remains available");
        }
    }

    private static void CacheActions()
    {
        ToggleTool = Settings.GetAction(ToggleToolAction);
        Apply = Settings.GetAction(ApplyAction);
        Clear = Settings.GetAction(ClearAction);
        if (ToggleTool != null) ToggleTool.shouldBeEnabled = true;
        if (Apply != null) Apply.shouldBeEnabled = true;
        if (Clear != null) Clear.shouldBeEnabled = true;
    }

    public void OnDispose()
    {
        RestrictionPathfindHook.Uninstall();
        if (m_RuntimeWorld != null && m_RuntimeWorld.IsCreated)
        {
            m_RuntimeWorld.GetExistingSystemManaged<RoadEnforcementCoordinator>()?.ReleaseAll();
            m_RuntimeWorld.GetExistingSystemManaged<RailEnforcementBackend>()?.ReleaseAll();
            m_RuntimeWorld.GetExistingSystemManaged<RoadRestrictionVisualSignsSystem>()?.ClearOwned();
        }
        Interlocked.Exchange(ref s_DiagnosticsRequested, 0);
        Interlocked.Exchange(ref s_NativeProtocolRequested, 0);
        Interlocked.Exchange(ref s_ResetRequested, 0);
        Settings?.UnregisterInOptionsUI();
        Log.Info(nameof(OnDispose));
    }
}
