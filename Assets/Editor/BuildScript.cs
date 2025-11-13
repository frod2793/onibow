using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Jenkins와 같은 CI/CD 환경에서 커맨드 라인을 통해 Unity 프로젝트를 빌드하기 위한 스크립트입니다.
/// </summary>
public class BuildScript
{
    /// <summary>
    /// Jenkins에서 호출할 메인 빌드 메서드입니다.
    /// Unity Editor를 커맨드 라인으로 실행할 때 -executeMethod BuildScript.PerformBuild 인자를 사용합니다.
    /// 추가 인자:
    /// - -buildTarget [BuildTarget] (예: WebGL, StandaloneWindows64)
    /// - -outputPath [path] (예: Builds/WebGL, Builds/Windows/OniBow.exe, Builds/Android/OniBow.aab)
    /// - -androidBuildType [APK/AAB] (Android 빌드 시에만 사용)
    /// - -cleanBuild (이 플래그가 있으면 빌드 전 출력 폴더를 삭제합니다)
    /// </summary>
    public static void PerformBuild()
    {
        Debug.Log("========== BuildScript.PerformBuild 시작 ==========");
        var args = Environment.GetCommandLineArgs();

        string buildTargetStr = GetArgument(args, "-buildTarget");
        string androidBuildType = GetArgument(args, "-androidBuildType");
        string outputPath = GetArgument(args, "-outputPath");
        bool cleanBuild = args.Any(arg => arg.Equals("-cleanBuild", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(buildTargetStr))
        {
            Debug.LogError("빌드 타겟이 지정되지 않았습니다. -buildTarget 인자를 확인해주세요. (예: WebGL, StandaloneWindows64)");
            EditorApplication.Exit(1);
            return;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            Debug.LogError("빌드 결과물 경로가 지정되지 않았습니다. -outputPath 인자를 확인해주세요.");
            EditorApplication.Exit(1);
            return;
        }

        if (!Enum.TryParse(buildTargetStr, out BuildTarget buildTarget))
        {
            Debug.LogError($"잘못된 빌드 타겟입니다: {buildTargetStr}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"빌드 타겟: {buildTarget}");
        Debug.Log($"결과물 경로: {outputPath}");

        // 안드로이드 빌드 시, 추가 설정 적용
        if (buildTarget == BuildTarget.Android)
        {
            SetupAndroidBuildSettings(androidBuildType);
        }

        BuildPlayer(buildTarget, outputPath, cleanBuild);
    }

    /// <summary>
    /// 지정된 타겟과 경로로 플레이어를 빌드하는 공용 메서드입니다.
    /// </summary>
    private static void BuildPlayer(BuildTarget buildTarget, string outputPath, bool cleanBuild)
    {
        Debug.Log($"========== {buildTarget} 빌드 시작 ==========");
        
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("빌드할 씬이 없습니다. Build Settings를 확인해주세요.");
            EditorApplication.Exit(1);
            return;
        }

        // WebGL은 outputPath 자체가 디렉토리이고, 다른 플랫폼은 outputPath의 부모가 디렉토리입니다.
        string outputDirectory = buildTarget == BuildTarget.WebGL ? outputPath : Path.GetDirectoryName(outputPath);

        if (cleanBuild)
        {
            if (Directory.Exists(outputDirectory))
            {
                Debug.Log($"클린 빌드 옵션이 활성화되었습니다. 출력 디렉토리를 삭제합니다: {outputDirectory}");
                try
                {
                    Directory.Delete(outputDirectory, true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"출력 디렉토리 삭제 중 오류 발생: {e.Message}");
                    EditorApplication.Exit(1);
                    return;
                }
            }
        }

        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Debug.Log($"출력 디렉토리를 생성합니다: {outputDirectory}");
            Directory.CreateDirectory(outputDirectory);
        }

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = buildTarget,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        Debug.Log($"========== 빌드 결과: {summary.result} ==========");

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"빌드 성공: {summary.totalSize / 1024 / 1024} MB, 소요 시간: {summary.totalTime.TotalSeconds:F2} 초");
            EditorApplication.Exit(0); // 성공 코드
        }
        else
        {
            Debug.LogError($"빌드 실패: {summary.totalErrors} 개의 에러 발생");
            EditorApplication.Exit(1); // 실패 코드
        }
    }

    /// <summary>
    /// Android 빌드를 위한 키스토어 및 빌드 타입을 설정합니다.
    /// Jenkins와 같은 CI 환경에서는 환경 변수를 통해 보안 정보를 전달하는 것이 안전합니다.
    /// </summary>
    private static void SetupAndroidBuildSettings(string buildType)
    {
        Debug.Log("========== Android 빌드 설정 시작 ==========");

        // 환경 변수에서 키스토어 정보 읽어오기
        // Jenkins Credential Binding에서 아래 이름으로 변수를 설정해야 합니다.
        PlayerSettings.Android.keystoreName = Environment.GetEnvironmentVariable("UNITY_KEYSTORE_NAME");
        PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("UNITY_KEYSTORE_PASS");
        PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("UNITY_KEYALIAS_NAME");
        PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("UNITY_KEYALIAS_PASS");

        if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
        {
            Debug.LogWarning("키스토어 정보가 설정되지 않았습니다. Debug Keystore로 빌드될 수 있습니다.");
        }

        // 빌드 타입 설정 (AAB 또는 APK)
        EditorUserBuildSettings.buildAppBundle = "AAB".Equals(buildType, StringComparison.OrdinalIgnoreCase);
        Debug.Log($"Android 빌드 타입: {(EditorUserBuildSettings.buildAppBundle ? "AAB" : "APK")}");
    }

    /// <summary>
    /// 커맨드 라인 인자에서 특정 키에 해당하는 값을 추출합니다.
    /// </summary>
    private static string GetArgument(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}