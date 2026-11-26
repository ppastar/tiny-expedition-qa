using System;
using UnityEngine;

namespace TinyExpedition.Game.Battle
{
    /// <summary>
    /// 코드로 그림을 그리는 작은 캔버스. 원·타원·둥근 사각형·선·다각형을 거리 함수로 칠해 가장자리가 부드럽다.
    /// 좌표는 픽셀, 원점은 왼쪽 아래(Texture2D와 같음). 외부 그림 파일 없이 캐릭터·배경·이펙트를 만든다.
    /// </summary>
    internal sealed class Paint
    {
        public readonly int W, H;
        private readonly Color[] _px;

        public Paint(int w, int h)
        {
            W = w; H = h;
            _px = new Color[w * h];
        }

        public static Color Hex(uint rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        public static Color Dark(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public static Color Light(Color c, float k) => new Color(c.r + (1 - c.r) * k, c.g + (1 - c.g) * k, c.b + (1 - c.b) * k, c.a);
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private void Put(int x, int y, Color c, float coverage)
        {
            float a = coverage * c.a;
            if (a <= 0.002f) return;
            if (a > 1f) a = 1f;
            int i = y * W + x;
            var d = _px[i];
            float outA = a + d.a * (1 - a);
            float k = d.a * (1 - a);
            _px[i] = new Color((c.r * a + d.r * k) / outA, (c.g * a + d.g * k) / outA, (c.b * a + d.b * k) / outA, outA);
        }

        /// <summary>sdf(x, y) = 도형 경계까지의 거리(안쪽이 음수). 경계 1픽셀을 부드럽게 칠한다.</summary>
        private void Fill(float minX, float minY, float maxX, float maxY, Color c, Func<float, float, float> sdf)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX) - 1), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY) - 1), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = sdf(x + 0.5f, y + 0.5f);
                    if (d < 0.5f) Put(x, y, c, Mathf.Clamp01(0.5f - d));
                }
        }

        public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

        public void Ellipse(float cx, float cy, float rx, float ry, Color c, float rotDeg = 0f)
        {
            float cos = Mathf.Cos(rotDeg * Mathf.Deg2Rad), sin = Mathf.Sin(rotDeg * Mathf.Deg2Rad);
            float m = Mathf.Max(rx, ry), small = Mathf.Min(rx, ry);
            Fill(cx - m, cy - m, cx + m, cy + m, c, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float lx = (dx * cos + dy * sin) / rx, ly = (-dx * sin + dy * cos) / ry;
                return (Mathf.Sqrt(lx * lx + ly * ly) - 1f) * small;
            });
        }

        /// <summary>타원 테두리만.</summary>
        public void Ring(float cx, float cy, float rx, float ry, float thickness, Color c)
        {
            float m = Mathf.Max(rx, ry) + thickness, small = Mathf.Min(rx, ry);
            Fill(cx - m, cy - m, cx + m, cy + m, c, (x, y) =>
            {
                float lx = (x - cx) / rx, ly = (y - cy) / ry;
                return Mathf.Abs((Mathf.Sqrt(lx * lx + ly * ly) - 1f) * small) - thickness * 0.5f;
            });
        }

        public void RoundRect(float cx, float cy, float hw, float hh, float r, Color c, float rotDeg = 0f)
        {
            float cos = Mathf.Cos(rotDeg * Mathf.Deg2Rad), sin = Mathf.Sin(rotDeg * Mathf.Deg2Rad);
            float m = Mathf.Sqrt(hw * hw + hh * hh);
            r = Mathf.Min(r, Mathf.Min(hw, hh));
            Fill(cx - m, cy - m, cx + m, cy + m, c, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float qx = Mathf.Abs(dx * cos + dy * sin) - (hw - r), qy = Mathf.Abs(-dx * sin + dy * cos) - (hh - r);
                float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            });
        }

        /// <summary>양 끝이 둥근 선.</summary>
        public void Line(float x0, float y0, float x1, float y1, float width, Color c)
        {
            float r = width * 0.5f, vx = x1 - x0, vy = y1 - y0, len2 = Mathf.Max(vx * vx + vy * vy, 0.0001f);
            Fill(Mathf.Min(x0, x1) - r, Mathf.Min(y0, y1) - r, Mathf.Max(x0, x1) + r, Mathf.Max(y0, y1) + r, c, (x, y) =>
            {
                float t = Mathf.Clamp01(((x - x0) * vx + (y - y0) * vy) / len2);
                float px = x - (x0 + vx * t), py = y - (y0 + vy * t);
                return Mathf.Sqrt(px * px + py * py) - r;
            });
        }

        /// <summary>점을 이은 굵은 선 (x0, y0, x1, y1, ...).</summary>
        public void Path(float width, Color c, params float[] xy)
        {
            for (int i = 0; i + 3 < xy.Length; i += 2) Line(xy[i], xy[i + 1], xy[i + 2], xy[i + 3], width, c);
        }

        /// <summary>볼록 다각형 (x0, y0, x1, y1, ...). 점 순서 방향은 상관없다.</summary>
        public void Poly(Color c, params float[] xy)
        {
            int n = xy.Length / 2;
            float area = 0, minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += xy[i * 2] * xy[j * 2 + 1] - xy[j * 2] * xy[i * 2 + 1];
                minX = Mathf.Min(minX, xy[i * 2]); maxX = Mathf.Max(maxX, xy[i * 2]);
                minY = Mathf.Min(minY, xy[i * 2 + 1]); maxY = Mathf.Max(maxY, xy[i * 2 + 1]);
            }
            float sign = area >= 0 ? 1f : -1f;
            Fill(minX, minY, maxX, maxY, c, (x, y) =>
            {
                float d = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float ex = xy[j * 2] - xy[i * 2], ey = xy[j * 2 + 1] - xy[i * 2 + 1];
                    float len = Mathf.Sqrt(ex * ex + ey * ey);
                    if (len < 0.0001f) continue;
                    // 변의 바깥쪽 법선 방향 거리 가운데 가장 큰 값
                    float dist = ((x - xy[i * 2]) * ey - (y - xy[i * 2 + 1]) * ex) / len * sign;
                    if (dist > d) d = dist;
                }
                return d;
            });
        }

        /// <summary>가운데가 밝고 가장자리로 갈수록 사라지는 빛.</summary>
        public void Glow(float cx, float cy, float r, Color c, float power = 2f)
        {
            int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(W - 1, (int)(cx + r));
            int y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(H - 1, (int)(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float t = 1f - Mathf.Sqrt(dx * dx + dy * dy) / r;
                    if (t > 0) Put(x, y, c, Mathf.Pow(t, power));
                }
        }

        /// <summary>위아래 그라데이션으로 전체를 채운다.</summary>
        public void VGradient(Color bottom, Color top)
        {
            for (int y = 0; y < H; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(H - 1));
                for (int x = 0; x < W; x++) _px[y * W + x] = c;
            }
        }

        public void FillAll(Color c)
        {
            for (int i = 0; i < _px.Length; i++) _px[i] = c;
        }

        /// <summary>그려진 모양 둘레에 외곽선을 두른다 (스티커 느낌).</summary>
        public void Outline(int radius, Color c)
        {
            var a = new float[_px.Length];
            var tmp = new float[_px.Length];
            for (int i = 0; i < a.Length; i++) a[i] = _px[i].a;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float m = 0;
                    for (int k = Mathf.Max(0, x - radius); k <= Mathf.Min(W - 1, x + radius); k++) m = Mathf.Max(m, a[y * W + k]);
                    tmp[y * W + x] = m;
                }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float m = 0;
                    for (int k = Mathf.Max(0, y - radius); k <= Mathf.Min(H - 1, y + radius); k++) m = Mathf.Max(m, tmp[k * W + x]);
                    var p = _px[y * W + x];
                    if (m <= 0.002f) { _px[y * W + x] = new Color(c.r, c.g, c.b, 0); continue; }
                    float outA = Mathf.Max(m, p.a);
                    // 원래 그림을 외곽선 색 위에 올린다
                    _px[y * W + x] = new Color(p.r * p.a + c.r * (1 - p.a), p.g * p.a + c.g * (1 - p.a), p.b * p.a + c.b * (1 - p.a), outA);
                }
        }

        public Texture2D ToTexture(bool repeat = false)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            tex.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels(_px);
            tex.Apply(true, true);
            return tex;
        }

        /// <param name="pivotY">0 = 아래쪽(발), 0.5 = 가운데.</param>
        public Sprite ToSprite(float pixelsPerUnit, float pivotY = 0f)
        {
            var tex = ToTexture();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, pivotY), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
