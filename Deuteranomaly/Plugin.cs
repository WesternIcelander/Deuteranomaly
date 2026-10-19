using IPA;
using IPALogger = IPA.Logging.Logger;

namespace Deuteranomaly
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }

        [Init]
        public void Init(IPALogger logger)
        {
            Instance = this;
            Log = logger;
        }

        [OnStart]
        public void OnApplicationStart()
        {
            Log.Info("Deuteranomaly loaded.");
        }

        [OnExit]
        public void OnApplicationQuit()
        {
        }
    }
}
