using System.IO;
using System.Linq;
using TinyExpedition.Core.Data;
using TinyExpedition.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TinyExpedition.EditorTools
{
    /// <summary>에디터 메뉴와 빌드 훅. 기획 데이터가 깨진 채로 빌드되는 걸 막는다.</summary>
    public static class QaEditorTools
    {
        public const string ScenePath = "Assets/TinyExpedition/Main.unity";
        private static string DataDir => Path.Combine(Application.streamingAssetsPath, "GameData");
        private static string SkinDir => Path.Combine(Application.streamingAssetsPath, "Skins");

        [MenuItem("Tiny Expedition/데이터 검사")]
        public static void ValidateDataMenu()
        {
            var issues = Validate(out var db);
            foreach (var i in issues)
            {
                if (i.Level == IssueLevel.Error) Debug.LogError(i);
                else Debug.LogWarning(i);
            }
            var errors = issues.Count(i => i.Level == IssueLevel.Error);
            EditorUtility.DisplayDialog("데이터 검사",
                errors == 0 ? "오류 없음 (경고 " + issues.Count + "건)" : "오류 " + errors + "건 — Console 창을 확인하세요.", "확인");
        }

        [MenuItem("Tiny Expedition/그림 리소스 누락 검사")]
        public static void SkinCheckMenu()
        {
            Validate(out var db);
            var missing = db.Characters.Where(c => !File.Exists(SkinLibrary.FileFor(SkinDir, c.Id))).ToList();
            foreach (var c in missing) Debug.LogWarning("그림 없음: " + c.Id + " (" + c.Name + ") → " + SkinLibrary.FileFor(SkinDir, c.Id));
            EditorUtility.DisplayDialog("그림 리소스", missing.Count == 0 ? "모든 캐릭터 그림이 있습니다." :
                missing.Count + "/" + db.Characters.Count + "명 그림 없음 (기본 카드로 표시됨). Console 확인.", "확인");
        }

        [MenuItem("Tiny Expedition/Main 씬 만들기")]
        public static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = UiKit.Bg;
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Main 씬을 만들고 빌드 목록에 넣었습니다: " + ScenePath);
        }

        /// <summary>CI용: -executeMethod TinyExpedition.EditorTools.QaEditorTools.ValidateDataBatch</summary>
        public static void ValidateDataBatch()
        {
            var issues = Validate(out _);
            foreach (var i in issues) Debug.Log(i);
            EditorApplication.Exit(issues.Any(i => i.Level == IssueLevel.Error) ? 1 : 0);
        }

        /// <summary>CI용: -executeMethod TinyExpedition.EditorTools.QaEditorTools.BuildWindows</summary>
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Build/Windows/TinyExpedition.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static System.Collections.Generic.List<DataIssue> Validate(out GameDatabase db)
        {
            var dir = DataDir;
            db = GameDatabase.Load(f => File.ReadAllText(Path.Combine(dir, f)));
            return DataValidator.Validate(db);
        }

        /// <summary>빌드 직전에 데이터 검사. 오류가 있으면 빌드 실패.</summary>
        private sealed class PreBuildDataCheck : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                var errors = Validate(out _).Where(i => i.Level == IssueLevel.Error).ToList();
                if (errors.Count > 0)
                    throw new BuildFailedException("기획 데이터 오류 " + errors.Count + "건:\n" + string.Join("\n", errors));
            }
        }
    }
}
