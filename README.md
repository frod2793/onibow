# OniBow : 2D Side-Scrolling Action Game


> **"수학적 알고리즘 기반의 투사체 제어와 비동기 FSM 설계를 적용한 고성능 2D 액션 게임"**

## 📖 프로젝트 개요 (Overview)
**OniBow**는 플레이어가 다양한 스킬과 곡예에 가까운 이동 기술(대쉬, 점프)을 활용하여 적과 전투를 벌이는 2D 횡스크롤 액션 게임입니다.

단순한 기능 구현을 넘어, **UniTask를 활용한 비동기 상태 관리**, **벡터/행렬 연산을 통한 물리 효과 최적화**, 그리고 **Jenkins 기반의 CI/CD 파이프라인 구축** 등 상용 게임 수준의 기술적 기반을 마련하는 데 중점을 두었습니다.

* **개발 기간:** 202X.XX ~ 202X.XX (1인 개발)
* **주요 역할:** 클라이언트 프로그래밍, 시스템 아키텍처 설계, 툴 개발
* **데모 플레이:** [Itch.io 링크 또는 웹사이트 URL 입력]

---

## ✨ 주요 기능 (Key Features)

### 🎮 Gameplay
* **Dynamic Action:** * 이동, 대쉬(잔상 효과 포함), 자동 공격 시스템
  * 4종의 액티브 스킬 (배리어, 힐, 유도 미사일, 바주카) 구현
* **Advanced AI:** * FSM(유한 상태 머신) 기반의 적 AI
  * 플레이어 거리 추적, 회피 기동, 스킬(다발 사격) 사용 등 지능적 패턴
* **Visual Effects:** * 타격감을 극대화하는 화면 쉐이크, Floating Text(데미지), 체력 경고 비네트 효과

### ⚙️ System & Architecture
* **Resolution Management:** WebGL 및 모바일 대응을 위한 16:9 레터박스/필러박스 자동 처리
* **Optimization:** * `Object Pooling` 시스템을 통한 투사체 및 이펙트 메모리 관리 (GC 최소화)
  * 행렬 연산을 이용한 효율적인 잔상 렌더링
* **DevOps:** Jenkins를 활용한 WebGL/Android 자동 빌드 파이프라인 구축

---

## 💻 핵심 기술 및 코드 분석 (Technical Deep Dive)

### 1. 2차 베지에 곡선(Quadratic Bezier Curve)을 이용한 투사체 제어
물리 엔진(`Rigidbody`)의 불확실성을 배제하고, 수학적 공식을 통해 투사체의 궤적을 **결정론적(Deterministic)**으로 제어하여 기획 의도에 맞는 정확한 타격감을 구현했습니다.

* **Code Snippet (`ArrowController.cs`)**
```csharp
// t(0~1) 값에 따라 2차 베지에 곡선 상의 위치를 반환
_moveTween = DOTween.To(() => t, x =>
{
    t = x;
    // 공식: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
    Vector3 newPos = (1 - t) * (1 - t) * startPos 
                   + 2 * (1 - t) * t * controlPoint 
                   + t * t * endPos;
    transform.position = newPos;

    // 접선 벡터를 계산하여 투사체가 진행 방향을 바라보도록 회전 처리
    if (newPos != previousPos) {
        Vector2 dir = (newPos - previousPos).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
    previousPos = newPos;
}, 1f, duration).SetEase(Ease.Linear);
2. UniTask와 CancellationToken을 활용한 안전한 비동기 FSMUnity의 코루틴(Coroutine) 대신 UniTask를 사용하여 적(Enemy)의 AI 로직을 비동기 루프로 구현했습니다. CancellationToken을 도입하여 오브젝트 파괴 시 발생할 수 있는 MissingReferenceException과 메모리 누수를 원천 차단했습니다.Code Snippet (Enemy.cs)C#private async UniTaskVoid AI_LoopAsync(CancellationToken token)
{
    // 토큰 취소 요청(사망, 파괴 등)이 없을 때까지 루프 실행
    while (!token.IsCancellationRequested && !m_isDead)
    {
        switch (CurrentState)
        {
            case EnemyState.Idle:
                await OnIdleStateAsync(token); // 상태별 비동기 로직 대기
                break;
            case EnemyState.Moving:
                await OnMovingStateAsync(token);
                break;
            // ...
        }
        // Update 타이밍에 맞춰 프레임 대기
        await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
    }
}
3. 벡터 연산을 통한 유도 미사일(Homing Missile) 조향 알고리즘단순한 LookAt 방식이 아닌, 수직 벡터(Vector2.Perpendicular)와 사인 파(Mathf.Sin)를 결합하여 S자 곡선을 그리며 날아가는 자연스러운 유도 미사일 궤적을 구현했습니다.Code Snippet (HomingMissile.cs)C#// 타겟 방향의 수직 벡터 구하기 (회전축 역할)
Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;

// 사인 파동을 이용해 오프셋 계산 (S자 움직임 생성)
float sineOffset = Mathf.Sin((Time.time + _randomStartTime) * waveFrequency) * waveAmplitude;

// 최종 목표 지점 보정 및 부드러운 회전 적용 (RotateTowards)
Vector2 aimPoint = targetPosition + perpendicular * sineOffset;
transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);
4. 행렬(Matrix) 연산을 활용한 잔상 스냅샷 최적화복잡한 계층 구조(Hierarchy)를 가진 캐릭터의 잔상을 생성할 때, Instantiate의 오버헤드를 줄이고 lossyScale 오차를 해결하기 위해 행렬 연산을 도입했습니다. 원본의 World Transform을 잔상 컨테이너의 Local 좌표계로 역산하여 매핑했습니다.Code Snippet (AfterimageSnapshot.cs)C#// 원본 렌더러(World) -> 스냅샷 부모(Local) 변환 행렬 계산
Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;

// 행렬에서 위치, 회전, 크기 성분 추출하여 적용 (1:1 매칭)
partRenderer.transform.localPosition = targetMatrix.GetColumn(3);
partRenderer.transform.localRotation = targetMatrix.rotation;
partRenderer.transform.localScale = targetMatrix.lossyScale;
📂 주요 스크립트 명세 (Detailed Specs)분류스크립트설명CorePlayerControl.csUniTask 기반 이동/공격 루프, 벽/절벽 예측 알고리즘이 포함된 대쉬 로직Enemy.cs거리 기반 FSM, 회피 기동 확률 연산, 비동기 스킬 시전 등 AI 핵심 로직SkillManager.cs배리어, 힐, 유도 미사일, 바주카 등 스킬 쿨타임 및 실행 관리SystemEffectManager.cs데미지 텍스트 및 이펙트의 중앙 집중식 관리 (오브젝트 풀링 적용)SoundManager.csBGM/SFX 풀링 시스템, 볼륨 페이드(DOTween), 음소거 설정 관리UIManager.cs체력/쿨타임 UI 동기화, InputSystem 키 입력 처리, 설정 팝업 제어ToolSoundNameDrawer.cs사운드 리소스 이름을 인스펙터에서 드롭다운으로 선택하는 커스텀 에디터 속성🚀 빌드 자동화 (Jenkins CI/CD)Jenkins 파이프라인과 연동하여 원클릭으로 WebGL 및 Android(AAB/APK) 빌드를 생성하는 자동화 스크립트를 작성했습니다.Command Line Build Example (Android AAB):Bash/path/to/Unity -quit -batchmode \
  -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget Android \
  -androidBuildType AAB \
  -outputPath "Builds/Android/OniBow.aab" \
  -cleanBuild \
  -logFile "build_android.log"
KeyStore 정보는 Jenkins의 Credential Binding을 통해 환경 변수로 안전하게 주입됩니다.
