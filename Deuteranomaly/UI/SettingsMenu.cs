using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;
using UnityEngine;

namespace Deuteranomaly.UI
{
    /// <summary>
    /// Puts a "Deuteranomaly" button in the main menu's left-hand mod panel (BSML's menu buttons),
    /// which opens <see cref="SettingsFlowCoordinator"/>.
    /// </summary>
    internal static class SettingsMenu
    {
        private static readonly MenuButton Button = new MenuButton(
            "Deuteranomaly",
            "Color vision settings for saber and note colors",
            Present);

        private static SettingsFlowCoordinator _flowCoordinator;

        internal static void Init()
        {
            MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;
        }

        internal static void Dispose()
        {
            MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
        }

        // BSML binds a new MenuButtons each time the menu scene is installed (also after an
        // internal restart), so register again every time and drop the old flow coordinator,
        // whose injected references belong to the previous menu.
        private static void OnMainMenuInitializing()
        {
            if (_flowCoordinator != null)
            {
                UnityEngine.Object.Destroy(_flowCoordinator.gameObject);
            }

            _flowCoordinator = null;
            MenuButtons.Instance.RegisterButton(Button);
        }

        private static void Present()
        {
            try
            {
                if (_flowCoordinator == null)
                {
                    _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
                }

                BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Opening the settings failed: {e}");
            }
        }
    }
}
