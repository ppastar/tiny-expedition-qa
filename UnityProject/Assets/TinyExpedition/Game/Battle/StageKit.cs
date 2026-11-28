using System.Collections.Generic;
using UnityEngine;

namespace TinyExpedition.Game.Battle
{
    /// <summary>
    /// 2.5D 무대(전투 BattleStage · 홈 로비 LobbyStage)가 같이 쓰는 부품: 원근 카메라, 배경·바닥·소품·반딧불, 부위 그림 자세.
    /// 같은 코드로 그려서 화면마다 그림체·카메라 느낌이 같다. 규칙·게임 상태와는 무관하다.
    /// </summary>
    public static class StageKit
    {
        public static readonly Vector3 CamBase = new Vector3(0, 3.6f, -12.5f);
        public const float CamPitch = 11f;

        /// <summary>전투 무대의 소품 배치 (x, z, 종류, 크기).</summary>
        public static readonly Vector4[] BattleProps =
        {
            new Vector4(-8.0f, 6.5f, 0, 1.7f), new Vector4(-4.2f, 10f, 1, 1.5f), new Vector4(0.6f, 12f, 3, 1.5f), new Vector4(5.2f, 8.5f, 2, 1.6f),
            new Vector4(9.0f, 6.0f, 3, 1.5f), new Vector4(-10.5f, 3.5f, 2, 1.3f), new Vector4(10.8f, 11f, 0, 2.0f), new Vector4(-1.5f, 14f, 1, 1.8f),
            new Vector4(7.0f, 13.5f, 1, 1.6f), new Vector4(-7.0f, 13f, 0, 2.0f),
        };

        public static int Order(float z) => Mathf.RoundToInt(-z * 20f);

        public static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite, Vector3 pos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = BattleArt.Material;
            sr.sortingOrder = order;
            return sr;
        }

        public static Camera CreateCamera(Transform parent, string name, Color background)
        {
            var camGo = new GameObject(name);
            camGo.transform.SetParent(parent, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.depth = 10; // Main 씬의 기본 카메라 위에 그린다
            return cam;
        }

        /// <summary>화면이 16:9보다 좁으면 양옆이 잘리지 않게 뒤로 물러난 카메라 위치.</summary>
        public static Vector3 CameraPosition(Camera cam, float x)
        {
            float aspect = cam.aspect <= 0 ? 16f / 9f : cam.aspect;
            float back = Mathf.Max(1f, (16f / 9f) / aspect);
            return new Vector3(x, CamBase.y * back, CamBase.z * back);
        }

        /// <summary>배경(하늘·봉우리), 바닥, 깊이가 다른 소품, 앞쪽 잎, 반딧불을 세운다. 테마 id로 색을 고른다.</summary>
        public static BattleArt.Theme BuildScenery(Transform root, string themeId, List<Transform> fireflies, Vector4[] props = null)
        {
            var theme = BattleArt.ThemeFor(themeId);
            var back = MakeSprite(root, "Backdrop", BattleArt.Backdrop(themeId), new Vector3(0, -2.5f, 17f), -6000);
            back.transform.localScale = Vector3.one;

            // 바닥: 무늬를 반복해서 깐 평면. 멀어질수록 안개 색으로.
            var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
            ground.transform.SetParent(root, false);
            var mesh = new Mesh { name = "Ground" };
            float[] zs = { -8f, 3f, 17f };
            var verts = new Vector3[6];
            var uvs = new Vector2[6];
            var cols = new Color[6];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 2; c++)
                {
                    float x = c == 0 ? -26f : 26f;
                    verts[r * 2 + c] = new Vector3(x, 0, zs[r]);
                    uvs[r * 2 + c] = new Vector2(x / 3f, zs[r] / 3f);
                    cols[r * 2 + c] = r == 0 ? Color.white : r == 1 ? Color.Lerp(Color.white, theme.Haze, 0.15f) : Color.Lerp(Color.white, theme.Haze, 0.8f);
                }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5 };
            mesh.RecalculateBounds();
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = ground.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(BattleArt.Material) { mainTexture = BattleArt.Ground(themeId), name = "Ground" };
            mr.sortingOrder = -5000;

            // 깊이가 다른 소품: 카메라가 움직이면 가까운 것이 더 많이 움직여 보인다 (패럴랙스)
            foreach (var pr in props ?? BattleProps)
            {
                var sr = MakeSprite(root, "Prop", BattleArt.PropSprite(themeId, (int)pr.z), new Vector3(pr.x, 0, pr.y), Order(pr.y));
                sr.transform.localScale = Vector3.one * pr.w;
                // 멀수록 안개에 묻힌다
                sr.color = Color.Lerp(Color.white, theme.Haze, Mathf.InverseLerp(4f, 18f, pr.y) * 0.6f);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var fg = MakeSprite(root, "Foreground", BattleArt.Foreground(themeId), new Vector3(side * 6.6f, -0.9f, -4.6f), Order(-4.6f));
                fg.flipX = side > 0;
                fg.transform.localScale = Vector3.one * 1.5f;
            }

            var rng = new System.Random(3); // 연출용 고정 난수 (게임 난수와 무관)
            for (int i = 0; i < 18; i++)
            {
                var pos = new Vector3((float)rng.NextDouble() * 18f - 9f, 0.6f + (float)rng.NextDouble() * 3.6f, (float)rng.NextDouble() * 11f - 1.5f);
                var sr = MakeSprite(root, "Firefly", BattleArt.Dot(), pos, Order(pos.z) + 5);
                sr.color = Paint.Alpha(theme.Glow, 0.9f);
                sr.transform.localScale = Vector3.one * (0.14f + (float)rng.NextDouble() * 0.2f);
                fireflies.Add(sr.transform);
            }
            return theme;
        }

        /// <summary>반딧불이 카메라를 보며 천천히 떠다니게 한다.</summary>
        public static void DriftFireflies(List<Transform> fireflies, Quaternion face, float time)
        {
            for (int i = 0; i < fireflies.Count; i++)
            {
                var f = fireflies[i];
                f.rotation = face;
                f.localPosition += new Vector3(Mathf.Sin(time * 0.7f + i), Mathf.Cos(time * 0.9f + i * 2f), 0) * 0.0025f;
            }
        }

        /// <summary>부위 그림을 축(pivot) 둘레로 돌리고 늘린다. 부위 그림은 전부 발밑 가운데가 원점인 같은 크기라 축만 알면 된다.</summary>
        public static void Pose(SpriteRenderer sr, Vector2 pivot, float angle, Vector2 scale, float lift)
        {
            if (sr.flipX) pivot.x = -pivot.x;
            var rot = Quaternion.Euler(0, 0, angle);
            var moved = rot * new Vector3(pivot.x * scale.x, pivot.y * scale.y, 0);
            sr.transform.localRotation = rot;
            sr.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            sr.transform.localPosition = new Vector3(pivot.x - moved.x, pivot.y - moved.y + lift, 0);
        }

        /// <summary>
        /// 대기 중 부위 움직임: 머리 까딱임(tilt만큼 한 박자 더 기운다), 뒷머리, 날갯짓, 눈 깜빡임.
        /// order는 몸의 정렬 순서이고 부위는 그 앞뒤로 놓는다. eyesShut이면 눈을 감긴다(맞았을 때·기뻐할 때).
        /// </summary>
        public static void IdleParts(BattleArt.UnitArt art, SpriteRenderer body, SpriteRenderer head, SpriteRenderer back, SpriteRenderer wings,
            float time, float phase, float tilt, int order, bool eyesShut)
        {
            float sway = Mathf.Sin(time * 1.7f + phase) * 2.2f + tilt * 0.25f;
            float lift = Mathf.Sin(time * 3f + phase + 0.6f) * 0.012f;
            if (head != null)
            {
                Pose(head, art.Neck, sway, Vector2.one, lift);
                head.sortingOrder = order + 1;
            }
            if (back != null)
            {
                Pose(back, art.Neck, sway * 0.6f, Vector2.one, lift);
                back.sortingOrder = order - 2;
            }
            if (wings != null)
            {
                float flap = (Mathf.Sin(time * 9f + phase) + 1f) * 0.5f;
                Pose(wings, art.Flap, 0f, new Vector2(1f - flap * 0.24f, 1f + flap * 0.07f), 0f);
                wings.sortingOrder = order - 1;
            }
            if (art.Blink != null)
            {
                bool closed = eyesShut || Mathf.Repeat(time + phase * 0.77f, 3.4f) < 0.13f;
                var eyes = head != null ? head : body;
                var want = closed ? art.Blink : head != null ? art.Head : art.Body;
                if (eyes.sprite != want) eyes.sprite = want;
            }
        }
    }
}
