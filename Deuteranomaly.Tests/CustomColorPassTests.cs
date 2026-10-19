using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Deuteranomaly.Core;
using Xunit;

namespace Deuteranomaly.Tests
{
    public class CustomColorPassTests
    {
        // Stands in for CustomJSONData's CustomData, so cloned dictionaries can be checked for their type.
        class FakeCustomData : ConcurrentDictionary<string, object> { }

        static readonly Settings Deutan = new Settings { Type = CvdType.Deutan, Severity = 1.0, Threshold = 15 };

        // Deutan confusion pair from the core tests (6.4 at severity 1.0), and the default red/blue.
        static readonly Rgb ConfusedLeft = Hex("A01A1A");
        static readonly Rgb ConfusedRight = Hex("00601A");
        static readonly Rgb DefaultLeft = Hex("C81414");
        static readonly Rgb DefaultRight = Hex("288ED2");

        static Rgb Hex(string h)
        {
            return new Rgb(Convert.ToInt32(h.Substring(0, 2), 16) / 255.0,
                           Convert.ToInt32(h.Substring(2, 2), 16) / 255.0,
                           Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
        }

        static List<object> Color(Rgb c, params object[] rest)
        {
            var list = new List<object> { c.R, c.G, c.B };
            list.AddRange(rest);
            return list;
        }

        static Rgb Read(object value)
        {
            var list = (List<object>)value;
            return new Rgb(Convert.ToDouble(list[0]), Convert.ToDouble(list[1]), Convert.ToDouble(list[2]));
        }

        static FakeCustomData Data(params object[] pairs)
        {
            var data = new FakeCustomData();
            for (int i = 0; i < pairs.Length; i += 2)
            {
                data[(string)pairs[i]] = pairs[i + 1];
            }
            return data;
        }

        static PassObject Note(double time, int side, FakeCustomData data)
        {
            return new PassObject { Time = time, Side = side, CustomData = data };
        }

        static double Distance(Rgb a, Rgb b)
        {
            return ColorAssist.SimulatedDistance(a, b, Deutan.Type, Deutan.Severity);
        }

        static void AssertSame(Rgb expected, Rgb actual)
        {
            Assert.Equal(expected.R, actual.R, 9);
            Assert.Equal(expected.G, actual.G, 9);
            Assert.Equal(expected.B, actual.B, 9);
        }

        [Fact]
        public void ResolveKeepsALockedSide()
        {
            Result r = ColorAssist.Resolve(ConfusedLeft, ConfusedRight, Deutan, false, true);
            Assert.True(r.Changed);
            AssertSame(ConfusedRight, r.Right);
            Assert.True(r.FinalDistance >= Deutan.Threshold);

            Result both = ColorAssist.Resolve(ConfusedLeft, ConfusedRight, Deutan, true, true);
            Assert.False(both.Changed);
        }

        [Fact]
        public void StaticConfusedColorsAreRewrittenWithoutTouchingTheOriginals()
        {
            List<object> leftOriginal = Color(ConfusedLeft, 1.0);
            List<object> rightOriginal = Color(ConfusedRight);
            var left = Data("color", leftOriginal);
            var right = Data("color", rightOriginal);

            PassStats stats = CustomColorPass.Run(
                new[] { Note(1, 0, left), Note(1.1, 1, right) }, null, null, false, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(2, stats.Colors);
            Assert.True(stats.Changed >= 1);
            Assert.True(Distance(Read(left["color"]), Read(right["color"])) >= Deutan.Threshold);
            Assert.Equal(1.0, ((List<object>)left["color"])[3]);
            // The lists may be shared with the cached map data, so they must stay as they were.
            AssertSame(ConfusedLeft, Read(leftOriginal));
            AssertSame(ConfusedRight, Read(rightOriginal));
            Assert.NotSame(leftOriginal, left["color"]);
        }

        [Fact]
        public void DistinguishableColorsAreKept()
        {
            var left = Data("color", Color(DefaultLeft));
            var right = Data("color", Color(DefaultRight));

            PassStats stats = CustomColorPass.Run(
                new[] { Note(1, 0, left), Note(1, 1, right) }, null, null, false, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(2, stats.Colors);
            Assert.Equal(0, stats.Changed);
            AssertSame(DefaultLeft, Read(left["color"]));
            AssertSame(DefaultRight, Read(right["color"]));
        }

        [Fact]
        public void AnimatedKeyframesAreCorrectedAgainstAnUncoloredNote()
        {
            var animation = Data("color", new List<object>
            {
                Color(ConfusedLeft, 1.0, 0.0),
                Color(ConfusedLeft, 1.0, 1.0, "easeInOutSine")
            });
            var left = Data("animation", animation);
            // No custom color: shows the scheme color, which the pass can't change.
            var right = Data();

            PassStats stats = CustomColorPass.Run(
                new[] { Note(2, 0, left), Note(2, 1, right) }, null, null, false, DefaultLeft, ConfusedRight, Deutan);

            Assert.Equal(2, stats.Colors);
            Assert.Equal(2, stats.Changed);
            var copy = Assert.IsType<FakeCustomData>(left["animation"]);
            Assert.NotSame(animation, copy);
            var points = (List<object>)copy["color"];
            Assert.True(Distance(Read(points[0]), ConfusedRight) >= Deutan.Threshold);
            Assert.Equal("easeInOutSine", ((List<object>)points[1])[5]);
            AssertSame(ConfusedLeft, Read(((List<object>)animation["color"])[0]));
        }

        [Fact]
        public void TrackColorFromAPointDefinitionIsInlinedAndCorrected()
        {
            List<object> definition = new List<object> { Color(ConfusedLeft, 1.0, 0.0), Color(ConfusedLeft, 1.0, 1.0) };
            var map = Data("pointDefinitions", Data("confused", definition));
            var animate = Data("track", "leftNotes", "duration", 4.0, "color", "confused");
            var left = Data("track", "leftNotes");
            var right = Data("color", Color(ConfusedRight));

            PassStats stats = CustomColorPass.Run(
                new[] { Note(10, 0, left), Note(10, 1, right) },
                new[] { new PassEvent { Time = 9, Type = CustomColorPass.AnimateTrack, CustomData = animate } },
                map, false, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(3, stats.Colors);
            var points = Assert.IsType<List<object>>(animate["color"]);
            Assert.True(Distance(Read(points[0]), Read(right["color"])) >= Deutan.Threshold);
            AssertSame(ConfusedLeft, Read(definition[0]));
        }

        [Fact]
        public void TrackSharedByBothSidesIsLeftAlone()
        {
            var animate = Data("track", "both", "color", Color(ConfusedLeft, 1.0));

            PassStats stats = CustomColorPass.Run(
                new[] { Note(1, 0, Data("track", "both")), Note(1, 1, Data("track", new List<object> { "both", "other" })) },
                new[] { new PassEvent { Time = 0, Type = CustomColorPass.AssignPathAnimation, CustomData = animate } },
                null, false, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(1, stats.SharedTrackEvents);
            Assert.Equal(0, stats.Colors);
            AssertSame(ConfusedLeft, Read(animate["color"]));
        }

        [Fact]
        public void RuntimeProvidersAndModifiersAreSkipped()
        {
            var animation = Data("color", new List<object>
            {
                new List<object> { "baseNote0Color", 0.0 },
                new List<object> { ConfusedLeft.R, ConfusedLeft.G, ConfusedLeft.B, 1.0, 0.5, new List<object> { 0.5, 0.5, 0.5, 1.0, "opMul" } },
                Color(ConfusedLeft, 1.0, 1.0)
            });

            PassStats stats = CustomColorPass.Run(
                new[] { Note(1, 0, Data("animation", animation)), Note(1, 1, Data("color", Color(ConfusedRight))) },
                new[] { new PassEvent { Time = 0, Type = "SetMaterialProperty", CustomData = Data() } },
                null, false, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(1, stats.RuntimePoints);
            Assert.Equal(1, stats.SkippedPoints);
            Assert.Equal(1, stats.VivifyEvents);
            Assert.Equal(2, stats.Colors);
        }

        [Fact]
        public void Version2KeysAndPointDefinitionsAreUsed()
        {
            var map = Data("_pointDefinitions", new List<object>
            {
                Data("_name", "confused", "_points", new List<object> { Color(ConfusedLeft, 1.0, 0.0) })
            });
            var animate = Data("_track", "L", "_color", "confused");
            var right = Data("_color", Color(ConfusedRight));

            PassStats stats = CustomColorPass.Run(
                new[] { Note(5, 0, Data("_track", "L")), Note(5, 1, right) },
                new[] { new PassEvent { Time = 5, Type = CustomColorPass.AnimateTrack, CustomData = animate } },
                map, true, DefaultLeft, DefaultRight, Deutan);

            Assert.Equal(2, stats.Colors);
            var points = Assert.IsType<List<object>>(animate["_color"]);
            Assert.True(Distance(Read(points[0]), Read(right["_color"])) >= Deutan.Threshold);
        }

        [Fact]
        public void BrightColorsKeepTheirGlow()
        {
            // Values above 1 make Chroma notes glow; the correction keeps that brightness.
            var left = Data("color", new List<object> { ConfusedLeft.R * 3, ConfusedLeft.G * 3, ConfusedLeft.B * 3 });
            var right = Data();

            CustomColorPass.Run(
                new[] { Note(1, 0, left), Note(1, 1, right) }, null, null, false, DefaultLeft, ConfusedRight, Deutan);

            Rgb written = Read(left["color"]);
            double max = Math.Max(written.R, Math.Max(written.G, written.B));
            Assert.True(max > 1.5);
            var normalized = new Rgb(written.R / max, written.G / max, written.B / max);
            Assert.True(Distance(normalized, ConfusedRight) >= Deutan.Threshold);
        }

        [Fact]
        public void EachColorPairsWithTheNearestOtherSide()
        {
            var early = Data("color", Color(ConfusedLeft));
            var late = Data("color", Color(ConfusedLeft));

            PassStats stats = CustomColorPass.Run(
                new[]
                {
                    Note(0, 0, early), Note(0.2, 1, Data("color", Color(DefaultRight))),
                    Note(100, 0, late), Note(100.2, 1, Data("color", Color(ConfusedRight)))
                },
                null, null, false, DefaultLeft, DefaultRight, Deutan);

            // Next to a distinguishable blue, the early red stays; next to the green, the late one changes.
            AssertSame(ConfusedLeft, Read(early["color"]));
            Assert.Equal(4, stats.Colors);
            Assert.Equal(2, stats.Pairs);
        }
    }
}
