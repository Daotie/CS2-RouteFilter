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
using Unity.Entities;

namespace RouteFilter;

public sealed class Mod : IMod
{
    public const string Id = "RouteFilter";
    public const string Version = "1.0.5";
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

    /// <summary>
    /// True while key binding registration is waiting for a main-thread retry. Game 1.6.0f1
    /// can run mod OnLoad on a thread-pool continuation where the Input System's Temp
    /// allocator fails inside AddActionMap (ArgumentNullException: destination); on the main
    /// thread the same registration succeeds.
    /// </summary>
    public static bool KeyBindingsPending { get; private set; }

    public void OnLoad(UpdateSystem updateSystem)
    {
        Log.Info(nameof(OnLoad));

        Settings = new Setting(this);
        Settings.RegisterInOptionsUI();
        AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));
        RegisterKeyBindingsSafe();

        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
        GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleZH(Settings));
        GameManager.instance.localizationManager.AddSource("zh-CN", new LocaleZH(Settings));

        updateSystem.UpdateAt<RestrictionShortcutSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAfter<RestrictionOverlaySystem, RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.Serialize);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.Deserialize);
        updateSystem.UpdateAt<RestrictionPersistenceSystem>(SystemUpdatePhase.ModificationEnd);
        updateSystem.UpdateBefore<RestrictionIndexSystem, VehicleAccessSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAt<RouteFilterUISystem>(SystemUpdatePhase.UIUpdate);
        updateSystem.UpdateAfter<RestrictionPathSystem, Game.Pathfind.LanesModifiedSystem>(SystemUpdatePhase.ModificationEnd);
        updateSystem.UpdateAfter<VehicleAccessSystem, Game.Simulation.CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<VehicleAccessSystem, Game.Simulation.TrainNavigationSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateBefore<VehicleAccessSystem, Game.Simulation.CarMoveSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateBefore<VehicleAccessSystem, Game.Simulation.TrainMoveSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<VehicleDetourSystem, VehicleAccessSystem>(SystemUpdatePhase.GameSimulation);
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
        Settings?.UnregisterInOptionsUI();
        Log.Info(nameof(OnDispose));
    }
}
