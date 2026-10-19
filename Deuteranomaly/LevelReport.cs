using UnityEngine;

namespace Deuteranomaly
{
    /// <summary>What happened to the saber colors of the last level started, for the settings screen.</summary>
    internal sealed class LevelReport
    {
        /// <summary>The last level started, or null before the first one. Set on the main thread.</summary>
        internal static LevelReport Last { get; set; }

        internal string Level;
        internal string SongName;
        internal Color OriginalLeft;
        internal Color OriginalRight;
        internal Color FinalLeft;
        internal Color FinalRight;
        internal double OriginalDistance;
        internal double FinalDistance;
        internal bool Changed;
        internal bool MeetsThreshold = true;

        /// <summary>Why the colors were kept regardless of distance (for example an exclusion), or null.</summary>
        internal string Note;

        /// <summary>Summary of the Chroma custom color pass, or null when it didn't run or found no colors.</summary>
        internal string CustomColors;
    }
}
