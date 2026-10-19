using BeatSaberMarkupLanguage;
using HMUI;

namespace Deuteranomaly.UI
{
    /// <summary>The settings screen: one <see cref="SettingsViewController"/> with a back button.</summary>
    internal class SettingsFlowCoordinator : FlowCoordinator
    {
        private SettingsViewController _settingsView;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Deuteranomaly");
                showBackButton = true;
                _settingsView = BeatSaberUI.CreateViewController<SettingsViewController>();
            }

            if (addedToHierarchy)
            {
                ProvideInitialViewControllers(_settingsView);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}
