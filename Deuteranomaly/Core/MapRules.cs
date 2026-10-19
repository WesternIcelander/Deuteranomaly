using System;
using System.Collections.Generic;

namespace Deuteranomaly.Core
{
    public enum MapRule
    {
        /// <summary>Colors change only when they look too similar.</summary>
        Default,

        /// <summary>The override is forced on: colors always move further apart.</summary>
        Pinned,

        /// <summary>The map's colors are always kept.</summary>
        Excluded
    }

    /// <summary>
    /// Per-map pins and exclusions. Entries are <c>HASH/Characteristic/Difficulty</c> (one difficulty)
    /// or just <c>HASH</c> (every difficulty of the level), compared case-insensitively. HASH is the
    /// level ID without the <c>custom_level_</c> prefix, so built-in levels use their level ID.
    /// </summary>
    public static class MapRules
    {
        private const string CustomLevelPrefix = "custom_level_";

        /// <summary>The level part of a key: the custom level hash, or the level ID for built-in levels.</summary>
        public static string LevelPart(string levelId)
        {
            if (levelId == null)
            {
                return "";
            }

            return levelId.StartsWith(CustomLevelPrefix, StringComparison.OrdinalIgnoreCase)
                ? levelId.Substring(CustomLevelPrefix.Length)
                : levelId;
        }

        /// <summary>The key for one difficulty, as written in the config.</summary>
        public static string Key(string levelId, string characteristic, string difficulty)
        {
            return LevelPart(levelId) + "/" + characteristic + "/" + difficulty;
        }

        /// <summary>The rule for a difficulty. An exclusion wins over a pin, so a conflict keeps the map's colors.</summary>
        public static MapRule Find(IEnumerable<string> pins, IEnumerable<string> exclusions, string levelId, string characteristic, string difficulty)
        {
            string level = LevelPart(levelId);
            string key = Key(levelId, characteristic, difficulty);
            if (Matches(exclusions, level, key))
            {
                return MapRule.Excluded;
            }

            return Matches(pins, level, key) ? MapRule.Pinned : MapRule.Default;
        }

        /// <summary>
        /// Settings for a pinned map: the threshold is raised to at least <paramref name="extraDistance"/>
        /// above the original pair's simulated distance, so <see cref="ColorAssist.Resolve(Rgb, Rgb, Settings)"/>
        /// always changes the colors. Returns a copy; <paramref name="settings"/> is not modified.
        /// </summary>
        public static Settings ForPinned(Settings settings, double originalDistance, double extraDistance)
        {
            Settings pinned = settings.Clone();
            pinned.Threshold = Math.Max(settings.Threshold, originalDistance + Math.Max(0.0, extraDistance));
            return pinned;
        }

        private static bool Matches(IEnumerable<string> entries, string level, string key)
        {
            if (entries == null)
            {
                return false;
            }

            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string e = entry.Trim();
                if (string.Equals(e, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e, level, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
