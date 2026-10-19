using System;
using System.Collections.Generic;
using System.Linq;

// Pure C#, no Unity or Beat Saber references, so it can be unit-tested outside the game.
// Targets .NET Framework 4.7.2 (no Math.Cbrt, no tuples).
namespace Deuteranomaly.Core
{
    /// <summary>Color vision deficiency type. Severity picks between "-anomaly" (below 1) and "-opia" (1).</summary>
    public enum CvdType { Protan, Deutan, Tritan }

    /// <summary>sRGB color, each channel 0..1. Convert to/from UnityEngine.Color at the edges.</summary>
    public struct Rgb
    {
        public double R, G, B;
        public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
    }

    public class Settings
    {
        /// <summary>Which deficiency to simulate.</summary>
        public CvdType Type = CvdType.Deutan;

        /// <summary>Severity: 0 = normal vision, 1 = full protanopia, deuteranopia or tritanopia. Tune by eye.</summary>
        public double Severity = 0.6;

        /// <summary>Minimum CIEDE2000 distance between the two simulated colors. Tune by eye.</summary>
        public double Threshold = 15.0;

        /// <summary>Hue search step in degrees.</summary>
        public double HueStepDeg = 10.0;

        /// <summary>Brightness (HSV value) multipliers tried for each color.</summary>
        public double[] ValueFactors = { 1.0, 0.8, 1.2 };

        /// <summary>Lowest HSV value an overridden color may have, so blocks stay visible.</summary>
        public double MinValue = 0.35;

        /// <summary>A copy that can be changed without affecting this one. <see cref="ValueFactors"/> is shared, as it is never modified.</summary>
        public Settings Clone() => (Settings)MemberwiseClone();
    }

    public class Result
    {
        public Rgb Left, Right;
        public bool Changed;
        public bool MeetsThreshold;      // false only if no candidate pair reached the threshold
        public double OriginalDistance;  // simulated distance of the input pair
        public double FinalDistance;     // simulated distance of the returned pair
    }

    public static class ColorAssist
    {
        // ---------- public entry point ----------

        public static Result Resolve(Rgb left, Rgb right, Settings s)
        {
            return Resolve(left, right, s, false, false);
        }

        /// <summary>
        /// Like <see cref="Resolve(Rgb, Rgb, Settings)"/>, but a side marked as kept is never
        /// changed (for example a color the caller can't rewrite). With both kept, only the
        /// distance is reported.
        /// </summary>
        public static Result Resolve(Rgb left, Rgb right, Settings s, bool keepLeft, bool keepRight)
        {
            double origDist = SimulatedDistance(left, right, s.Type, s.Severity);
            if (origDist >= s.Threshold)
            {
                return new Result
                {
                    Left = left, Right = right, Changed = false, MeetsThreshold = true,
                    OriginalDistance = origDist, FinalDistance = origDist
                };
            }

            if (keepLeft && keepRight)
            {
                return new Result
                {
                    Left = left, Right = right, Changed = false, MeetsThreshold = false,
                    OriginalDistance = origDist, FinalDistance = origDist
                };
            }

            List<Candidate> lc = keepLeft ? OriginalOnly(left, s) : BuildCandidates(left, s);
            List<Candidate> rc = keepRight ? OriginalOnly(right, s) : BuildCandidates(right, s);

            double bestCost = double.MaxValue;
            Candidate bestL = null, bestR = null;
            double bestDist = 0;

            // Cheapest pairs first: once a pair's cost can't beat the best passing pair, stop.
            // The level-start pass resolves many pairs, so this matters more than it looks.
            List<Candidate> ls = lc.OrderBy(c => c.Cost).ToList();
            List<Candidate> rs = rc.OrderBy(c => c.Cost).ToList();
            for (int i = 0; i < ls.Count; i++)
            {
                if (ls[i].Cost + rs[0].Cost >= bestCost) break;
                for (int j = 0; j < rs.Count; j++)
                {
                    double cost = ls[i].Cost + rs[j].Cost;
                    if (cost >= bestCost) break;
                    double d = DeltaE2000(ls[i].SimLab, rs[j].SimLab);
                    if (d < s.Threshold) continue;
                    bestCost = cost; bestL = ls[i]; bestR = rs[j]; bestDist = d;
                }
            }

            bool met = bestL != null;
            if (!met)
            {
                // Nothing passes: fall back to the most distinguishable pair.
                double fallbackDist = -1;
                for (int i = 0; i < lc.Count; i++)
                {
                    for (int j = 0; j < rc.Count; j++)
                    {
                        double d = DeltaE2000(lc[i].SimLab, rc[j].SimLab);
                        if (d > fallbackDist) { fallbackDist = d; bestL = lc[i]; bestR = rc[j]; }
                    }
                }
                bestDist = fallbackDist;
            }

            return new Result
            {
                Left = bestL.Color, Right = bestR.Color,
                // The best pair can be the unchanged inputs (always candidates) when nothing reaches the threshold.
                Changed = !SameColor(bestL.Color, left) || !SameColor(bestR.Color, right),
                MeetsThreshold = met,
                OriginalDistance = origDist, FinalDistance = bestDist
            };
        }

        public static double SimulatedDistance(Rgb a, Rgb b, CvdType type, double severity)
        {
            return DeltaE2000(ToLab(Simulate(a, type, severity)),
                              ToLab(Simulate(b, type, severity)));
        }

        // ---------- candidate generation ----------

        class Candidate
        {
            public Rgb Color;
            public double[] SimLab;  // Lab of the simulated color
            public double Cost;      // perceptual change from the original under normal vision
        }

        static List<Candidate> BuildCandidates(Rgb original, Settings s)
        {
            double[] origLab = ToLab(original);
            double h, sat, v;
            RgbToHsv(original, out h, out sat, out v);

            List<Candidate> list = new List<Candidate>();
            for (double dh = -180 + s.HueStepDeg; dh <= 180.0001; dh += s.HueStepDeg)
            {
                foreach (double vf in s.ValueFactors)
                {
                    double nv = Math.Min(1.0, Math.Max(s.MinValue, v * vf));
                    double nh = ((h + dh) % 360 + 360) % 360;
                    Rgb c = HsvToRgb(nh, sat, nv);
                    list.Add(new Candidate
                    {
                        Color = c,
                        SimLab = ToLab(Simulate(c, s.Type, s.Severity)),
                        Cost = DeltaE2000(origLab, ToLab(c))
                    });
                }
            }
            // The unchanged color is always a candidate, so the search can keep one saber as is.
            list.AddRange(OriginalOnly(original, s));
            return list;
        }

        static List<Candidate> OriginalOnly(Rgb original, Settings s)
        {
            return new List<Candidate>
            {
                new Candidate
                {
                    Color = original,
                    SimLab = ToLab(Simulate(original, s.Type, s.Severity)),
                    Cost = 0
                }
            };
        }

        static bool SameColor(Rgb a, Rgb b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        // ---------- color vision simulation ----------

        // Machado et al. (2009) severity 1.0 matrices, row-major, applied in linear RGB.
        static readonly double[] MachadoProtan =
        {
            0.152286, 1.052583, -0.204868,
            0.114503, 0.786281, 0.099216,
            -0.003882, -0.048116, 1.051998
        };

        static readonly double[] MachadoDeutan =
        {
            0.367322, 0.860646, -0.227968,
            0.280085, 0.672501, 0.047413,
            -0.011820, 0.042940, 0.968881
        };

        // Brettel, Vienot & Mollon (1997) tritanopia in libDaltonLens's precomputed form (public
        // domain): sRGB primaries, Smith & Pokorny 1975 LMS, RGB white as the neutral axis.
        // The sign of the dot product with the separation normal picks the half-plane.
        static readonly double[] BrettelTritan1 =
        {
            1.01277, 0.13548, -0.14826,
            -0.01243, 0.86812, 0.14431,
            0.07589, 0.80500, 0.11911
        };

        static readonly double[] BrettelTritan2 =
        {
            0.93678, 0.18979, -0.12657,
            0.06154, 0.81526, 0.12320,
            -0.37562, 1.12767, 0.24796
        };

        static readonly double[] BrettelTritanNormal = { 0.03901, -0.02788, -0.01113 };

        // Severity < 1 is a linear interpolation in linear RGB between the input and the full
        // simulation. For Machado that equals interpolating from the identity matrix, which
        // approximates the published per-severity tables rather than reproducing them exactly.
        public static Rgb Simulate(Rgb c, CvdType type, double severity)
        {
            double s = Math.Min(1.0, Math.Max(0.0, severity));
            double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);

            double[] m;
            switch (type)
            {
                case CvdType.Protan: m = MachadoProtan; break;
                case CvdType.Deutan: m = MachadoDeutan; break;
                default:
                    double side = BrettelTritanNormal[0] * r + BrettelTritanNormal[1] * g
                                + BrettelTritanNormal[2] * b;
                    m = side >= 0 ? BrettelTritan1 : BrettelTritan2;
                    break;
            }

            double sr = m[0] * r + m[1] * g + m[2] * b;
            double sg = m[3] * r + m[4] * g + m[5] * b;
            double sb = m[6] * r + m[7] * g + m[8] * b;
            return new Rgb(ToSrgb(r + s * (sr - r)), ToSrgb(g + s * (sg - g)), ToSrgb(b + s * (sb - b)));
        }

        // ---------- color space conversions ----------

        static double ToLinear(double c)
        {
            c = Math.Min(1.0, Math.Max(0.0, c));
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double ToSrgb(double c)
        {
            c = Math.Min(1.0, Math.Max(0.0, c));
            return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        }

        public static double[] ToLab(Rgb c)
        {
            double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
            double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
            double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
            double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
            double fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }

        static double LabF(double t)
        {
            return t > 216.0 / 24389.0 ? Math.Pow(t, 1.0 / 3.0) : (24389.0 / 27.0 * t + 16) / 116;
        }

        static void RgbToHsv(Rgb c, out double h, out double s, out double v)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            double min = Math.Min(c.R, Math.Min(c.G, c.B));
            double d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == c.R) h = 60 * (((c.G - c.B) / d) % 6);
            else if (max == c.G) h = 60 * ((c.B - c.R) / d + 2);
            else h = 60 * ((c.R - c.G) / d + 4);
            if (h < 0) h += 360;
        }

        static Rgb HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return new Rgb(r + m, g + m, b + m);
        }

        // ---------- CIEDE2000 ----------

        static double Rad(double deg) { return deg * Math.PI / 180.0; }

        static double HueDeg(double b, double a)
        {
            if (a == 0 && b == 0) return 0;
            double h = Math.Atan2(b, a) * 180.0 / Math.PI;
            return h < 0 ? h + 360 : h;
        }

        public static double DeltaE2000(double[] lab1, double[] lab2)
        {
            double L1 = lab1[0], a1 = lab1[1], b1 = lab1[2];
            double L2 = lab2[0], a2 = lab2[1], b2 = lab2[2];

            double C1 = Math.Sqrt(a1 * a1 + b1 * b1), C2 = Math.Sqrt(a2 * a2 + b2 * b2);
            double Cbar7 = Math.Pow((C1 + C2) / 2, 7);
            double G = 0.5 * (1 - Math.Sqrt(Cbar7 / (Cbar7 + Math.Pow(25, 7))));
            double a1p = (1 + G) * a1, a2p = (1 + G) * a2;
            double C1p = Math.Sqrt(a1p * a1p + b1 * b1), C2p = Math.Sqrt(a2p * a2p + b2 * b2);
            double h1p = HueDeg(b1, a1p), h2p = HueDeg(b2, a2p);

            double dLp = L2 - L1, dCp = C2p - C1p;
            double dhp = 0;
            if (C1p * C2p != 0)
            {
                dhp = h2p - h1p;
                if (dhp > 180) dhp -= 360;
                else if (dhp < -180) dhp += 360;
            }
            double dHp = 2 * Math.Sqrt(C1p * C2p) * Math.Sin(Rad(dhp) / 2);

            double Lbp = (L1 + L2) / 2, Cbp = (C1p + C2p) / 2;
            double hbp;
            if (C1p * C2p == 0) hbp = h1p + h2p;
            else if (Math.Abs(h1p - h2p) <= 180) hbp = (h1p + h2p) / 2;
            else if (h1p + h2p < 360) hbp = (h1p + h2p + 360) / 2;
            else hbp = (h1p + h2p - 360) / 2;

            double T = 1 - 0.17 * Math.Cos(Rad(hbp - 30)) + 0.24 * Math.Cos(Rad(2 * hbp))
                         + 0.32 * Math.Cos(Rad(3 * hbp + 6)) - 0.20 * Math.Cos(Rad(4 * hbp - 63));
            double dTheta = 30 * Math.Exp(-Math.Pow((hbp - 275) / 25, 2));
            double Cbp7 = Math.Pow(Cbp, 7);
            double Rc = 2 * Math.Sqrt(Cbp7 / (Cbp7 + Math.Pow(25, 7)));
            double l50 = (Lbp - 50) * (Lbp - 50);
            double Sl = 1 + 0.015 * l50 / Math.Sqrt(20 + l50);
            double Sc = 1 + 0.045 * Cbp;
            double Sh = 1 + 0.015 * Cbp * T;
            double Rt = -Math.Sin(Rad(2 * dTheta)) * Rc;

            double tl = dLp / Sl, tc = dCp / Sc, th = dHp / Sh;
            return Math.Sqrt(tl * tl + tc * tc + th * th + Rt * tc * th);
        }
    }
}
