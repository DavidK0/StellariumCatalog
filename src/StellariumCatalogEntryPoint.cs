using Brutal.ImGuiApi;
using HarmonyLib;
using KSA;
using ModMenu;
using StarMap.API;

namespace StellariumCatalog;

[StarMapMod]
public class StellariumCatalogEntryPoint {
    private static Harmony? _harmony;

    [StarMapAllModsLoaded]
    public static void OnFullyLoaded() {
        _harmony = new Harmony("dejvid.stellariumcatalog");

        StellariumCatalogSettingsStore.Init();
        StellariumCatalogSettingsStore.Load();
        SaveLoadObserver.ApplyPatches(_harmony);

        StellariumRenderer.Init();
    }

    [ModMenuEntry("StellariumCat...")]
    public static void DrawMenu() {
        ImGui.Checkbox("IAU Constellations", ref StellariumRenderer.showIAUConstellations);

        ImGui.Separator();

        ImGui.Checkbox("Show Asterisms", ref StellariumRenderer.showAsterisms);
        ImGui.Checkbox("Show Names", ref StellariumRenderer.showAsterismNames);

        if(ImGui.BeginMenu("Sky Cultures")) {

            for(int i = 0; i < SkyCulturesRenderer.SkyCultures.Count; i++) {
                SkyCulture skyCulture = SkyCulturesRenderer.SkyCultures[i];

                bool selected = i == SkyCulturesRenderer.ActiveSkyCultureIndex;

                if(ImGui.MenuItem(
                    selected ? $"✓ {skyCulture.Name}" : skyCulture.Name)) {
                    SkyCulturesRenderer.ActiveSkyCultureIndex = i;

                }
            }

            ImGui.EndMenu();
        }
    }

    [StarMapAfterGui]
    public static void OnAfterUi(double dt) {
        StellariumRenderer.Draw();
    }
}