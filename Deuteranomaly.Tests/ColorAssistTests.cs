using System;
using Deuteranomaly.Core;
using Xunit;

namespace Deuteranomaly.Tests
{
    public class ColorAssistTests
    {
        static Rgb Hex(string h)
        {
            return new Rgb(Convert.ToInt32(h.Substring(0, 2), 16) / 255.0,
                           Convert.ToInt32(h.Substring(2, 2), 16) / 255.0,
                           Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
        }

        static double Value(Rgb c) { return Math.Max(c.R, Math.Max(c.G, c.B)); }

        static bool Same(Rgb a, Rgb b)
        {
            return Math.Abs(a.R - b.R) < 1e-9 && Math.Abs(a.G - b.G) < 1e-9 && Math.Abs(a.B - b.B) < 1e-9;
        }

        // ---------- CIEDE2000 (Sharma, Wu & Dalal reference pairs) ----------

        [Theory]
        [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
        [InlineData(50, 3.1571, -77.2803, 50, 0, -82.7485, 2.8615)]
        [InlineData(50, 2.8361, -74.0200, 50, 0, -82.7485, 3.4412)]
        [InlineData(50, -1.3802, -84.2814, 50, 0, -82.7485, 1.0000)]
        [InlineData(50, 2.5, 0, 50, 0, -2.5, 4.3065)]
        [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
        [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
        public void DeltaE2000MatchesReference(double l1, double a1, double b1,
                                               double l2, double a2, double b2, double expected)
        {
            double[] lab1 = { l1, a1, b1 }, lab2 = { l2, a2, b2 };
            Assert.InRange(ColorAssist.DeltaE2000(lab1, lab2), expected - 1e-3, expected + 1e-3);
            Assert.InRange(ColorAssist.DeltaE2000(lab2, lab1), expected - 1e-3, expected + 1e-3);
        }

        // ---------- simulation ----------

        public static TheoryData<CvdType> AllTypes()
        {
            return new TheoryData<CvdType> { CvdType.Protan, CvdType.Deutan, CvdType.Tritan };
        }

        [Theory]
        [MemberData(nameof(AllTypes))]
        public void GrayStaysGray(CvdType type)
        {
            foreach (double v in new[] { 0.0, 0.05, 0.2, 0.5, 0.9, 1.0 })
            {
                foreach (double severity in new[] { 0.3, 0.6, 1.0 })
                {
                    Rgb sim = ColorAssist.Simulate(new Rgb(v, v, v), type, severity);
                    Assert.InRange(sim.R, v - 2e-3, v + 2e-3);
                    Assert.InRange(sim.G, v - 2e-3, v + 2e-3);
                    Assert.InRange(sim.B, v - 2e-3, v + 2e-3);
                }
            }
        }

        [Fact]
        public void TritanNearGrayIsContinuousOnBothHalfPlanes()
        {
            // The separation normal (0.03901, -0.02788, -0.01113) puts reddish colors on plane 1
            // and bluish colors on plane 2. Colors just off gray on either side must stay close
            // to their input, so there is no jump at the boundary.
            double[] n = { 0.03901, -0.02788, -0.01113 };
            foreach (double v in new[] { 0.1, 0.5, 0.9 })
            {
                Rgb reddish = new Rgb(v + 0.02, v, v), bluish = new Rgb(v, v, v + 0.02);
                Assert.True(n[0] * Lin(reddish.R) + n[1] * Lin(reddish.G) + n[2] * Lin(reddish.B) >= 0);
                Assert.True(n[0] * Lin(bluish.R) + n[1] * Lin(bluish.G) + n[2] * Lin(bluish.B) < 0);

                foreach (Rgb near in new[] { reddish, bluish })
                {
                    Rgb sim = ColorAssist.Simulate(near, CvdType.Tritan, 1.0);
                    Assert.InRange(sim.R, near.R - 0.03, near.R + 0.03);
                    Assert.InRange(sim.G, near.G - 0.03, near.G + 0.03);
                    Assert.InRange(sim.B, near.B - 0.03, near.B + 0.03);
                }
            }
        }

        static double Lin(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        [Theory]
        [MemberData(nameof(AllTypes))]
        public void SeverityZeroIsIdentity(CvdType type)
        {
            foreach (string h in new[] { "C81414", "288ED2", "FF0000", "00FF00", "0000FF", "FF8000", "8000FF", "123456" })
            {
                Rgb c = Hex(h);
                Rgb sim = ColorAssist.Simulate(c, type, 0);
                Assert.InRange(sim.R, c.R - 1e-9, c.R + 1e-9);
                Assert.InRange(sim.G, c.G - 1e-9, c.G + 1e-9);
                Assert.InRange(sim.B, c.B - 1e-9, c.B + 1e-9);
            }
        }

        // ---------- Resolve ----------

        [Theory]
        [InlineData(CvdType.Protan, 0.6)]
        [InlineData(CvdType.Protan, 1.0)]
        [InlineData(CvdType.Deutan, 0.6)]
        [InlineData(CvdType.Deutan, 1.0)]
        [InlineData(CvdType.Tritan, 0.6)]
        [InlineData(CvdType.Tritan, 1.0)]
        public void DefaultRedBlueIsKept(CvdType type, double severity)
        {
            Rgb left = Hex("C81414"), right = Hex("288ED2");
            Result r = ColorAssist.Resolve(left, right, new Settings { Type = type, Severity = severity });

            Assert.False(r.Changed);
            Assert.True(r.MeetsThreshold);
            Assert.True(Same(r.Left, left));
            Assert.True(Same(r.Right, right));
            Assert.Equal(r.OriginalDistance, r.FinalDistance);
        }

        // Pairs measured below the default threshold of 15 for their type (CIEDE2000 of the
        // simulated colors at severity 1.0): dark red / dark green 9.9 protan, 6.3 deutan;
        // orange / green 10.1 deutan; blue / blue-green 9.8 tritan; magenta / pink 6.0 tritan.
        [Theory]
        [InlineData(CvdType.Protan, "A01A1A", "00601A")]
        [InlineData(CvdType.Deutan, "A01A1A", "00601A")]
        [InlineData(CvdType.Deutan, "FF8000", "00FF00")]
        [InlineData(CvdType.Tritan, "0060FF", "00A0A0")]
        [InlineData(CvdType.Tritan, "FF00FF", "FF8080")]
        public void ConfusionPairIsChanged(CvdType type, string leftHex, string rightHex)
        {
            Settings s = new Settings { Type = type, Severity = 1.0 };
            Result r = ColorAssist.Resolve(Hex(leftHex), Hex(rightHex), s);

            Assert.True(r.OriginalDistance < s.Threshold);
            Assert.True(r.Changed);
            Assert.True(r.MeetsThreshold);
            Assert.True(r.FinalDistance >= s.Threshold);
            Assert.InRange(ColorAssist.SimulatedDistance(r.Left, r.Right, type, s.Severity),
                           r.FinalDistance - 1e-9, r.FinalDistance + 1e-9);
        }

        [Theory]
        [InlineData(CvdType.Protan, "A01A1A", "00601A")]
        [InlineData(CvdType.Deutan, "FF8000", "00FF00")]
        [InlineData(CvdType.Tritan, "0060FF", "00A0A0")]
        [InlineData(CvdType.Deutan, "401010", "103010")]
        [InlineData(CvdType.Tritan, "101040", "103030")]
        public void ChangedColorsStayAboveMinValue(CvdType type, string leftHex, string rightHex)
        {
            Settings s = new Settings { Type = type, Severity = 1.0 };
            Rgb left = Hex(leftHex), right = Hex(rightHex);
            Result r = ColorAssist.Resolve(left, right, s);

            Assert.True(r.Changed);
            Assert.False(Same(r.Left, left) && Same(r.Right, right));
            // A side the search kept as is may be darker than MinValue; a replaced color may not.
            if (!Same(r.Left, left)) Assert.True(Value(r.Left) >= s.MinValue - 1e-9);
            if (!Same(r.Right, right)) Assert.True(Value(r.Right) >= s.MinValue - 1e-9);
        }
    }
}
