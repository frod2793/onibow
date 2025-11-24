# OniBow: 2D Side-Scrolling Action Game

`OniBow`는 WebGL 및 Android 환경에 최적화된 고성능 2D 횡스크롤 액션 게임입니다.
플레이어는 다양한 스킬과 고난도 이동 기술(대쉬, 점프)을 활용하여 적과 전투를 벌이며 클리어해 나갑니다.

본 프로젝트는 단순한 기능 구현을 넘어, **UniTask를 활용한 비동기 상태 관리 최적화**와 수학적 알고리즘을 통한 독자적인 물리 효과(베지에 곡선 투사체, 유도 미사일) 구현에 중점을 두었습니다. 또한, Jenkins CI/CD 파이프라인을 구축하여 개발 생산성을 극대화했습니다.


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
| **Engine** | Unity 6 |
| **Language** | C# |
| **Platform** | WebGL, Android |
| **Key Tech** | UniTask, DOTween, Jenkins CI/CD, Object Pooling |

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
물리 엔진(Rigidbody)에 의존하지 않고, 수학적 공식을 통해 투사체의 궤적을 결정론적(Deterministic)으로 제어하여 정확한 타격감을 구현했습니다. `DOTween.To`를 활용하여 시간(t)에 따른 위치를 정밀하게 계산하고, 이동 방향에 맞춰 자연스럽게 회전시킵니다.

<details>
<summary><b>Code: ArrowController.cs</b></summary>

```csharp
/// <summary>
/// 화살의 포물선 이동과 생명 주기를 관리합니다.
/// </summary>
public class ArrowController : MonoBehaviour
{
    public enum ArrowOwner { Player, Enemy }
    public ArrowOwner Owner { get; set; }

    private Tween _moveTween;

    /// <summary>
    /// 지정된 궤적을 따라 화살을 발사합니다. (포물선)
    /// </summary>
    public void Launch(Vector3 startPos, Vector3 controlPoint, Vector3 endPos, float duration)
    {
        _moveTween?.Kill();

        float t = 0f;
        Vector3 previousPos = startPos;
        transform.position = startPos;

        _moveTween = DOTween.To(() => t, x =>
        {
            t = x;
            if (this == null || !gameObject.activeInHierarchy) return;

            // B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            Vector3 newPos = (1 - t) * (1 - t) * startPos + 2 * (1 - t) * t * controlPoint + t * t * endPos;
            transform.position = newPos;

            if (newPos != previousPos)
            {
                Vector2 dir = (newPos - previousPos).normalized;
                if (dir != Vector2.zero)
                {
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0, 0, angle);
                }
            }
            previousPos = newPos;
        }, 1f, duration)
        .SetEase(Ease.Linear)
        .OnComplete(ReturnToPool);
    }

    private void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null && gameObject.activeInHierarchy)
        {
            ObjectPoolManager.Instance.Return(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        _moveTween?.Kill();
    }
}
```
</details>

### 2. 행렬(Matrix) 연산을 활용한 잔상 스냅샷 최적화
캐릭터의 잔상을 생성할 때, `Instantiate`의 오버헤드를 줄이고 `lossyScale`로 인한 오차를 해결하기 위해 행렬 연산을 도입했습니다. 원본의 Transform을 잔상 컨테이너의 로컬 좌표계로 역산하여 매핑함으로써, 복잡한 부모-자식 계층 구조에 관계없이 정확하고 빠른 스냅샷을 생성합니다.

<details>
<summary><b>Code: AfterimageSnapshot.cs</b></summary>

```csharp
/// <summary>
/// 잔상 '스냅샷'의 생명 주기를 관리합니다.
/// </summary>
public class AfterimageSnapshot : MonoBehaviour
{
    private readonly List<SpriteRenderer> _partRenderers = new List<SpriteRenderer>();

    private void Awake()
    {
        GetComponentsInChildren(true, _partRenderers);
    }

    /// <summary>
    /// 원본 렌더러들의 상태를 복제하여 스냅샷을 활성화하고, 사라짐 효과를 시작합니다.
    /// </summary>
    public void Activate(List<SpriteRenderer> sourceRenderers, Color color, float fadeDuration, bool overrideSorting, int sortingOrderOverride)
    {
        // ... (트윈 정리 및 루프 준비)

        for (int i = 0; i < sourceRenderers.Count; i++)
        {
            // ... (렌더러 준비 및 활성화 로직)
            
            var sourceRenderer = sourceRenderers[i];
            if (sourceRenderer.gameObject.activeInHierarchy && sourceRenderer.sprite != null)
            {
                // ... (스프라이트, 정렬 순서 등 속성 복사)

                // [핵심 로직]
                // Matrix 연산을 통해 원본 렌더러의 모든 Transform 속성(위치, 회전, 크기)을
                // 스냅샷 컨테이너(부모)에 상대적인 로컬 Transform으로 정확하게 변환합니다.
                Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                partRenderer.transform.localPosition = targetMatrix.GetColumn(3);
                partRenderer.transform.localRotation = targetMatrix.rotation;
                partRenderer.transform.localScale = targetMatrix.lossyScale;

                // 페이드 아웃 트윈 시작
                partRenderer.color = new Color(color.r, color.g, color.b, 1f);
                partRenderer.DOFade(0, fadeDuration).SetEase(Ease.InQuad)
                    .OnComplete(ReturnToPool); // 마지막 트윈에만 연결하여 중복 호출 방지
            }
            // ...
        }
    }

    private void ReturnToPool()
    {
        // ... (오브젝트 풀 반환 로직)
    }
}
```
</details>

### 3. UniTask와 CancellationToken을 활용한 안전한 비동기 FSM
UniTask를 사용하여 적(Enemy)의 AI 로직을 비동기 루프로 구현했습니다. `CancellationToken`을 도입하여 피격, 회피, 사망 등 상태가 급격히 변할 때 기존 비동기 작업을 안전하게 취소하고 새로운 상태로 전환합니다. 이를 통해 `MissingReferenceException`과 메모리 누수를 원천 차단하고, 안정적인 비동기 상태 머신을 구축했습니다.

<details>
<summary><b>Code: Enemy.cs</b></summary>

```csharp
public class Enemy : MonoBehaviour
{
    private CancellationTokenSource m_aiTaskCts;
    private Rigidbody2D m_rigidbody2D;
    private bool m_isDead;
    public EnemyState CurrentState { get; private set; }

    void Start()
    {
        // ...
        m_aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(m_aiTaskCts.Token).Forget();
    }

    private void OnDestroy()
    {
        m_aiTaskCts?.Cancel();
        m_aiTaskCts?.Dispose();
    }

    public async void TakeDamage(int damage)
    {
        if (m_isDead || CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged) { return; }

        // ... (회피 로직)

        // ... (체력 감소 및 UI 업데이트)

        if (m_currentHp <= 0)
        {
            Die();
        }
        else
        {
            // 피격 애니메이션 재생 (기존 AI 루프는 취소됨)
            PlayDamagedAnimationAsync().Forget();
        }
    }

    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !m_isDead) // 루프 시작
        {
            switch (CurrentState)
            {
                case EnemyState.Idle:
                    await OnIdleStateAsync(token);
                    break;
                case EnemyState.Moving:
                    await OnMovingStateAsync(token);
                    break;
                // ... (기타 상태 처리)
            }
            // 상태에 따른 비동기 작업이 끝난 후 다음 프레임까지 대기
            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token).SuppressCancellationThrow();
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (m_isDead || m_enemyAnimation == null) { return; }

        // 현재 진행 중인 AI 행동(이동, 공격 등)을 즉시 취소
        m_aiTaskCts?.Cancel();
        SetState(EnemyState.Damaged);
        m_rigidbody2D.linearVelocity = Vector2.zero;
        
        var damagedClip = m_enemyAnimation.DAMAGED_List.Count > 0 ? m_enemyAnimation.DAMAGED_List[0] : null;
        if (damagedClip != null)
        {
            try
            {
                // 애니메이션 길이만큼 대기. 이 시간 동안 다른 AI 로직은 정지됨.
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClip.length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException)
            {
                return; // 오브젝트 파괴 시 예외 처리
            }
        }

        // 피격 애니메이션이 끝난 후, 사망 상태가 아니라면 새로운 CancellationToken으로 AI 루프를 다시 시작
        if (!m_isDead) 
        {
            SetState(EnemyState.Idle);
            m_aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(m_aiTaskCts.Token).Forget();
        }
    }
    // ...
}
```
</details>

### 4. 물리 기반 유도 미사일 조향 로직
`Rigidbody2D`를 사용하여 미사일의 이동을 처리하되, 조향 로직은 수학적 계산을 통해 구현했습니다. 타겟 방향 벡터에 수직인 벡터(`Vector2.Perpendicular`)와 사인 파(`Mathf.Sin`)를 결합하여, S자 곡선을 그리며 날아가는 자연스러운 유도 알고리즘을 만들었습니다. `FixedUpdate`에서 물리 연산을 처리하여 안정적인 움직임을 보장합니다.

<details>
<summary><b>Code: HomingMissile.cs</b></summary>

```csharp
[RequireComponent(typeof(Rigidbody2D))]
public class HomingMissile : MonoBehaviour
{
    [SerializeField] private float speed = 4f;
    [SerializeField] private float rotateSpeed = 200f;
    [SerializeField] private float waveFrequency = 2f;
    [SerializeField] private float waveAmplitude = 1.5f;

    private Transform _target;
    private Rigidbody2D _rigidbody2D;
    private bool _isHoming = false;

    public void Launch(Transform target, Transform firePoint)
    {
        // ... (초기 위치/회전 설정)
        _target = target;

        // DOTween 시퀀스로 초기 발사 애니메이션 구현 (예: 위로 솟구치는 움직임)
        Sequence launchSequence = DOTween.Sequence();
        launchSequence.Append(transform.DOMoveY(transform.position.y + 1.5f, 0.3f).SetEase(Ease.OutSine));
        launchSequence.OnComplete(() => {
            _isHoming = true; // 시퀀스 완료 후 추적 시작
        });
    }

    private void FixedUpdate()
    {
        if (!_isHoming || _target == null) return;
        
        HandleHoming();
    }

    private void HandleHoming()
    {
        Vector2 currentPosition = _rigidbody2D.position;
        Vector2 targetPosition = _target.position;
        
        // 타겟 방향에 대한 수직 벡터를 계산하여 S자 곡선의 기준 축으로 사용
        Vector2 directionToTarget = targetPosition - currentPosition;
        Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;

        // 사인 파동을 이용해 시간에 따른 오프셋을 계산하여 자연스러운 S자 움직임을 생성
        float sineOffset = Mathf.Sin(Time.time * waveFrequency) * waveAmplitude;

        // 최종 조준 지점을 보정하고, 목표 회전값으로 부드럽게 회전
        Vector2 aimPoint = targetPosition + perpendicular * sineOffset;
        Vector2 finalDirection = (aimPoint - currentPosition).normalized;
        float targetAngle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);
        
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);
        
        // 최종적으로 계산된 방향으로 Rigidbody를 이동
        _rigidbody2D.MovePosition(currentPosition + (Vector2)transform.right * speed * Time.fixedDeltaTime);
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

이 프로젝트는 Jenkins 파이프라인을 통해 WebGL 및 Android 빌드를 자동화할 수 있도록 `BuildScript.cs`를 구성하였습니다. 커맨드 라인 인자를 통해 빌드 타겟, 출력 경로, 빌드 옵션 등을 동적으로 제어할 수 있습니다.

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
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// CI/CD 환경에서 커맨드 라인을 통해 Unity 프로젝트를 빌드하기 위한 스크립트입니다.
/// </summary>
public class BuildScript
{
    public static void PerformBuild()
    {
        var args = Environment.GetCommandLineArgs();
        
        // 커맨드 라인 인자에서 빌드 옵션 파싱
        string buildTargetStr = GetArgument(args, "-buildTarget");
        string outputPath = GetArgument(args, "-outputPath");
        bool cleanBuild = args.Any(arg => arg.Equals("-cleanBuild", StringComparison.OrdinalIgnoreCase));
        
        if (!Enum.TryParse(buildTargetStr, out BuildTarget buildTarget))
        {
            Debug.LogError($"잘못된 빌드 타겟입니다: {buildTargetStr}");
            EditorApplication.Exit(1);
            return;
        }

        // ... (Android 빌드 설정 등)

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = outputPath,
            target = buildTarget,
            options = BuildOptions.None 
        };

        // 빌드 실행 및 리포트 분석
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"빌드 성공: {summary.totalSize / 1024 / 1024} MB");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"빌드 실패: {summary.totalErrors} 개의 에러 발생");
            EditorApplication.Exit(1);
        }
    }

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
```
</details>
---
