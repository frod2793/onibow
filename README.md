[OniBow : 2D Side-Scrolling Action GameWebGL 및 Android 환경에 최적화된 고성능 2D 액션 게임 프로젝트Engine: Unity 2022.3 LTSPlatform: WebGL, AndroidKey Tech: UniTask (Async/Await), DOTween, Jenkins CI/CD, Custom Physics📖 프로젝트 소개 (Overview)OniBow는 플레이어가 다양한 스킬과 곡예에 가까운 이동 기술(대쉬, 점프 등)을 활용하여 적과 전투를 벌이는 2D 횡스크롤 액션 게임입니다.단순한 기능 구현을 넘어, 비동기 프로그래밍을 통한 상태 관리 최적화와 수학적 알고리즘을 활용한 독자적인 물리 효과 구현에 중점을 두었습니다. 또한, Jenkins 기반의 빌드 자동화 파이프라인을 구축하여 개발 생산성을 극대화했습니다.[여기에 플레이 GIF 또는 스크린샷 이미지를 넣어주세요]✨ 주요 기능 (Key Features)🎮 GameplayDynamic Action: 이동, 대쉬(잔상 효과), 자동 공격 및 4종의 액티브 스킬(배리어, 힐, 추적 미사일, 바주카) 구현Advanced AI: 플레이어 추적, 거리별 패턴 변경, 회피 기동이 가능한 FSM(유한 상태 머신) 기반 적 AIVisual Effects: 타격감을 극대화하는 화면 쉐이크, 피격 텍스트(Floating Text), 체력 경고 비네트 효과⚙️ System & OptimizationResolution Management: 다양한 디바이스 대응을 위한 16:9 고정 비율 및 레터박스/필러박스 자동 처리 (WebGL 최적화)Object Pooling: 투사체, 이펙트, 데미지 텍스트에 풀링 시스템을 적용하여 GC(Garbage Collection) 최소화Background System: 쉐이더 기반의 시차 스크롤링(Parallax Scrolling) 및 부드러운 테마 전환🛠 DevOps (CI/CD) & ToolsAutomated Build: Jenkins와 연동 가능한 커맨드 라인 빌드 스크립트로 WebGL/Android 빌드 자동화Editor Extensions: 사운드 리소스 이름을 인스펙터에서 드롭다운으로 선택할 수 있는 SoundNameAttribute 개발💻 핵심 알고리즘 및 기술적 구현 (Technical Implementation)1. 2차 베지에 곡선(Quadratic Bezier Curve)을 이용한 투사체 제어Unity의 물리 엔진(Rigidbody)에 의존하지 않고, 수학적 공식을 통해 투사체의 궤적을 결정론적(Deterministic)으로 제어하여 정확한 타격감을 구현했습니다.File: ArrowController.csCode Snippet:// t(0~1) 값에 따라 곡선 상의 위치를 반환하는 2차 베지에 공식 적용
_moveTween = DOTween.To(() => t, x =>
{
    t = x;
    // 공식: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
    Vector3 newPos = (1 - t) * (1 - t) * startPos 
                   + 2 * (1 - t) * t * controlPoint 
                   + t * t * endPos;
    transform.position = newPos;

    // 이동 방향에 따른 회전 처리 (접선 벡터 계산)
    if (newPos != previousPos)
    {
        Vector2 dir = (newPos - previousPos).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
    previousPos = newPos;
}, 1f, duration).SetEase(Ease.Linear);
2. 행렬(Matrix) 연산을 활용한 잔상 스냅샷 최적화복잡한 계층 구조를 가진 캐릭터의 잔상을 생성할 때, Instantiate의 오버헤드를 줄이고 lossyScale로 인한 오차를 해결하기 위해 행렬 연산을 도입했습니다. 원본의 Transform을 잔상 컨테이너의 로컬 좌표계로 역산하여 매핑했습니다.File: AfterimageSnapshot.csCode Snippet:// 원본 렌더러(World) -> 스냅샷 부모(Local) 변환 행렬 계산
Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;

// 행렬에서 위치, 회전, 크기 성분 추출하여 적용
partRenderer.transform.localPosition = targetMatrix.GetColumn(3); // 4번째 열이 위치 벡터
partRenderer.transform.localRotation = targetMatrix.rotation;
partRenderer.transform.localScale = targetMatrix.lossyScale;
3. UniTask와 CancellationToken을 활용한 안전한 비동기 FSM기존 코루틴(Coroutine) 대신 UniTask를 사용하여 적(Enemy)의 AI 로직을 비동기 루프로 구현했습니다. 특히 CancellationToken을 도입하여 오브젝트 파괴 시 발생할 수 있는 MissingReferenceException과 메모리 누수를 원천 차단했습니다.File: Enemy.csCode Snippet:private async UniTaskVoid AI_LoopAsync(CancellationToken token)
{
    // 토큰 취소 요청(오브젝트 파괴 등)이 없을 때까지 루프 실행
    while (!token.IsCancellationRequested && !m_isDead)
    {
        switch (CurrentState)
        {
            case EnemyState.Idle:
                await OnIdleStateAsync(token); // 각 상태별 비동기 메서드 호출
                break;
            case EnemyState.Moving:
                await OnMovingStateAsync(token);
                break;
            // ... (생략)
        }
        // 다음 프레임까지 대기 (Update 타이밍)
        await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
    }
}
4. 벡터 연산을 통한 유도 미사일(Homing Missile) 조향 로직타겟을 향해 단순히 회전하는 것이 아니라, 수직 벡터(Vector2.Perpendicular)와 사인 파(Mathf.Sin)를 결합하여 S자 곡선을 그리며 날아가는 자연스러운 유도 미사일 알고리즘을 구현했습니다.File: HomingMissile.csCode Snippet:// 타겟 방향의 수직 벡터 구하기 (회전축 역할)
Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;

// 사인 파동을 이용해 오프셋 계산 (S자 움직임 생성)
float sineOffset = Mathf.Sin((Time.time + _randomStartTime) * waveFrequency) * waveAmplitude;

// 최종 목표 지점 보정 및 회전 적용 (RotateTowards 사용)
Vector2 aimPoint = targetPosition + perpendicular * sineOffset;
transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);
📂 스크립트 기능 명세 (Detailed Specs)ScriptDescriptionPlayerControl.csUniTask 기반의 비동기 이동/공격 루프, 벽/절벽 감지 알고리즘이 포함된 대쉬 로직Enemy.cs거리 기반 상태 전환, 회피 기동 확률 연산, 비동기 스킬 시전 등 AI 핵심 로직SkillManager.cs배리어, 힐, 유도 미사일, 바주카 등 플레이어/적 스킬의 쿨타임 및 실행 관리EffectManager.cs데미지 텍스트(Floating Text) 및 파티클 이펙트의 생성/파괴를 관리하는 중앙 매니저SoundManager.csBGM/SFX 풀링 시스템, 볼륨 페이드 인/아웃(DOTween), 음소거 설정 관리UIManager.cs플레이어 체력/쿨타임 UI 동기화, InputSystem 키 입력 처리, 설정 팝업 제어SoundNameDrawer.cs사운드 리소스 이름을 인스펙터에서 드롭다운으로 선택 가능하게 하는 에디터 툴🚀 빌드 자동화 (Jenkins CI/CD)이 프로젝트는 Jenkins 파이프라인을 통해 WebGL 및 Android 빌드를 자동화할 수 있도록 BuildScript.cs를 구성하였습니다.Command Line Build Usage1. WebGL Clean Build/path/to/Unity -quit -batchmode \
  -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget WebGL \
  -outputPath "Builds/WebGL" \
  -cleanBuild \
  -logFile "build_webgl.log"
2. Android AAB Build (Production)Jenkins Credential Binding을 통해 키스토어 정보를 안전하게 주입합니다./path/to/Unity -quit -batchmode \
  -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget Android \
  -androidBuildType AAB \
  -outputPath "Builds/Android/OniBow.aab" \
  -cleanBuild \
  -logFile "build_android.log"](url)
