using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace aleyhin.utils
{
    public class RecolorShaderGUI : ShaderGUI
    {
        private const float _STRIP_HEIGHT = 18f;
        private const float _WHEEL_SIZE = 96f;
        private const float _SWATCH_WIDTH = 40f;
        private const float _RESET_WIDTH = 50f;
        private const float _LINE_GAP = 4f;
        private const float _COLUMN_GAP = 8f;
        private const float _MARK_SIZE = 4f;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = (Material)materialEditor.target;
            Texture2D basePalette = RecolorPalette.FindSubAsset<Texture2D>(material, RecolorPalette.BASE_PALETTE_NAME);
            if (basePalette == null)
            {
                EditorGUILayout.HelpBox("No color families yet. Right-click the source texture and run Create Recolor Material.", MessageType.Info);
                base.OnGUI(materialEditor, properties);
                return;
            }

            RecolorPalette.ReadBasePalette(basePalette, out Color32[] colors, out int[] groups, out int groupCount);
            MaterialProperty[] targets = new MaterialProperty[groupCount];
            Color[] targetColors = new Color[RecolorPalette.GROUP_COUNT];
            for (int group = 0; group < groupCount; group++)
            {
                targets[group] = FindProperty(RecolorPalette.GROUP_COLOR_PROPERTY + group, properties);
                targetColors[group] = targets[group].colorValue;
            }

            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
            DrawStrip("Original", colors);
            DrawStrip("Result", RecolorPalette.Recolor(colors, colors, groups, groupCount, targetColors));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Families", EditorStyles.boldLabel);
            Vector3[] sources = RecolorPalette.GroupMeans(colors, groups, groupCount);
            Dictionary<string, int> nameCounts = new Dictionary<string, int>();
            for (int group = 0; group < groupCount; group++)
            {
                string name = RecolorPalette.GroupName(sources[group]);
                nameCounts.TryGetValue(name, out int seen);
                nameCounts[name] = seen + 1;
                DrawFamily(seen == 0 ? name : $"{name} {seen + 1}", RecolorPalette.FromOklab(sources[group], 255), targets[group]);
            }

            if (GUILayout.Button("Reset All"))
            {
                foreach (MaterialProperty target in targets)
                {
                    target.colorValue = Color.clear;
                }
            }

            foreach (Object target in materialEditor.targets)
            {
                RecolorPalette.Bake((Material)target);
            }

            EditorGUILayout.Space();
            base.OnGUI(materialEditor, properties);
        }

        private static void DrawStrip(string label, Color32[] colors)
        {
            Rect rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(false, _STRIP_HEIGHT), new GUIContent(label));
            DrawSwatches(rect, colors, -1);
        }

        private static void DrawSwatches(Rect rect, Color32[] colors, int marked)
        {
            float width = rect.width / colors.Length;
            for (int i = 0; i < colors.Length; i++)
            {
                EditorGUI.DrawRect(new Rect(rect.x + i * width, rect.y, Mathf.Ceil(width), rect.height), colors[i]);
            }
            if (marked >= 0)
            {
                float x = rect.x + (marked + 0.5f) * width - _MARK_SIZE * 0.5f;
                EditorGUI.DrawRect(new Rect(x, rect.yMax - _MARK_SIZE - 2f, _MARK_SIZE, _MARK_SIZE), Color.white);
            }
        }

        private static void DrawFamily(string name, Color source, MaterialProperty target)
        {
            bool overridden = target.colorValue.a > 0f;
            Rect row = EditorGUILayout.GetControlRect(false, _WHEEL_SIZE);
            Rect wheelRect = new Rect(row.x, row.y, _WHEEL_SIZE, _WHEEL_SIZE);
            float x = wheelRect.xMax + _COLUMN_GAP;
            float width = row.xMax - x;
            float line = EditorGUIUtility.singleLineHeight;
            float stride = line + _LINE_GAP;
            Rect nameRect = new Rect(x, row.y, width - _RESET_WIDTH - _COLUMN_GAP, line);
            Rect resetRect = new Rect(row.xMax - _RESET_WIDTH, row.y, _RESET_WIDTH, line);
            Rect sourceRect = new Rect(x, row.y + stride + 1f, _SWATCH_WIDTH, line - 2f);
            Rect sourceLabelRect = new Rect(sourceRect.xMax + _COLUMN_GAP, row.y + stride, width - _SWATCH_WIDTH - _COLUMN_GAP, line);
            Rect libraryRect = new Rect(x, row.y + stride * 2f, width, line);
            Rect rampRect = new Rect(x, row.y + stride * 3f, width, _STRIP_HEIGHT);

            EditorGUI.BeginChangeCheck();
            Color picked = RecolorColorWheel.Draw(wheelRect, overridden ? target.colorValue : source);
            if (EditorGUI.EndChangeCheck())
            {
                target.colorValue = new Color(picked.r, picked.g, picked.b, 1f);
            }

            EditorGUI.LabelField(nameRect, name, EditorStyles.boldLabel);
            EditorGUI.DrawRect(sourceRect, source);
            EditorGUI.LabelField(sourceLabelRect, "Source", EditorStyles.miniLabel);
            if (overridden)
            {
                RecolorRamp ramp = RecolorPalette.Match(target.colorValue);
                EditorGUI.LabelField(libraryRect, "Library: " + ramp.Source, EditorStyles.miniLabel);
                DrawSwatches(rampRect, RecolorPalette.Transferred(ramp, target.colorValue), ramp.Main);
            }
            else
            {
                EditorGUI.LabelField(libraryRect, "Untouched", EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(!overridden))
            {
                if (GUI.Button(resetRect, "Reset"))
                {
                    target.colorValue = Color.clear;
                }
            }
        }
    }
}
