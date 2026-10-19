using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Deuteranomaly.Core;
using HarmonyLib;
using IPA.Loader;

namespace Deuteranomaly
{
    /// <summary>
    /// Optional integration with Chroma: corrects the colors in the map's custom data
    /// (CustomJSONData) before Heck and Chroma parse it. No compile-time reference to either
    /// mod; CustomJSONData's types are found by name, and a missing one turns this off.
    /// </summary>
    internal static class CustomColorIntegration
    {
        private static bool _available;
        private static Type _beatmapDataType;
        private static PropertyInfo _objectsProperty;
        private static PropertyInfo _eventsProperty;
        private static PropertyInfo _mapCustomDataProperty;
        private static PropertyInfo _versionProperty;
        private static PropertyInfo _itemCustomDataProperty;
        private static PropertyInfo _eventTypeProperty;

        // Beatmap data already corrected. A restart reuses the scene setup data and its
        // transformed beatmap data, which must not be corrected twice.
        private static readonly ConditionalWeakTable<object, object> Processed =
            new ConditionalWeakTable<object, object>();

        internal static void Init()
        {
            if (PluginManager.GetPluginFromId("CustomJSONData") == null || PluginManager.GetPluginFromId("Chroma") == null)
            {
                Plugin.Log.Info("Chroma or CustomJSONData is not installed; map custom colors are not checked.");
                return;
            }

            try
            {
                _beatmapDataType = Find("CustomJSONData.CustomBeatmap.CustomBeatmapData");
                Type itemInterface = Find("CustomJSONData.CustomBeatmap.ICustomData");
                Type eventType = Find("CustomJSONData.CustomBeatmap.CustomEventData");
                _objectsProperty = Property(_beatmapDataType, "beatmapObjectDatas");
                _eventsProperty = Property(_beatmapDataType, "customEventDatas");
                _mapCustomDataProperty = Property(_beatmapDataType, "customData");
                _versionProperty = Property(_beatmapDataType, "version");
                _itemCustomDataProperty = Property(itemInterface, "customData");
                _eventTypeProperty = Property(eventType, "eventType");
                _available = true;
                Plugin.Log.Info("Chroma found; map custom colors are checked too.");
            }
            catch (Exception e)
            {
                Plugin.Log.Warn($"CustomJSONData has changed, map custom colors are not checked: {e.Message}");
            }
        }

        internal static void Apply(GameplayCoreSceneSetupData setupData, string level, Settings settings)
        {
            if (!_available || setupData.colorScheme == null)
            {
                return;
            }

            object beatmapData = setupData.transformedBeatmapData;
            if (beatmapData == null || !_beatmapDataType.IsInstanceOfType(beatmapData) || Processed.TryGetValue(beatmapData, out _))
            {
                return;
            }
            Processed.Add(beatmapData, null);

            var stopwatch = Stopwatch.StartNew();
            var version = _versionProperty.GetValue(beatmapData) as Version;
            bool v2 = version != null && version < new Version(3, 0, 0);

            var objects = new List<PassObject>();
            foreach (object item in (IEnumerable)_objectsProperty.GetValue(beatmapData))
            {
                ColorType colorType;
                if (item is NoteData note)
                {
                    colorType = note.colorType;
                }
                else if (item is SliderData slider)
                {
                    colorType = slider.colorType;
                }
                else
                {
                    continue;
                }

                if (colorType == ColorType.ColorA || colorType == ColorType.ColorB)
                {
                    objects.Add(new PassObject
                    {
                        Time = ((BeatmapDataItem)item).time,
                        Side = (int)colorType,
                        CustomData = _itemCustomDataProperty.GetValue(item) as IDictionary<string, object>
                    });
                }
            }

            var events = new List<PassEvent>();
            foreach (object item in (IEnumerable)_eventsProperty.GetValue(beatmapData))
            {
                events.Add(new PassEvent
                {
                    Time = ((BeatmapDataItem)item).time,
                    Type = _eventTypeProperty.GetValue(item) as string,
                    CustomData = _itemCustomDataProperty.GetValue(item) as IDictionary<string, object>
                });
            }

            ColorScheme scheme = setupData.colorScheme;
            PassStats stats = CustomColorPass.Run(
                objects,
                events,
                _mapCustomDataProperty.GetValue(beatmapData) as IDictionary<string, object>,
                v2,
                ColorSchemeCorrector.ToRgb(scheme.saberAColor),
                ColorSchemeCorrector.ToRgb(scheme.saberBColor),
                settings);
            stopwatch.Stop();

            Log(level, stats, stopwatch.ElapsedMilliseconds);
        }

        private static void Log(string level, PassStats stats, long milliseconds)
        {
            if (stats.Colors > 0)
            {
                Plugin.Log.Info(
                    $"{level}: custom colors: {stats.Colors} checked, {stats.Changed} changed, {stats.Pairs} distinct pairs" +
                    (stats.PairsBelowThreshold > 0 ? $" ({stats.PairsBelowThreshold} below threshold, best available)" : "") +
                    $" ({milliseconds} ms)");
            }

            var skipped = new List<string>();
            if (stats.RuntimePoints > 0) skipped.Add($"{stats.RuntimePoints} keyframes with base… runtime values");
            if (stats.SkippedPoints > 0) skipped.Add($"{stats.SkippedPoints} keyframes with modifiers");
            if (stats.SharedTrackEvents > 0) skipped.Add($"{stats.SharedTrackEvents} track color events on tracks with both sides or no notes");
            if (stats.EventDefinitions > 0) skipped.Add($"{stats.EventDefinitions} eventDefinitions with track colors");
            if (stats.MissingPointDefinitions > 0) skipped.Add($"{stats.MissingPointDefinitions} missing point definitions");
            if (stats.VivifyEvents > 0) skipped.Add($"{stats.VivifyEvents} Vivify prefab/material events");
            if (skipped.Count > 0)
            {
                Plugin.Log.Info($"{level}: custom colors left alone: {string.Join(", ", skipped)}");
            }
        }

        private static Type Find(string name)
        {
            return AccessTools.TypeByName(name) ?? throw new MissingMemberException($"type {name} not found");
        }

        private static PropertyInfo Property(Type type, string name)
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(type.FullName, name);
        }
    }
}
