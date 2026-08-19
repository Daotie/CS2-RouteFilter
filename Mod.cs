using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using RouteFilter.Components;
using RouteFilter.Systems;
using System.Collections.Generic;
using Unity.Entities;

namespace RouteFilter;

public sealed class Mod : IMod
{
    public const string Id = "RouteFilter";
    public const string Version = "1.0.0";
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

    public void OnLoad(UpdateSystem updateSystem)
    {
        Log.Info(nameof(OnLoad));

        Settings = new Setting(this);
        Settings.RegisterInOptionsUI();
        Settings.RegisterKeyBindings();
        AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));

        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
        GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleZH(Settings));
        GameManager.instance.localizationManager.AddSource("zh-CN", new LocaleZH(Settings));

        ToggleTool = Settings.GetAction(ToggleToolAction);
        Apply = Settings.GetAction(ApplyAction);
        Clear = Settings.GetAction(ClearAction);
        ToggleTool.shouldBeEnabled = true;
        Apply.shouldBeEnabled = true;
        Clear.shouldBeEnabled = true;

        updateSystem.UpdateAt<RestrictionShortcutSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAfter<RestrictionOverlaySystem, RestrictionToolSystem>(SystemUpdatePhase.ToolUpdate);
        updateSystem.UpdateAt<RouteFilterUISystem>(SystemUpdatePhase.UIUpdate);
        updateSystem.UpdateAfter<RestrictionPathSystem, Game.Pathfind.LanesModifiedSystem>(SystemUpdatePhase.ModificationEnd);
        updateSystem.UpdateAfter<VehicleAccessSystem, Game.Simulation.CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<VehicleAccessSystem, Game.Simulation.TrainNavigationSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateBefore<VehicleAccessSystem, Game.Simulation.CarMoveSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateBefore<VehicleAccessSystem, Game.Simulation.TrainMoveSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAfter<VehicleDetourSystem, VehicleAccessSystem>(SystemUpdatePhase.GameSimulation);
    }

    public void OnDispose()
    {
        Settings?.UnregisterInOptionsUI();
        Log.Info(nameof(OnDispose));
    }
}
