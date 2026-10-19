using System;
using System.Collections.Generic;

// Pure C#, no Unity, Beat Saber or CustomJSONData references, so it can be unit-tested outside
// the game. Custom data is handled as plain dictionaries and lists, which is what CustomJSONData's
// CustomData (a ConcurrentDictionary<string, object>) and its List<object> values are.
// Targets .NET Framework 4.7.2 (no tuples).
namespace Deuteranomaly.Core
{
    /// <summary>A note, chain link or arc with a saber color.</summary>
    public sealed class PassObject
    {
        public double Time;
        /// <summary>0 = left (ColorA), 1 = right (ColorB), after left-handed mirroring.</summary>
        public int Side;
        /// <summary>The object's own custom data. Top-level entries may be replaced; nested values are shared and never modified.</summary>
        public IDictionary<string, object> CustomData;
    }

    /// <summary>A custom event (AnimateTrack, AssignPathAnimation, ...).</summary>
    public sealed class PassEvent
    {
        public double Time;
        public string Type;
        /// <summary>The event's own custom data, handled like <see cref="PassObject.CustomData"/>.</summary>
        public IDictionary<string, object> CustomData;
    }

    public sealed class PassStats
    {
        /// <summary>Colors and keyframes that were checked.</summary>
        public int Colors;
        /// <summary>Of those, how many were rewritten.</summary>
        public int Changed;
        /// <summary>Distinct color pairs resolved.</summary>
        public int Pairs;
        /// <summary>Pairs changed without reaching the threshold (best available).</summary>
        public int PairsBelowThreshold;
        /// <summary>Keyframes left alone because they use modifiers or aren't plain numbers.</summary>
        public int SkippedPoints;
        /// <summary>Keyframes left alone because they use <c>base…</c> providers (runtime values).</summary>
        public int RuntimePoints;
        /// <summary>Track color events left alone because their tracks hold both sides, or no notes.</summary>
        public int SharedTrackEvents;
        /// <summary>v3 <c>eventDefinitions</c> with track colors, which are left alone.</summary>
        public int EventDefinitions;
        /// <summary>Color references to point definitions that don't exist.</summary>
        public int MissingPointDefinitions;
        /// <summary>Vivify events that may color notes or sabers in ways the data doesn't show.</summary>
        public int VivifyEvents;
    }

    /// <summary>
    /// Corrects the Chroma colors in a map's custom data once, at level start: static object
    /// colors, animated object colors, and AnimateTrack / AssignPathAnimation track colors.
    /// Each color is paired with the other side's color nearest in time and the pair is resolved
    /// with <see cref="ColorAssist.Resolve(Rgb, Rgb, Settings, bool, bool)"/>.
    /// </summary>
    public static class CustomColorPass
    {
        public static PassStats Run(IList<PassObject> objects, IList<PassEvent> events,
            IDictionary<string, object> mapCustomData, bool v2, Rgb schemeLeft, Rgb schemeRight, Settings settings)
        {
            var runner = new Runner(v2, settings, schemeLeft, schemeRight, mapCustomData);
            runner.Collect(objects ?? new PassObject[0], events ?? new PassEvent[0]);
            runner.ResolveAll();
            return runner.Stats;
        }

        public const string AnimateTrack = "AnimateTrack";
        public const string AssignPathAnimation = "AssignPathAnimation";

        static readonly string[] VivifyEventTypes = { "AssignObjectPrefab", "SetMaterialProperty", "SetGlobalProperty" };

        /// <summary>One color value: a static color or a keyframe, in a list the pass owns.</summary>
        sealed class Slot
        {
            public double Time;
            public int Side;
            public Rgb Color;      // normalized to 0..1
            public double Scale;   // the original's brightest channel when above 1 (HDR glow), else 1
            public bool Fixed;     // a scheme-colored note: can't be rewritten, only paired against
            public List<object> Target;
        }

        sealed class CacheEntry
        {
            public Rgb InLeft, InRight;
            public Result Result;
        }

        sealed class Runner
        {
            public readonly PassStats Stats = new PassStats();

            readonly bool _v2;
            readonly Settings _settings;
            readonly Rgb[] _scheme;
            readonly string _colorKey, _animationKey, _trackKey;
            readonly Dictionary<string, List<object>> _pointDefinitions = new Dictionary<string, List<object>>();
            readonly Dictionary<string, int> _trackSides = new Dictionary<string, int>();
            readonly HashSet<string> _coloredTracks = new HashSet<string>();
            readonly List<Slot> _slots = new List<Slot>();

            public Runner(bool v2, Settings settings, Rgb schemeLeft, Rgb schemeRight, IDictionary<string, object> mapCustomData)
            {
                _v2 = v2;
                _settings = settings;
                _scheme = new[] { schemeLeft, schemeRight };
                _colorKey = v2 ? "_color" : "color";
                _animationKey = v2 ? "_animation" : "animation";
                _trackKey = v2 ? "_track" : "track";
                ReadMapData(mapCustomData);
            }

            void ReadMapData(IDictionary<string, object> map)
            {
                if (map == null)
                {
                    return;
                }

                if (_v2)
                {
                    var list = Get(map, "_pointDefinitions") as List<object>;
                    if (list == null)
                    {
                        return;
                    }
                    foreach (object item in list)
                    {
                        var def = item as IDictionary<string, object>;
                        var name = def == null ? null : Get(def, "_name") as string;
                        var points = def == null ? null : Get(def, "_points") as List<object>;
                        if (name != null && points != null && !_pointDefinitions.ContainsKey(name))
                        {
                            _pointDefinitions.Add(name, points);
                        }
                    }
                    return;
                }

                var defs = Get(map, "pointDefinitions") as IDictionary<string, object>;
                if (defs != null)
                {
                    foreach (KeyValuePair<string, object> kv in defs)
                    {
                        var points = kv.Value as List<object>;
                        if (points != null && !_pointDefinitions.ContainsKey(kv.Key))
                        {
                            _pointDefinitions.Add(kv.Key, points);
                        }
                    }
                }

                // Events defined here run through InvokeEvent, with no time of their own to pair by.
                var eventDefinitions = Get(map, "eventDefinitions") as List<object>;
                if (eventDefinitions != null)
                {
                    foreach (object item in eventDefinitions)
                    {
                        var def = item as IDictionary<string, object>;
                        var type = def == null ? null : Get(def, "type") as string;
                        var data = def == null ? null : Get(def, "data") as IDictionary<string, object>;
                        if (IsTrackColorEvent(type) && data != null && Get(data, _colorKey) != null)
                        {
                            Stats.EventDefinitions++;
                        }
                    }
                }
            }

            public void Collect(IList<PassObject> objects, IList<PassEvent> events)
            {
                // Tracks whose color is set by events: their notes don't show the scheme color.
                foreach (PassEvent e in events)
                {
                    if (e != null && IsTrackColorEvent(e.Type) && e.CustomData != null && Get(e.CustomData, _colorKey) != null)
                    {
                        foreach (string track in ReadTracks(Get(e.CustomData, _trackKey)))
                        {
                            _coloredTracks.Add(track);
                        }
                    }
                }

                foreach (PassObject o in objects)
                {
                    if (o != null && (o.Side == 0 || o.Side == 1))
                    {
                        CollectObject(o);
                    }
                }

                foreach (PassEvent e in events)
                {
                    if (e != null)
                    {
                        CollectEvent(e);
                    }
                }
            }

            void CollectObject(PassObject o)
            {
                IDictionary<string, object> data = o.CustomData;
                bool colored = false;
                bool onColoredTrack = false;

                if (data != null)
                {
                    foreach (string track in ReadTracks(Get(data, _trackKey)))
                    {
                        int mask;
                        _trackSides.TryGetValue(track, out mask);
                        _trackSides[track] = mask | (1 << o.Side);
                        onColoredTrack |= _coloredTracks.Contains(track);
                    }

                    if (Get(data, _colorKey) is List<object>)
                    {
                        colored |= RewriteValue(data, _colorKey, false, o.Time, o.Side);
                    }

                    var animation = Get(data, _animationKey) as IDictionary<string, object>;
                    if (animation != null && Get(animation, _colorKey) != null)
                    {
                        // The animation dictionary is shared with the cached map data: copy it first.
                        IDictionary<string, object> copy = CloneDictionary(animation);
                        data[_animationKey] = copy;
                        colored |= RewriteValue(copy, _colorKey, true, o.Time, o.Side);
                    }
                }

                if (!colored && !onColoredTrack)
                {
                    _slots.Add(new Slot { Time = o.Time, Side = o.Side, Color = _scheme[o.Side], Scale = 1, Fixed = true });
                }
            }

            void CollectEvent(PassEvent e)
            {
                if (Array.IndexOf(VivifyEventTypes, e.Type) >= 0)
                {
                    Stats.VivifyEvents++;
                    return;
                }

                if (!IsTrackColorEvent(e.Type) || e.CustomData == null || Get(e.CustomData, _colorKey) == null)
                {
                    return;
                }

                int mask = 0;
                foreach (string track in ReadTracks(Get(e.CustomData, _trackKey)))
                {
                    int sides;
                    _trackSides.TryGetValue(track, out sides);
                    mask |= sides;
                }

                if (mask != 1 && mask != 2)
                {
                    Stats.SharedTrackEvents++;
                    return;
                }

                RewriteValue(e.CustomData, _colorKey, true, e.Time, mask == 1 ? 0 : 1);
            }

            /// <summary>
            /// Replaces <c>owner[key]</c> with a copy the pass owns (inlining a named point
            /// definition) and adds a slot for every plain color in it. Returns whether the
            /// value is a color at all, so the caller knows the object isn't scheme-colored.
            /// </summary>
            bool RewriteValue(IDictionary<string, object> owner, string key, bool allowPoints, double time, int side)
            {
                object value = Get(owner, key);
                var source = value as List<object>;
                var name = value as string;
                if (allowPoints && name != null)
                {
                    if (!_pointDefinitions.TryGetValue(name, out source))
                    {
                        Stats.MissingPointDefinitions++;
                        return true;
                    }
                }

                if (source == null || source.Count == 0)
                {
                    return false;
                }

                if (allowPoints && source[0] is List<object>)
                {
                    var points = new List<object>(source.Count);
                    foreach (object item in source)
                    {
                        var point = item as List<object>;
                        if (point == null)
                        {
                            points.Add(item);
                            continue;
                        }
                        var copy = new List<object>(point);
                        AddSlot(copy, time, side);
                        points.Add(copy);
                    }
                    owner[key] = points;
                    return true;
                }

                var single = new List<object>(source);
                AddSlot(single, time, side);
                owner[key] = single;
                return true;
            }

            void AddSlot(List<object> point, double time, int side)
            {
                foreach (object item in point)
                {
                    if (item is List<object>)
                    {
                        Stats.SkippedPoints++;
                        return;
                    }
                    var text = item as string;
                    if (text != null && text.StartsWith("base", StringComparison.Ordinal))
                    {
                        Stats.RuntimePoints++;
                        return;
                    }
                }

                if (point.Count < 3 || !IsNumber(point[0]) || !IsNumber(point[1]) || !IsNumber(point[2]))
                {
                    Stats.SkippedPoints++;
                    return;
                }

                double r = Math.Max(0, Convert.ToDouble(point[0]));
                double g = Math.Max(0, Convert.ToDouble(point[1]));
                double b = Math.Max(0, Convert.ToDouble(point[2]));
                double scale = Math.Max(1, Math.Max(r, Math.Max(g, b)));
                _slots.Add(new Slot
                {
                    Time = time,
                    Side = side,
                    Color = new Rgb(r / scale, g / scale, b / scale),
                    Scale = scale,
                    Target = point
                });
            }

            public void ResolveAll()
            {
                var bySide = new[] { new List<Slot>(), new List<Slot>() };
                foreach (Slot slot in _slots)
                {
                    bySide[slot.Side].Add(slot);
                }
                var times = new double[2][];
                for (int side = 0; side < 2; side++)
                {
                    bySide[side].Sort((x, y) => x.Time.CompareTo(y.Time));
                    times[side] = new double[bySide[side].Count];
                    for (int i = 0; i < times[side].Length; i++)
                    {
                        times[side][i] = bySide[side][i].Time;
                    }
                }

                var cache = new Dictionary<long, CacheEntry>();
                foreach (Slot slot in _slots)
                {
                    if (slot.Fixed)
                    {
                        continue;
                    }
                    Stats.Colors++;

                    int other = 1 - slot.Side;
                    Slot partner = Nearest(bySide[other], times[other], slot.Time);
                    Rgb partnerColor = partner == null ? _scheme[other] : partner.Color;
                    bool partnerFixed = partner == null || partner.Fixed;

                    Rgb left = slot.Side == 0 ? slot.Color : partnerColor;
                    Rgb right = slot.Side == 0 ? partnerColor : slot.Color;
                    bool keepLeft = slot.Side == 1 && partnerFixed;
                    bool keepRight = slot.Side == 0 && partnerFixed;

                    // Near-identical colors share an entry: one Resolve compares about 12,000 pairs.
                    long key = Quantize(left) | (Quantize(right) << 24) | ((keepLeft ? 1L : 0L) << 48) | ((keepRight ? 1L : 0L) << 49);
                    CacheEntry entry;
                    if (!cache.TryGetValue(key, out entry))
                    {
                        entry = new CacheEntry
                        {
                            InLeft = left,
                            InRight = right,
                            Result = ColorAssist.Resolve(left, right, _settings, keepLeft, keepRight)
                        };
                        cache.Add(key, entry);
                        Stats.Pairs++;
                        if (entry.Result.Changed && !entry.Result.MeetsThreshold)
                        {
                            Stats.PairsBelowThreshold++;
                        }
                    }

                    if (!entry.Result.Changed)
                    {
                        continue;
                    }
                    Rgb input = slot.Side == 0 ? entry.InLeft : entry.InRight;
                    Rgb output = slot.Side == 0 ? entry.Result.Left : entry.Result.Right;
                    if (input.R == output.R && input.G == output.G && input.B == output.B)
                    {
                        continue;
                    }

                    slot.Target[0] = output.R * slot.Scale;
                    slot.Target[1] = output.G * slot.Scale;
                    slot.Target[2] = output.B * slot.Scale;
                    Stats.Changed++;
                }
            }

            static Slot Nearest(List<Slot> slots, double[] times, double time)
            {
                if (slots.Count == 0)
                {
                    return null;
                }
                int i = Array.BinarySearch(times, time);
                if (i >= 0)
                {
                    // Equal times may repeat; any of them is equally near.
                    return slots[i];
                }
                i = ~i;
                if (i == 0)
                {
                    return slots[0];
                }
                if (i == slots.Count)
                {
                    return slots[i - 1];
                }
                return time - times[i - 1] <= times[i] - time ? slots[i - 1] : slots[i];
            }

            static IEnumerable<string> ReadTracks(object value)
            {
                var name = value as string;
                if (name != null)
                {
                    yield return name;
                    yield break;
                }
                var list = value as List<object>;
                if (list == null)
                {
                    yield break;
                }
                foreach (object item in list)
                {
                    var track = item as string;
                    if (track != null)
                    {
                        yield return track;
                    }
                }
            }
        }

        static bool IsTrackColorEvent(string type)
        {
            return type == AnimateTrack || type == AssignPathAnimation;
        }

        static object Get(IDictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value : null;
        }

        static bool IsNumber(object value)
        {
            return value is double || value is float || value is int || value is long
                || value is decimal || value is short || value is byte || value is uint || value is ulong;
        }

        static long Quantize(Rgb c)
        {
            return ToByte(c.R) | (ToByte(c.G) << 8) | (ToByte(c.B) << 16);
        }

        static long ToByte(double v)
        {
            return (long)Math.Round(Math.Min(1, Math.Max(0, v)) * 255);
        }

        /// <summary>Copies a dictionary into a new instance of the same type (CustomData stays CustomData).</summary>
        static IDictionary<string, object> CloneDictionary(IDictionary<string, object> source)
        {
            var copy = Activator.CreateInstance(source.GetType()) as IDictionary<string, object>
                ?? new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in source)
            {
                copy[kv.Key] = kv.Value;
            }
            return copy;
        }
    }
}
