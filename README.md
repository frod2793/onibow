# OniBow: 2D Side-Scrolling Action Game

`OniBow`는 WebGL 및 Android 환경에 최적화된 고성능 2D 횡스크롤 액션 게임입니다.
플레이어는 다양한 스킬과 고난도 이동 기술(대쉬, 점프)을 활용하여 적과 전투를 벌이며 스테이지를 클리어해 나갑니다.

본 프로젝트는 단순한 기능 구현을 넘어, **UniTask를 활용한 비동기 상태 관리 최적화**와 수학적 알고리즘을 통한 **독자적인 물리 효과(베지에 곡선 투사체, 유도 미사일)** 구현에 중점을 두었습니다. 또한, **Jenkins CI/CD 파이프라인**을 구축하여 개발 생산성을 극대화했습니다.

*(플레이 스크린샷 또는 GIF를 이곳에 삽입하세요)*

---

## 📝 목차 (Table of Contents)

*   프로젝트 개요 (Overview)
*   주요 기능 (Key Features)
*   기술적 구현 (Technical Implementation)
*   시스템 아키텍처 (System Architecture)
*   빌드 및 실행 (Build--Run)
*   설치 방법 (Installation)

---

## 📖 프로젝트 개요 (Overview)

| 항목 | 내용 |
| :--- | :--- |
| **Engine** | Unity 2022.3 LTS (2D) |
| **Language** | C# |
| **Platform** | WebGL, Android |
| **Key Tech** | UniTask, DOTween, Jenkins CI/CD, Object Pooling |
| **Period** | 202X.XX ~ 202X.XX (1인 개발) |

---

## ✨ 주요 기능 (Key Features)

### 🎮 Gameplay
*   **Dynamic Action**: 이동, 대쉬(잔상 효과 포함), 자동 공격 및 4종의 액티브 스킬(배리어, 힐, 추적 미사일, 바주카) 구현
*   **Advanced AI**: 플레이어 추적, 거리별 패턴 변경(원거리/근거리), 회피 기동이 가능한 FSM(유한 상태 머신) 기반 적 AI
*   **Combat System**: 타격감을 극대화하는 화면 쉐이크, 플로팅 데미지 텍스트, 체력 경고 비네트(Vignette) 효과

### ⚙️ System & Optimization
*   **Resolution Management**: 다양한 디바이스 대응을 위한 16:9 고정 비율 및 레터박스/필러박스 자동 처리 (WebGL 최적화)
*   **Object Pooling**: 투사체, 이펙트, 데미지 텍스트에 풀링 시스템을 적용하여 가비지 컬렉션(GC) 최소화
*   **Background System**: 쉐이더 기반의 시차 스크롤링(Parallax Scrolling) 및 부드러운 테마 전환

### 🛠 DevOps (CI/CD) & Tools
*   **Automated Build**: Jenkins와 연동 가능한 커맨드 라인 빌드 스크립트로 WebGL/Android 빌드 자동화
*   **Editor Extensions**: 사운드 리소스 이름을 인스펙터에서 드롭다운으로 선택할 수 있는 `SoundNameAttribute` 개발

---

## 💻 기술적 구현 (Technical Implementation)

### 1. 2차 베지에 곡선을 이용한 투사체 제어
물리 엔진(Rigidbody)에 의존하지 않고, 수학적 공식을 통해 투사체의 궤적을 결정론적(Deterministic)으로 제어하여 정확한 타격감을 구현했습니다. `DOTween.To`를 활용하여 시간(t)에 따른 위치를 정밀하게 계산합니다.

<details>
<summary><b>Code: ArrowController.cs (Optimized)</b></summary>

```csharp
/// <summary>
/// 2차 베지에 곡선을 따라 포물선 이동을 처리하는 컨트롤러
/// </summary>
public class ArrowController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("곡선 이동에 소요되는 시간입니다.")]
    private float m_duration = 1.0f;

    [SerializeField]
    [Tooltip("곡선의 휘어짐을 제어하는 포인트입니다.")]
    private Vector3 m_controlPoint;

    private Tween m_moveTween;
    private Vector3 m_previousPosition;

    public void Fire(Vector3 startPos, Vector3 endPos)
    {
        transform.position = startPos;
        m_previousPosition = startPos;
        float t = 0f;

        m_moveTween = DOTween.To(() => t, value =>
        {
            t = value;
            UpdatePositionAndRotation(t, startPos, endPos);
        }, 1f, m_duration).SetEase(Ease.Linear);
    }

    /// <summary>
    /// 베지에 곡선 공식에 따라 위치를 계산하고, 이동 방향으로 회전시킵니다.
    /// </summary>
    private void UpdatePositionAndRotation(float t, Vector3 start, Vector3 end)
    {
        // B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
        Vector3 newPosition = (1 - t) * (1 - t) * start
                            + 2 * (1 - t) * t * m_controlPoint
                            + t * t * end;
        transform.position = newPosition;

        // 부동 소수점 오차를 고려하여 이전 위치와 충분히 다를 때만 방향을 계산합니다.
        if (Vector3.Distance(newPosition, m_previousPosition) > 1e-4f)
        {
            Vector2 direction = (newPosition - m_previousPosition).normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
        m_previousPosition = newPosition;
    }
}
```
</details>

### 2. 행렬(Matrix) 연산을 활용한 잔상 스냅샷 최적화
캐릭터의 잔상을 생성할 때, `Instantiate`의 오버헤드를 줄이고 `lossyScale`로 인한 오차를 해결하기 위해 행렬 연산을 도입했습니다. 원본의 Transform을 잔상 컨테이너의 로컬 좌표계로 역산하여 매핑함으로써 정확하고 빠른 스냅샷을 생성합니다.

<details>
<summary><b>Code: AfterimageSnapshot.cs (Optimized)</b></summary>

```csharp
/// <summary>
/// 행렬 연산을 사용하여 원본 렌더러의 스냅샷을 생성합니다.
/// </summary>
public class AfterimageSnapshot : MonoBehaviour
{
    /// <summary>
    /// 원본 렌더러의 Transform을 스냅샷의 로컬 좌표계로 변환하여 적용합니다.
    /// </summary>
    /// <param name="sourceRenderer">스냅샷을 생성할 원본 렌더러</param>
    /// <param name="partRenderer">스냅샷을 표시할 렌더러</param>
    public void TakeSnapshot(Renderer sourceRenderer, Renderer partRenderer)
    {
        // 원본(World) -> 스냅샷 부모(Local)로 변환하는 행렬을 계산합니다.
        // 이 연산을 통해 부모-자식 관계의 복잡한 Transform 상속을 한 번에 처리할 수 있습니다.
        Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;

        // 계산된 행렬에서 위치, 회전, 크기 정보를 추출하여 적용합니다.
        partRenderer.transform.localPosition = targetMatrix.GetColumn(3);
        partRenderer.transform.localRotation = targetMatrix.rotation;
        partRenderer.transform.localScale = targetMatrix.lossyScale;
    }
}
```
</details>

### 3. UniTask와 CancellationToken을 활용한 안전한 비동기 FSM
UniTask를 사용하여 적(Enemy)의 AI 로직을 비동기 루프로 구현했습니다. CancellationToken을 도입하여 오브젝트 파괴 시 발생할 수 있는 `MissingReferenceException`과 메모리 누수를 원천 차단하고, 안정적인 비동기 상태 머신을 구축했습니다.

<details>
<summary><b>Code: Enemy.cs (Optimized)</b></summary>

```csharp
public class Enemy : MonoBehaviour
{
    private bool m_isDead = false;
    private EnemyState m_currentState;
    private CancellationTokenSource m_cancellationTokenSource;

    private void OnEnable()
    {
        m_cancellationTokenSource = new CancellationTokenSource();
        AI_LoopAsync(m_cancellationTokenSource.Token).Forget();
    }

    private void OnDisable()
    {
        // 오브젝트 비활성화 또는 파괴 시 토큰을 취소하여 모든 비동기 작업을 안전하게 중단합니다.
        m_cancellationTokenSource?.Cancel();
        m_cancellationTokenSource?.Dispose();
    }

    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        // 토큰 취소 요청이 없을 때까지 메인 AI 루프를 실행합니다.
        while (!token.IsCancellationRequested && !m_isDead)
        {
            switch (m_currentState)
            {
                case EnemyState.Idle:
                    await OnIdleStateAsync(token);
                    break;
                case EnemyState.Moving:
                    await OnMovingStateAsync(token);
                    break;
                // ... (기타 상태 처리)
            }
            // 다음 프레임까지 대기하여 Update 루프처럼 동작하게 합니다.
            await UniTask.NextFrame(token);
        }
    }
    // ...
}
```
</details>

### 4. 벡터 연산을 통한 유도 미사일 조향 로직
타겟을 향해 단순히 회전하는 것이 아니라, 수직 벡터(`Vector2.Perpendicular`)와 사인 파(`Mathf.Sin`)를 결합하여 S자 곡선을 그리며 날아가는 자연스러운 유도 미사일 알고리즘을 구현했습니다.

<details>
<summary><b>Code: HomingMissile.cs (Optimized)</b></summary>

```csharp
public class HomingMissile : MonoBehaviour
{
    [SerializeField, Tooltip("파동의 진폭입니다.")]
    private float m_waveAmplitude = 1.5f;
    [SerializeField, Tooltip("파동의 빈도입니다.")]
    private float m_waveFrequency = 2.0f;
    [SerializeField, Tooltip("초당 회전 속도입니다.")]
    private float m_rotateSpeed = 200f;

    private float m_randomStartTime;

    private void Awake()
    {
        m_randomStartTime = Random.Range(0f, 2f * Mathf.PI);
    }

    private void Update()
    {
        // ... (targetPosition, directionToTarget, targetRotation 계산 로직)

        // 타겟 방향에 대한 수직 벡터를 계산하여 S자 곡선의 기준 축으로 사용합니다.
        Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;

        // 사인 파동을 이용해 시간에 따른 오프셋을 계산하여 자연스러운 S자 움직임을 생성합니다.
        float sineOffset = Mathf.Sin((Time.time + m_randomStartTime) * m_waveFrequency) * m_waveAmplitude;

        // 최종 조준 지점을 보정하고, 목표 회전값으로 부드럽게 회전시킵니다.
        Vector2 aimPoint = (Vector2)target.position + perpendicular * sineOffset;
        Vector2 directionToAim = (aimPoint - (Vector2)transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(Vector3.forward, directionToAim);
        
        // RotateTowards는 이미 프레임 속도에 독립적이므로 deltaTime을 곱하지 않습니다.
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, m_rotateSpeed * Time.deltaTime);
    }
}
```
</details>

---

## 📂 시스템 아키텍처 (System Architecture)

| Script | Description |
|:---|:---|
| **PlayerControl.cs** | UniTask 기반 비동기 이동/공격 루프, 벽/절벽 감지 알고리즘이 포함된 대쉬 로직 |
| **Enemy.cs** | 거리 기반 상태 전환, 회피 기동 확률 연산, 비동기 스킬 시전 등 AI 핵심 로직 |
| **SkillManager.cs** | 배리어, 힐, 유도 미사일, 바주카 등 플레이어/적 스킬의 쿨타임 및 실행 관리 |
| **EffectManager.cs** | 데미지 텍스트(Floating Text) 및 파티클 이펙트의 생성/파괴를 관리하는 중앙 매니저 |
| **SoundManager.cs** | BGM/SFX 풀링 시스템, 볼륨 페이드 인/아웃(DOTween), 음소거 설정 관리 |
| **UIManager.cs** | 플레이어 체력/쿨타임 UI 동기화, InputSystem 키 입력 처리, 설정 팝업 제어 |
| **SoundNameDrawer.cs**| 사운드 리소스 이름을 인스펙터에서 드롭다운으로 선택 가능하게 하는 에디터 툴 |

---

## 🚀 빌드 및 실행 (Build & Run)

이 프로젝트는 Jenkins 파이프라인을 통해 WebGL 및 Android 빌드를 자동화할 수 있도록 `BuildScript.cs`를 구성하였습니다.

### Command Line Build Usage

#### 1. WebGL Clean Build
```bash
/path/to/Unity -quit -batchmode \
-projectPath . \
-executeMethod BuildScript.PerformBuild \
-buildTarget WebGL \
-outputPath "Builds/WebGL" \
-cleanBuild \
-logFile "build_webgl.log"
```

#### 2. Android AAB Build (Production)
*Jenkins Credential Binding을 통해 키스토어 정보를 안전하게 주입합니다.*
```bash
/path/to/Unity -quit -batchmode \
-projectPath . \
-executeMethod BuildScript.PerformBuild \
-buildTarget Android \
-androidBuildType AAB \
-outputPath "Builds/Android/OniBow.aab" \
-cleanBuild \
-logFile "build_android.log"
```

<details>
<summary><b>Example: BuildScript.cs</b></summary>

```csharp
using UnityEditor;
using System;
using System.Linq;

public class BuildScript
{
    public static void PerformBuild()
    {
        var args = Environment.GetCommandLineArgs();
        
        // 커맨드 라인 인자에서 빌드 옵션 파싱
        string buildTarget = GetArgumentValue(args, "-buildTarget");
        string outputPath = GetArgumentValue(args, "-outputPath");
        bool isCleanBuild = args.Contains("-cleanBuild");
        
        // ... (androidBuildType 등 추가 인자 파싱)

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        buildPlayerOptions.locationPathName = outputPath;
        buildPlayerOptions.target = (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTarget);
        
        BuildOptions options = isCleanBuild ? BuildOptions.CleanBuildCache : BuildOptions.None;
        buildPlayerOptions.options = options;

        // 빌드 실행
        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    private static string GetArgumentValue(string[] args, string argName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == argName)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
```
</details>

---

## 📦 설치 방법 (Installation)

*   **Web**: [Itch.io/OniBow 링크] (브라우저에서 즉시 플레이 가능)
*   **Android**: `Builds/Android/OniBow.apk` 파일을 다운로드하여 안드로이드 기기에 설치

---

Copyright © 2025 [Your Name/Organization]. All rights reserved.
