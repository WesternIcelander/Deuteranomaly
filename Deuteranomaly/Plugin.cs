using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config.Stores;
using IPAConfig = IPA.Config.Config;
using IPALogger = IPA.Logging.Logger;

namespace Deuteranomaly
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal const string HarmonyId = "io.siggi.beatsaber.deuteranomaly";

        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }
        internal static PluginConfig Config { get; private set; }

        private Harmony _harmony;

        [Init]
        public void Init(IPALogger logger, IPAConfig config)
        {
            Instance = this;
            Log = logger;
            Config = config.Generated<PluginConfig>();
        }

        [OnStart]
        public void OnApplicationStart()
        {
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            CustomColorIntegration.Init();
            UI.SettingsMenu.Init();
            Log.Info(
                $"Deuteranomaly loaded ({(Config.Enabled ? "enabled" : "disabled")}, {Config.Type}, severity {Config.Severity:F2}, threshold {Config.Threshold:F1}, " +
                $"{Config.PinnedMaps.Count} pinned, {Config.ExcludedMaps.Count} excluded).");
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            UI.SettingsMenu.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}
