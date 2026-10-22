using UnityEngine;

namespace aleyhin.utils
{
    public static class RecolorColorWheel
    {
        private const int _RING_TEXELS = 128;
        private const int _SQUARE_TEXELS = 64;
        private const int _CURSOR_TEXELS = 16;
        private const float _RING_WIDTH = 0.18f;
        private const float _SQUARE_HALF = 0.66f;
        private const float _CURSOR_SIZE = 12f;
        private const float _EDGE_SOFTNESS = 1.5f;
        private const float _HUE_CACHE_STEP = 1f / 720f;

        private static Texture2D _ring;
        private static Texture2D _square;
        private static Texture2D _cursor;
        private static float _squareHue = -1f;
        private static bool _draggingRing;

        public static Color Draw(Rect rect, Color color)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            Vector2 center = rect.center;
            float inner = radius * (1f - _RING_WIDTH);
            float half = inner * _SQUARE_HALF;
            Rect square = new Rect(center.x - half, center.y - half, half * 2f, half * 2f);

            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        float distance = Vector2.Distance(current.mousePosition, center);
                        bool onRing = distance >= inner - 2f && distance <= radius + 2f;
                        if (onRing || square.Contains(current.mousePosition))
                        {
                            _draggingRing = onRing;
                            GUIUtility.hotControl = id;
                            Apply(current.mousePosition, center, square, ref hue, ref saturation, ref value);
                            current.Use();
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        Apply(current.mousePosition, center, square, ref hue, ref saturation, ref value);
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
                case EventType.Repaint:
                    Paint(center, radius, inner, square, hue, saturation, value);
                    break;
            }
            return Color.HSVToRGB(hue, saturation, value);
        }

        private static void Apply(Vector2 mouse, Vector2 center, Rect square, ref float hue, ref float saturation, ref float value)
        {
            if (_draggingRing)
            {
                Vector2 offset = mouse - center;
                hue = Mathf.Repeat(Mathf.Atan2(-offset.y, offset.x) * Mathf.Rad2Deg / 360f, 1f);
            }
            else
            {
                saturation = Mathf.Clamp01((mouse.x - square.x) / square.width);
                value = Mathf.Clamp01(1f - (mouse.y - square.y) / square.height);
            }
            GUI.changed = true;
        }

        private static void Paint(Vector2 center, float radius, float inner, Rect square, float hue, float saturation, float value)
        {
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), Ring(), ScaleMode.StretchToFill, true);
            GUI.DrawTexture(square, Square(hue), ScaleMode.StretchToFill, true);
            float angle = hue * 360f * Mathf.Deg2Rad;
            float mid = (radius + inner) * 0.5f;
            DrawCursor(center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * mid);
            DrawCursor(new Vector2(square.x + saturation * square.width, square.y + (1f - value) * square.height));
        }

        private static void DrawCursor(Vector2 position)
        {
            Rect rect = new Rect(position.x - _CURSOR_SIZE * 0.5f, position.y - _CURSOR_SIZE * 0.5f, _CURSOR_SIZE, _CURSOR_SIZE);
            GUI.DrawTexture(rect, Cursor(), ScaleMode.StretchToFill, true);
        }

        private static Texture2D Ring()
        {
            if (_ring != null)
            {
                return _ring;
            }
            _ring = CreateTexture(_RING_TEXELS);
            Color[] pixels = new Color[_RING_TEXELS * _RING_TEXELS];
            float softness = _EDGE_SOFTNESS / (_RING_TEXELS * 0.5f);
            for (int y = 0; y < _RING_TEXELS; y++)
            {
                for (int x = 0; x < _RING_TEXELS; x++)
                {
                    float dx = (x + 0.5f) / _RING_TEXELS * 2f - 1f;
                    float dy = (y + 0.5f) / _RING_TEXELS * 2f - 1f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((distance - (1f - _RING_WIDTH)) / softness) * Mathf.Clamp01((1f - distance) / softness);
                    Color color = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg / 360f, 1f), 1f, 1f);
                    color.a = alpha;
                    pixels[x + y * _RING_TEXELS] = color;
                }
            }
            _ring.SetPixels(pixels);
            _ring.Apply(false);
            return _ring;
        }

        private static Texture2D Square(float hue)
        {
            if (_square != null && Mathf.Abs(hue - _squareHue) < _HUE_CACHE_STEP)
            {
                return _square;
            }
            if (_square == null)
            {
                _square = CreateTexture(_SQUARE_TEXELS);
            }
            Color[] pixels = new Color[_SQUARE_TEXELS * _SQUARE_TEXELS];
            for (int y = 0; y < _SQUARE_TEXELS; y++)
            {
                for (int x = 0; x < _SQUARE_TEXELS; x++)
                {
                    pixels[x + y * _SQUARE_TEXELS] = Color.HSVToRGB(hue, x / (_SQUARE_TEXELS - 1f), y / (_SQUARE_TEXELS - 1f));
                }
            }
            _square.SetPixels(pixels);
            _square.Apply(false);
            _squareHue = hue;
            return _square;
        }

        private static Texture2D Cursor()
        {
            if (_cursor != null)
            {
                return _cursor;
            }
            _cursor = CreateTexture(_CURSOR_TEXELS);
            Color[] pixels = new Color[_CURSOR_TEXELS * _CURSOR_TEXELS];
            float half = _CURSOR_TEXELS * 0.5f;
            for (int y = 0; y < _CURSOR_TEXELS; y++)
            {
                for (int x = 0; x < _CURSOR_TEXELS; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                    float white = Mathf.Clamp01(distance - 4f) * Mathf.Clamp01(6.5f - distance);
                    float outline = Mathf.Clamp01(distance - 3f) * Mathf.Clamp01(7.5f - distance);
                    pixels[x + y * _CURSOR_TEXELS] = new Color(white, white, white, Mathf.Max(white, outline * 0.85f));
                }
            }
            _cursor.SetPixels(pixels);
            _cursor.Apply(false);
            return _cursor;
        }

        private static Texture2D CreateTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
