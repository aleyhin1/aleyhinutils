using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace aleyhin.utils
{
    public static class RecolorMaterialTool
    {
        private const string _MENU_PATH = "Assets/Create Recolor Material";
        private const string _SHADER_NAME = "aleyhin/UI/Recolor";
        private const string _PROGRESS_TITLE = "Recolor Material";
        private const int _KMEANS_ITERATIONS = 8;
        private const int _MODE_ITERATIONS = 20;
        private const float _MODE_WIDTH = 15f;
        private const float _MODE_MERGE = 8f;
        private const int _FIT_SHIFT = 2;

        [MenuItem(_MENU_PATH, true)]
        private static bool CanCreate()
        {
            return Selection.activeObject is Texture2D;
        }

        [MenuItem(_MENU_PATH)]
        private static void Create()
        {
            foreach (Object selected in Selection.objects)
            {
                if (selected is Texture2D)
                {
                    CreateForTexture(AssetDatabase.GetAssetPath(selected));
                }
            }
            EditorUtility.ClearProgressBar();
        }

        private static void CreateForTexture(string sourcePath)
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                Debug.LogError($"Recolor: {sourcePath} is not a PNG or JPG.");
                return;
            }

            EditorUtility.DisplayProgressBar(_PROGRESS_TITLE, sourcePath, 0f);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            Color32[] pixels = source.GetPixels32();
            Object.DestroyImmediate(source);

            HashSet<int> uniqueColors = new HashSet<int>();
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a > 0)
                {
                    uniqueColors.Add(pixel.r | (pixel.g << 8) | (pixel.b << 16));
                }
            }
            if (uniqueColors.Count == 0)
            {
                Debug.LogError($"Recolor: {sourcePath} has no visible pixels.");
                return;
            }

            BuildFitPoints(pixels, out float[] fitColors, out float[] fitWeights);
            float[] palette = MedianCut(fitColors, fitWeights, out int paletteCount);
            KMeans(fitColors, fitWeights, palette, paletteCount);
            Color32[] paletteColors = ToColors(palette, paletteCount);

            int[] groups = GroupPalette(paletteColors, paletteCount, out int groupCount);
            int[] order = OrderEntries(paletteColors, paletteCount);
            Color32[] basePalette = new Color32[RecolorPalette.PALETTE_SIZE * 2];
            for (int entry = 0; entry < RecolorPalette.PALETTE_SIZE; entry++)
            {
                int previous = order[Mathf.Min(entry, paletteCount - 1)];
                basePalette[entry] = paletteColors[previous];
                basePalette[RecolorPalette.PALETTE_SIZE + entry] = new Color32((byte)groups[previous], 0, 0, 255);
            }

            string materialPath = $"{Path.GetDirectoryName(sourcePath).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(sourcePath)}_palette.mat";
            Material material = CreateOrUpdateMaterial(materialPath, basePalette);
            EditorGUIUtility.PingObject(material);
            Debug.Log($"Recolor: {sourcePath} has {uniqueColors.Count} unique colors, summarized as {paletteCount} palette entries in {groupCount} color groups. Wrote {materialPath}.");
        }

        private static void BuildFitPoints(Color32[] pixels, out float[] colors, out float[] weights)
        {
            Dictionary<int, double[]> bins = new Dictionary<int, double[]>();
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a == 0)
                {
                    continue;
                }
                int key = (pixel.r >> _FIT_SHIFT) | ((pixel.g >> _FIT_SHIFT) << 6) | ((pixel.b >> _FIT_SHIFT) << 12);
                if (!bins.TryGetValue(key, out double[] sum))
                {
                    sum = new double[4];
                    bins[key] = sum;
                }
                sum[0] += pixel.r;
                sum[1] += pixel.g;
                sum[2] += pixel.b;
                sum[3] += 1.0;
            }

            colors = new float[bins.Count * 3];
            weights = new float[bins.Count];
            int index = 0;
            foreach (double[] sum in bins.Values)
            {
                for (int c = 0; c < 3; c++)
                {
                    colors[index * 3 + c] = (float)(sum[c] / sum[3]);
                }
                weights[index] = (float)sum[3];
                index++;
            }
        }

        private static float[] MedianCut(float[] colors, float[] weights, out int count)
        {
            List<int> all = new List<int>(weights.Length);
            for (int i = 0; i < weights.Length; i++)
            {
                all.Add(i);
            }
            List<List<int>> boxes = new List<List<int>> { all };

            while (boxes.Count < RecolorPalette.PALETTE_SIZE)
            {
                int bestBox = -1;
                int bestAxis = 0;
                float bestScore = 0f;
                for (int b = 0; b < boxes.Count; b++)
                {
                    List<int> box = boxes[b];
                    if (box.Count < 2)
                    {
                        continue;
                    }
                    float weight = 0f;
                    foreach (int point in box)
                    {
                        weight += weights[point];
                    }
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float min = float.MaxValue;
                        float max = float.MinValue;
                        foreach (int point in box)
                        {
                            float value = colors[point * 3 + axis];
                            min = Mathf.Min(min, value);
                            max = Mathf.Max(max, value);
                        }
                        float score = (max - min) * Mathf.Sqrt(weight);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestBox = b;
                            bestAxis = axis;
                        }
                    }
                }
                if (bestBox < 0)
                {
                    break;
                }

                List<int> split = boxes[bestBox];
                boxes.RemoveAt(bestBox);
                int sortAxis = bestAxis;
                split.Sort((x, y) => colors[x * 3 + sortAxis].CompareTo(colors[y * 3 + sortAxis]));
                float total = 0f;
                foreach (int point in split)
                {
                    total += weights[point];
                }
                float accumulated = 0f;
                int cut = 1;
                for (int i = 0; i < split.Count; i++)
                {
                    accumulated += weights[split[i]];
                    if (accumulated >= total * 0.5f)
                    {
                        cut = Mathf.Clamp(i + 1, 1, split.Count - 1);
                        break;
                    }
                }
                boxes.Add(split.GetRange(0, cut));
                boxes.Add(split.GetRange(cut, split.Count - cut));
            }

            count = boxes.Count;
            float[] palette = new float[count * 3];
            for (int b = 0; b < count; b++)
            {
                double[] sum = new double[4];
                foreach (int point in boxes[b])
                {
                    for (int c = 0; c < 3; c++)
                    {
                        sum[c] += colors[point * 3 + c] * weights[point];
                    }
                    sum[3] += weights[point];
                }
                for (int c = 0; c < 3; c++)
                {
                    palette[b * 3 + c] = (float)(sum[c] / sum[3]);
                }
            }
            return palette;
        }

        private static void KMeans(float[] colors, float[] weights, float[] palette, int count)
        {
            for (int iteration = 0; iteration < _KMEANS_ITERATIONS; iteration++)
            {
                double[] sums = new double[count * 4];
                for (int p = 0; p < weights.Length; p++)
                {
                    int nearest = Nearest(palette, count, colors[p * 3], colors[p * 3 + 1], colors[p * 3 + 2]);
                    double weight = weights[p];
                    for (int c = 0; c < 3; c++)
                    {
                        sums[nearest * 4 + c] += colors[p * 3 + c] * weight;
                    }
                    sums[nearest * 4 + 3] += weight;
                }
                for (int i = 0; i < count; i++)
                {
                    if (sums[i * 4 + 3] > 0.0)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            palette[i * 3 + c] = (float)(sums[i * 4 + c] / sums[i * 4 + 3]);
                        }
                    }
                }
            }
        }

        private static int Nearest(float[] palette, int count, float r, float g, float b)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float dr = r - palette[i * 3];
                float dg = g - palette[i * 3 + 1];
                float db = b - palette[i * 3 + 2];
                float distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static Color32[] ToColors(float[] palette, int count)
        {
            Color32[] colors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                colors[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(palette[i * 3]), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(palette[i * 3 + 1]), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(palette[i * 3 + 2]), 0, 255),
                    255);
            }
            return colors;
        }

        private static int[] GroupPalette(Color32[] colors, int count, out int groupCount)
        {
            Vector3[] labs = new Vector3[count];
            List<float> hues = new List<float>();
            bool anyNeutral = false;
            for (int i = 0; i < count; i++)
            {
                labs[i] = RecolorPalette.ToOklab(colors[i]);
                if (RecolorPalette.Chroma(labs[i]) < RecolorPalette.NEUTRAL_CHROMA)
                {
                    anyNeutral = true;
                }
                else
                {
                    hues.Add(RecolorPalette.Hue(labs[i]));
                }
            }

            List<float> modes = new List<float>();
            foreach (float hue in hues)
            {
                float mode = HueMode(hue, hues);
                bool known = false;
                foreach (float existing in modes)
                {
                    known |= Mathf.Abs(Mathf.DeltaAngle(existing, mode)) <= _MODE_MERGE;
                }
                if (!known)
                {
                    modes.Add(mode);
                }
            }

            int offset = anyNeutral ? 1 : 0;
            while (modes.Count > RecolorPalette.GROUP_COUNT - offset)
            {
                int first = 0;
                int second = 1;
                float closest = float.MaxValue;
                for (int a = 0; a < modes.Count; a++)
                {
                    for (int b = a + 1; b < modes.Count; b++)
                    {
                        float distance = Mathf.Abs(Mathf.DeltaAngle(modes[a], modes[b]));
                        if (distance < closest)
                        {
                            closest = distance;
                            first = a;
                            second = b;
                        }
                    }
                }
                modes[first] = Mathf.Repeat(modes[first] + Mathf.DeltaAngle(modes[first], modes[second]) * 0.5f, 360f);
                modes.RemoveAt(second);
            }
            modes.Sort();

            groupCount = modes.Count + offset;
            int[] groups = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (RecolorPalette.Chroma(labs[i]) < RecolorPalette.NEUTRAL_CHROMA)
                {
                    continue;
                }
                float hue = RecolorPalette.Hue(labs[i]);
                int nearest = 0;
                for (int mode = 1; mode < modes.Count; mode++)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(modes[mode], hue)) < Mathf.Abs(Mathf.DeltaAngle(modes[nearest], hue)))
                    {
                        nearest = mode;
                    }
                }
                groups[i] = offset + nearest;
            }
            return groups;
        }

        private static float HueMode(float start, List<float> hues)
        {
            float mode = start;
            for (int iteration = 0; iteration < _MODE_ITERATIONS; iteration++)
            {
                Vector2 sum = Vector2.zero;
                foreach (float hue in hues)
                {
                    float distance = Mathf.DeltaAngle(mode, hue);
                    float weight = Mathf.Exp(-distance * distance / (2f * _MODE_WIDTH * _MODE_WIDTH));
                    sum += weight * new Vector2(Mathf.Cos(hue * Mathf.Deg2Rad), Mathf.Sin(hue * Mathf.Deg2Rad));
                }
                mode = Mathf.Atan2(sum.y, sum.x) * Mathf.Rad2Deg;
            }
            return Mathf.Repeat(mode, 360f);
        }

        private static int[] OrderEntries(Color32[] colors, int count)
        {
            int start = 0;
            for (int i = 1; i < count; i++)
            {
                if (colors[i].r + colors[i].g + colors[i].b < colors[start].r + colors[start].g + colors[start].b)
                {
                    start = i;
                }
            }

            List<int> order = new List<int>(count) { start };
            bool[] used = new bool[count];
            used[start] = true;
            while (order.Count < count)
            {
                int last = order[order.Count - 1];
                int next = -1;
                float nextDistance = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }
                    float distance = Distance(colors[last], colors[i]);
                    if (distance < nextDistance)
                    {
                        nextDistance = distance;
                        next = i;
                    }
                }
                used[next] = true;
                order.Add(next);
            }

            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int i = 1; i < count - 1; i++)
                {
                    for (int k = i + 1; k < count; k++)
                    {
                        float before = Distance(colors[order[i - 1]], colors[order[i]]);
                        float after = Distance(colors[order[i - 1]], colors[order[k]]);
                        if (k + 1 < count)
                        {
                            before += Distance(colors[order[k]], colors[order[k + 1]]);
                            after += Distance(colors[order[i]], colors[order[k + 1]]);
                        }
                        if (after < before - 1e-3f)
                        {
                            order.Reverse(i, k - i + 1);
                            improved = true;
                        }
                    }
                }
            }
            return order.ToArray();
        }

        private static float Distance(Color32 a, Color32 b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static Material CreateOrUpdateMaterial(string materialPath, Color32[] basePaletteData)
        {
            Shader shader = Shader.Find(_SHADER_NAME);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;

            Texture2D basePalette = RecolorPalette.FindSubAsset<Texture2D>(material, RecolorPalette.BASE_PALETTE_NAME);
            if (basePalette == null)
            {
                basePalette = new Texture2D(RecolorPalette.PALETTE_SIZE, 2, TextureFormat.RGBA32, false, false)
                {
                    name = RecolorPalette.BASE_PALETTE_NAME,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                AssetDatabase.AddObjectToAsset(basePalette, material);
            }
            basePalette.SetPixels32(basePaletteData);
            basePalette.Apply(false, false);

            Texture3D lut = RecolorPalette.FindSubAsset<Texture3D>(material, RecolorPalette.LUT_NAME);
            if (lut == null)
            {
                lut = new Texture3D(RecolorPalette.LUT_SIZE, RecolorPalette.LUT_SIZE, RecolorPalette.LUT_SIZE, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
                {
                    name = RecolorPalette.LUT_NAME,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                AssetDatabase.AddObjectToAsset(lut, material);
            }
            lut.SetPixels32(RecolorPalette.IdentityLut());
            lut.Apply(false);

            material.SetTexture(RecolorPalette.LUT_PROPERTY, lut);
            for (int group = 0; group < RecolorPalette.GROUP_COUNT; group++)
            {
                material.SetColor(RecolorPalette.GROUP_COLOR_PROPERTY + group, Color.clear);
            }
            EditorUtility.SetDirty(basePalette);
            EditorUtility.SetDirty(lut);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
