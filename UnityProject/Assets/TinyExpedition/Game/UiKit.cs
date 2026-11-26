using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 코드로 uGUI를 만드는 작은 도우미. 씬·프리팹 YAML 없이 화면을 구성해
    /// 리뷰하기 쉽고, 테스트가 이름(GameObject.name)으로 요소를 찾을 수 있게 한다.
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // 한글 글리프가 있는 OS 폰트를 우선 쓰고, 없으면 내장 폰트로.
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial" }, 32);
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        public static readonly Color Bg = new Color32(0x1b, 0x1e, 0x2b, 0xff);
        public static readonly Color Panel = new Color32(0x26, 0x2a, 0x3b, 0xff);
        public static readonly Color Accent = new Color32(0x8b, 0x6c, 0xff, 0xff);
        public static readonly Color Gold = new Color32(0xff, 0xc8, 0x57, 0xff);
        public static readonly Color TextColor = new Color32(0xf2, 0xf2, 0xf5, 0xff);
        public static readonly Color Muted = new Color32(0x9a, 0xa0, 0xb4, 0xff);

        public static Canvas CreateCanvas(Transform parent)
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 가로 화면 기준. 세로 길이에 맞춰 늘려서 화면 비율이 달라도 위아래가 잘리지 않는다.
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(parent, false);
            }
            return canvas;
        }

        public static RectTransform Panel_(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        public static RectTransform Column(string name, Transform parent, float spacing = 14, int padding = 28)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        public static RectTransform Row(string name, Transform parent, float spacing = 16, float height = 104)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            go.GetComponent<LayoutElement>().flexibleHeight = 0; // 남는 세로 공간을 줄이 가져가지 않게 (가져가면 화면 내용이 아래로 밀린다)
            return (RectTransform)go.transform;
        }

        public static Text Label(string name, Transform parent, string text, int size = 44, Color? color = null,
            TextAnchor align = TextAnchor.MiddleCenter, float height = -1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = height > 0 ? height : size * 1.6f;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, UnityAction onClick, Color? color = null, float height = 104)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color ?? Accent;
            Round(go.GetComponent<Image>());
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var b = go.GetComponent<UnityEngine.UI.Button>();
            b.onClick.AddListener(onClick);
            var label = Label("Text", go.transform, text, 46);
            Stretch((RectTransform)label.transform);
            return b;
        }

        private static Sprite _rounded;

        /// <summary>모서리가 둥근 9분할 그림을 입힌다 (그림 파일 없이 코드로 만든다).</summary>
        public static void Round(Image image)
        {
            if (_rounded == null)
            {
                const int size = 64, r = 22;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Rounded", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float qx = Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - r), qy = Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - r);
                        float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude - r;
                        px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
                    }
                tex.SetPixels(px);
                tex.Apply();
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
                _rounded.name = "Rounded";
            }
            image.sprite = _rounded;
            image.type = Image.Type.Sliced;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }
    }
}
