using Game;
using Game.UI;
using Game.UI.Menu;
using System;

namespace RouteFilter.Systems;

/// <summary>Registers against the live UI world, including the main menu.</summary>
public sealed partial class RouteFilterSettingsUISystem : UISystemBase
{
    private bool m_Registered;
    private bool m_Failed;

    public override GameMode gameMode => GameMode.MainMenu | GameMode.GameOrEditor;

    protected override void OnUpdate()
    {
        if (!m_Registered && !m_Failed) RegisterPage();
        base.OnUpdate();
    }

    protected override void OnDestroy()
    {
        if (m_Registered)
        {
            try { World.GetOrCreateSystemManaged<OptionsUISystem>().UnregisterSettings(Mod.Settings.id); }
            catch (Exception exception) { Mod.Log.Warn($"[RouteFilter.Settings] UI unregister failed: {exception.Message}"); }
        }
        base.OnDestroy();
    }

    public bool RegisterPage()
    {
        try
        {
            // The convenience RegisterInOptionsUI silently returns when the default world
            // is absent. Here the Options system is obtained from this live UI world.
            var options = World.GetOrCreateSystemManaged<OptionsUISystem>();
            var page = Mod.Settings.GetPageData(Mod.Settings.id, true).BuildPage();
            page.UpdateVisibility(false);
            if (page.visibleSections.Count == 0)
                throw new InvalidOperationException("RouteFilter settings generated no visible sections");
            options.RegisterSetting(Mod.Settings, Mod.Settings.id, true);
            m_Registered = true;
            m_Failed = false;
            Mod.Log.Info($"[RouteFilter.Settings] Registered in UI world: id={Mod.Settings.id}, visibleSections={page.visibleSections.Count}. Screen visibility still requires game verification.");
            return true;
        }
        catch (Exception exception)
        {
            m_Failed = true; // No per-frame exception loop; explicit Open retries.
            Mod.Log.Error($"[RouteFilter.Settings] UI-world registration failed: {exception}");
            return false;
        }
    }

    public void OpenSettings()
    {
        if (!RegisterPage()) return;
        World.GetOrCreateSystemManaged<OptionsUISystem>().OpenPage(Mod.Settings.id, Setting.kSection, false);
    }
}
