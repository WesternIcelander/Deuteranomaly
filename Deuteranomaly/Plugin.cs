using System.Reflection;
using HarmonyLib;
using IPA;
using IPALogger = IPA.Logging.Logger;

namespace Deuteranomaly
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal const string HarmonyId = "io.siggi.beatsaber.deuteranomaly";

        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }

        // Defaults until the config (step 7) and settings UI (step 8) exist.
        internal static bool Enabled { get; set; } = true;
        internal static Core.Settings Settings { get; } = new Core.Settings();

        private Harmony _harmony;

        [Init]
        public void Init(IPALogger logger)
        {
            Instance = this;
            Log = logger;
        }

        [OnStart]
        public void OnApplicationStart()
        {
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("Deuteranomaly loaded.");
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
