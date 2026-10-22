using System.Globalization;
using UnityEngine;

namespace aleyhin.utils
{
    public sealed class RecolorRamp
    {
        public readonly string Source;
        public readonly Color32[] Steps;
        public readonly int Main;
        public readonly Vector3 MainLab;

        private readonly Vector3[] _offsets;

        public RecolorRamp(string source, string hexSteps)
        {
            Source = source;
            string[] parts = hexSteps.Split(' ');
            Steps = new Color32[parts.Length];
            Vector3[] labs = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Steps[i] = ParseHex(parts[i]);
                labs[i] = RecolorPalette.ToOklab(Steps[i]);
                if (RecolorPalette.Chroma(labs[i]) > RecolorPalette.Chroma(labs[Main]))
                {
                    Main = i;
                }
            }
            MainLab = labs[Main];

            _offsets = new Vector3[parts.Length];
            for (int i = Main - 1; i >= 0; i--)
            {
                _offsets[i] = Offset(labs, i, _offsets[i + 1]);
            }
            for (int i = Main + 1; i < parts.Length; i++)
            {
                _offsets[i] = Offset(labs, i, _offsets[i - 1]);
            }
        }

        public int Count => Steps.Length;

        public Vector3 Offset(int step)
        {
            return _offsets[step];
        }

        public Vector2 Drift(float deltaL)
        {
            int last = _offsets.Length - 1;
            if (deltaL <= _offsets[0].x)
            {
                return new Vector2(_offsets[0].y, _offsets[0].z);
            }
            if (deltaL >= _offsets[last].x)
            {
                return new Vector2(_offsets[last].y, _offsets[last].z);
            }
            int step = 0;
            while (_offsets[step + 1].x < deltaL)
            {
                step++;
            }
            Vector3 from = _offsets[step];
            Vector3 to = _offsets[step + 1];
            float t = Mathf.InverseLerp(from.x, to.x, deltaL);
            return new Vector2(Mathf.Lerp(from.y, to.y, t), Mathf.Lerp(from.z, to.z, t));
        }

        private Vector3 Offset(Vector3[] labs, int step, Vector3 previous)
        {
            int neighbour = step < Main ? step + 1 : step - 1;
            float mainChroma = Mathf.Max(RecolorPalette.Chroma(MainLab), RecolorPalette.NEUTRAL_CHROMA);
            float chroma = Mathf.Max(RecolorPalette.Chroma(labs[step]), RecolorPalette.NEUTRAL_CHROMA);
            return new Vector3(
                labs[step].x - MainLab.x,
                previous.y + Mathf.DeltaAngle(RecolorPalette.Hue(labs[neighbour]), RecolorPalette.Hue(labs[step])),
                Mathf.Log(chroma / mainChroma));
        }

        private static Color32 ParseHex(string hex)
        {
            return new Color32(
                byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
                255);
        }
    }
}
