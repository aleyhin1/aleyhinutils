using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace aleyhin.utils
{
    public static class RecolorPalette
    {
        public const int PALETTE_SIZE = 256;
        public const int LUT_SIZE = 32;
        public const int GROUP_COUNT = 10;
        public const float NEUTRAL_CHROMA = 0.05f;
        public const float GROUP_MIN_SHARE = 1e-4f;
        public const string LUT_PROPERTY = "_RecolorLut";
        public const string GROUP_COLOR_PROPERTY = "_GroupColor";
        public const string BASE_PALETTE_NAME = "Base Palette";
        public const string LUT_NAME = "Recolor LUT";

        private const int _GAMUT_STEPS = 12;
        private const float _GAMUT_TOLERANCE = 1e-3f;
        private const float _NEUTRAL_BLEND = 0.02f;
        private const float _FAMILY_HUE_WIDTH = 12f;

        private static readonly float[] _HUE_LIMITS = { 50f, 70f, 95f, 112f, 150f, 185f, 225f, 285f, 330f, 360f };
        private static readonly string[] _HUE_NAMES = { "Reds", "Oranges", "Golds", "Yellows", "Greens", "Teals", "Cyans", "Blues", "Purples", "Pinks" };
        private static readonly Dictionary<int, Color[]> _bakedTargets = new Dictionary<int, Color[]>();

        private struct Family
        {
            public Vector3 Mean;
            public float Hue;
            public float Chroma;
            public bool Neutral;
            public Vector2 ShadowSlope;
            public Vector2 HighlightSlope;
        }

        public static T FindSubAsset<T>(Material material, string name) where T : Object
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(material)))
            {
                if (asset is T typed && typed.name == name)
                {
                    return typed;
                }
            }
            return null;
        }

        public static void ReadBasePalette(Texture2D basePalette, out Color32[] colors, out int[] groups, out int groupCount)
        {
            Color32[] data = basePalette.GetPixels32();
            colors = new Color32[PALETTE_SIZE];
            groups = new int[PALETTE_SIZE];
            groupCount = 0;
            for (int i = 0; i < PALETTE_SIZE; i++)
            {
                colors[i] = data[i];
                groups[i] = data[PALETTE_SIZE + i].r;
                groupCount = Mathf.Max(groupCount, groups[i] + 1);
            }
        }

        public static Color[] ReadTargets(Material material)
        {
            Color[] targets = new Color[GROUP_COUNT];
            for (int group = 0; group < GROUP_COUNT; group++)
            {
                targets[group] = material.GetColor(GROUP_COLOR_PROPERTY + group);
            }
            return targets;
        }

        public static Vector3[] GroupMeans(Color32[] colors, int[] groups, int groupCount)
        {
            Vector3[] sums = new Vector3[groupCount];
            int[] counts = new int[groupCount];
            for (int i = 0; i < colors.Length; i++)
            {
                sums[groups[i]] += ToOklab(colors[i]);
                counts[groups[i]]++;
            }
            for (int group = 0; group < groupCount; group++)
            {
                sums[group] /= Mathf.Max(counts[group], 1);
            }
            return sums;
        }

        public static RecolorRamp Match(Color target)
        {
            return RecolorLibrary.Nearest(ToOklab(target));
        }

        public static Color32[] Transferred(RecolorRamp ramp, Color target)
        {
            Vector3 main = ToOklab(target);
            Color32[] steps = new Color32[ramp.Count];
            for (int step = 0; step < ramp.Count; step++)
            {
                steps[step] = FromOklab(Compose(main, ramp.Offset(step)), 255);
            }
            return steps;
        }

        public static Color32[] Recolor(Color32[] colors, Color32[] baseColors, int[] groups, int groupCount, Color[] targets)
        {
            Color32[] result = (Color32[])colors.Clone();
            bool anyTarget = false;
            for (int group = 0; group < groupCount; group++)
            {
                anyTarget |= targets[group].a > 0f;
            }
            if (!anyTarget)
            {
                return result;
            }

            Family[] families = BuildFamilies(baseColors, groups, groupCount);
            Vector3[] picks = new Vector3[groupCount];
            RecolorRamp[] ramps = new RecolorRamp[groupCount];
            for (int group = 0; group < groupCount; group++)
            {
                if (targets[group].a > 0f)
                {
                    picks[group] = ToOklab(targets[group]);
                    ramps[group] = families[group].Neutral ? null : RecolorLibrary.Nearest(picks[group]);
                }
            }

            float[] shares = new float[groupCount];
            for (int i = 0; i < colors.Length; i++)
            {
                Vector3 lab = ToOklab(colors[i]);
                Shares(lab, families, shares);
                bool touched = false;
                Vector3 blended = Vector3.zero;
                for (int group = 0; group < groupCount; group++)
                {
                    if (shares[group] < GROUP_MIN_SHARE)
                    {
                        continue;
                    }
                    bool targeted = targets[group].a > 0f;
                    touched |= targeted;
                    blended += shares[group] * (targeted ? Transfer(lab, families[group], picks[group], ramps[group]) : lab);
                }
                if (touched)
                {
                    result[i] = FromOklab(blended, colors[i].a);
                }
            }
            return result;
        }

        public static Color32[] IdentityLut()
        {
            Color32[] colors = new Color32[LUT_SIZE * LUT_SIZE * LUT_SIZE];
            for (int b = 0; b < LUT_SIZE; b++)
            {
                for (int g = 0; g < LUT_SIZE; g++)
                {
                    for (int r = 0; r < LUT_SIZE; r++)
                    {
                        colors[r + g * LUT_SIZE + b * LUT_SIZE * LUT_SIZE] = new Color32(LutLevel(r), LutLevel(g), LutLevel(b), 255);
                    }
                }
            }
            return colors;
        }

        public static void Bake(Material material)
        {
            Color[] targets = ReadTargets(material);
            int id = material.GetInstanceID();
            if (_bakedTargets.TryGetValue(id, out Color[] baked) && SameColors(baked, targets))
            {
                return;
            }

            Texture2D basePalette = FindSubAsset<Texture2D>(material, BASE_PALETTE_NAME);
            Texture3D lut = FindSubAsset<Texture3D>(material, LUT_NAME);
            if (basePalette == null || lut == null)
            {
                return;
            }

            ReadBasePalette(basePalette, out Color32[] colors, out int[] groups, out int groupCount);
            Color32[] result = Recolor(IdentityLut(), colors, groups, groupCount, targets);
            Color32[] current = lut.GetPixels32();
            for (int i = 0; i < result.Length; i++)
            {
                if (current[i].r != result[i].r || current[i].g != result[i].g || current[i].b != result[i].b)
                {
                    lut.SetPixels32(result);
                    lut.Apply(false);
                    EditorUtility.SetDirty(lut);
                    break;
                }
            }
            _bakedTargets[id] = targets;
        }

        public static string GroupName(Vector3 lab)
        {
            if (Chroma(lab) < NEUTRAL_CHROMA)
            {
                return "Neutrals";
            }

            float hue = Mathf.Repeat(Hue(lab), 360f);
            int band = 0;
            while (hue >= _HUE_LIMITS[band])
            {
                band++;
            }
            return _HUE_NAMES[band];
        }

        public static Vector3 ToOklab(Color color)
        {
            float r = SrgbToLinear(color.r);
            float g = SrgbToLinear(color.g);
            float b = SrgbToLinear(color.b);
            float l = Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        public static Color32 FromOklab(Vector3 lab, byte alpha)
        {
            Vector3 linear = ToLinear(lab);
            return new Color32(ToByte(linear.x), ToByte(linear.y), ToByte(linear.z), alpha);
        }

        public static float Hue(Vector3 lab)
        {
            return Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
        }

        public static float Chroma(Vector3 lab)
        {
            return new Vector2(lab.y, lab.z).magnitude;
        }

        private static Family[] BuildFamilies(Color32[] baseColors, int[] groups, int groupCount)
        {
            Vector3[] means = GroupMeans(baseColors, groups, groupCount);
            Family[] families = new Family[groupCount];
            for (int group = 0; group < groupCount; group++)
            {
                families[group].Mean = means[group];
                families[group].Hue = Hue(means[group]);
                families[group].Chroma = Mathf.Max(Chroma(means[group]), NEUTRAL_CHROMA);
                families[group].Neutral = Chroma(means[group]) < NEUTRAL_CHROMA;
            }

            Vector3[] shadowSums = new Vector3[groupCount];
            Vector3[] highlightSums = new Vector3[groupCount];
            for (int i = 0; i < baseColors.Length; i++)
            {
                int group = groups[i];
                Vector3 lab = ToOklab(baseColors[i]);
                if (families[group].Neutral || Chroma(lab) < NEUTRAL_CHROMA)
                {
                    continue;
                }
                float deltaL = lab.x - families[group].Mean.x;
                Vector3 moment = new Vector3(
                    deltaL * deltaL,
                    deltaL * Mathf.DeltaAngle(families[group].Hue, Hue(lab)),
                    deltaL * Mathf.Log(Chroma(lab) / families[group].Chroma));
                if (deltaL < 0f)
                {
                    shadowSums[group] += moment;
                }
                else
                {
                    highlightSums[group] += moment;
                }
            }
            for (int group = 0; group < groupCount; group++)
            {
                families[group].ShadowSlope = Slope(shadowSums[group]);
                families[group].HighlightSlope = Slope(highlightSums[group]);
            }
            return families;
        }

        private static Vector2 Slope(Vector3 sums)
        {
            return sums.x > 0f ? new Vector2(sums.y / sums.x, sums.z / sums.x) : Vector2.zero;
        }

        private static void Shares(Vector3 lab, Family[] families, float[] shares)
        {
            float chroma = Chroma(lab);
            float hue = Hue(lab);
            float neutral = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NEUTRAL_CHROMA - _NEUTRAL_BLEND, NEUTRAL_CHROMA + _NEUTRAL_BLEND, chroma));
            int neutralFamily = -1;
            float nearest = float.MaxValue;
            for (int group = 0; group < families.Length; group++)
            {
                shares[group] = 0f;
                if (families[group].Neutral)
                {
                    if (neutralFamily < 0 || Mathf.Abs(families[group].Mean.x - lab.x) < Mathf.Abs(families[neutralFamily].Mean.x - lab.x))
                    {
                        neutralFamily = group;
                    }
                    continue;
                }
                float distance = Mathf.DeltaAngle(hue, families[group].Hue);
                shares[group] = distance * distance;
                nearest = Mathf.Min(nearest, shares[group]);
            }

            float total = 0f;
            for (int group = 0; group < families.Length; group++)
            {
                if (!families[group].Neutral)
                {
                    shares[group] = Mathf.Exp(-(shares[group] - nearest) / (2f * _FAMILY_HUE_WIDTH * _FAMILY_HUE_WIDTH));
                    total += shares[group];
                }
            }
            float chromatic = neutralFamily < 0 ? 1f : 1f - neutral;
            for (int group = 0; group < families.Length; group++)
            {
                if (!families[group].Neutral)
                {
                    shares[group] *= chromatic / total;
                }
            }
            if (neutralFamily >= 0)
            {
                shares[neutralFamily] = total > 0f ? neutral : 1f;
            }
        }

        private static Vector3 Transfer(Vector3 lab, Family family, Vector3 pick, RecolorRamp ramp)
        {
            float deltaL = lab.x - family.Mean.x;
            float lightness = Mathf.Clamp01(lab.x + pick.x - family.Mean.x);
            if (family.Neutral)
            {
                return new Vector3(lightness, lab.y + pick.y - family.Mean.y, lab.z + pick.z - family.Mean.z);
            }

            Vector2 slope = deltaL < 0f ? family.ShadowSlope : family.HighlightSlope;
            Vector2 drift = ramp.Drift(deltaL);
            float hue = (Hue(pick) + Mathf.DeltaAngle(family.Hue, Hue(lab)) - slope.x * deltaL + drift.x) * Mathf.Deg2Rad;
            float chroma = Chroma(lab) * Chroma(pick) / family.Chroma * Mathf.Exp(drift.y - slope.y * deltaL);
            return ClipChroma(new Vector3(lightness, Mathf.Cos(hue) * chroma, Mathf.Sin(hue) * chroma));
        }

        private static Vector3 Compose(Vector3 main, Vector3 offset)
        {
            float hue = (Hue(main) + offset.y) * Mathf.Deg2Rad;
            float chroma = Chroma(main) * Mathf.Exp(offset.z);
            return ClipChroma(new Vector3(Mathf.Clamp01(main.x + offset.x), Mathf.Cos(hue) * chroma, Mathf.Sin(hue) * chroma));
        }

        private static Vector3 ToLinear(Vector3 lab)
        {
            float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            return new Vector3(
                4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        private static bool InGamut(Vector3 lab)
        {
            Vector3 linear = ToLinear(lab);
            return Mathf.Min(linear.x, Mathf.Min(linear.y, linear.z)) >= -_GAMUT_TOLERANCE && Mathf.Max(linear.x, Mathf.Max(linear.y, linear.z)) <= 1f + _GAMUT_TOLERANCE;
        }

        private static Vector3 ClipChroma(Vector3 lab)
        {
            if (InGamut(lab))
            {
                return lab;
            }

            float low = 0f;
            float high = 1f;
            for (int step = 0; step < _GAMUT_STEPS; step++)
            {
                float scale = (low + high) * 0.5f;
                if (InGamut(new Vector3(lab.x, lab.y * scale, lab.z * scale)))
                {
                    low = scale;
                }
                else
                {
                    high = scale;
                }
            }
            return new Vector3(lab.x, lab.y * low, lab.z * low);
        }

        private static byte LutLevel(int step)
        {
            float visible = (float)step / (LUT_SIZE - 1);
            visible *= visible;
            return PlayerSettings.colorSpace == ColorSpace.Linear ? ToByte(visible) : (byte)Mathf.RoundToInt(visible * 255f);
        }

        private static bool SameColors(Color[] a, Color[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static float SrgbToLinear(float value)
        {
            return value <= 0.04045f ? value / 12.92f : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        private static byte ToByte(float linear)
        {
            float value = Mathf.Clamp01(linear);
            float srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * Mathf.Pow(value, 1f / 2.4f) - 0.055f;
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(srgb) * 255f);
        }

        private static float Cbrt(float value)
        {
            return Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), 1f / 3f);
        }
    }
}
