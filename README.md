# OniBow: 2D 횡스크롤 액션 게임

## 프로젝트 설명

"OniBow"는 플레이어가 다양한 스킬과 이동 기술을 활용하여 적과 전투를 벌이는 2D 횡스크롤 액션 게임입니다. 직관적인 조작과 시각적으로 풍부한 효과를 통해 몰입감 있는 게임 경험을 제공하며, WebGL 및 Android 환경에 최적화되어 있습니다.

## 주요 기능

- **플레이어 조작**: 이동, 대쉬, 자동 공격, 스킬 사용
- **다양한 스킬**: 배리어, 힐, 추적 미사일, 바주카 등
- **적 AI**: 플레이어를 추적하고 다양한 공격 패턴 사용
- **UI 시스템**: 체력 바, 스킬 쿨타임, 설정 팝업 등
- **시각 효과**: 폭발, 데미지 텍스트, 체력 경고 비네트 등
- **사운드 관리**: BGM 및 SFX 재생, 볼륨 조절
- **화면 비율 관리**: 16:9 고정 및 레터박스 처리 (WebGL 최적화)
- **배경 시스템**: 시차 스크롤링 및 테마 전환
- **빌드 자동화**: Jenkins 연동을 위한 커맨드 라인 빌드 스크립트 (WebGL, Android 지원)

## 스크립트별 기능 명세

### 1. `AKBullet.cs`
- **기능**: AK 총알의 생명주기, 충돌 처리, 오브젝트 풀 반환을 관리합니다.
- **주요 메서드**:
    - `OnTriggerEnter2D(Collider2D other)`: 플레이어와 충돌 시 데미지를 입히고 이펙트/사운드를 재생하며 카메라를 흔든 후 총알을 풀로 반환합니다.
    - `ReturnAfterDelay(float delay, CancellationToken token)`: 지정된 시간 후 총알을 오브젝트 풀로 반환하는 비동기 메서드입니다.
    - `ReturnToPool()`: 총알 오브젝트를 풀로 반환하고, 생명주기 취소 토큰을 해제합니다.

### 2. `EffectManager.cs`
- **기능**: 게임 내 모든 시각 효과(폭발, 데미지 텍스트, 체력 경고 비네트 등)를 중앙에서 관리합니다. 오브젝트 풀링을 사용하여 데미지 텍스트를 효율적으로 관리합니다.
- **주요 메서드**:
    - `PlayEffect(GameObject effectPrefab, Vector3 position, Quaternion rotation, float scale = 1f, int? sortingOrder = null)`: 지정된 프리팹으로 이펙트를 생성하고, ParticleSystem 또는 Animator 길이에 맞춰 자동으로 파괴합니다.
    - `ShowDamageText(GameObject target, int damage)`: 대상의 머리 위에 데미지 텍스트를 표시하고, 크리티컬 여부에 따라 스타일을 변경합니다. 오브젝트 풀에서 텍스트를 가져와 사용합니다.
    - `UpdateLowHealthEffect(int currentHp, int maxHp)`: 플레이어 체력 비율에 따라 화면 가장자리에 체력 경고 비네트 효과를 활성화/비활성화하고 깜빡이는 애니메이션을 제어합니다.

### 3. `SoundNameDrawer.cs` (Editor Script)
- **기능**: `SoundNameAttribute`가 적용된 `string` 필드를 Unity 인스펙터에서 `SoundManager`에 등록된 사운드 목록을 드롭다운으로 선택할 수 있도록 커스터마이징합니다.
- **주요 메서드**:
    - `OnGUI(Rect position, SerializedProperty property, GUIContent label)`: 인스펙터 GUI를 그리는 핵심 메서드로, 사운드 목록 팝업을 표시하고 선택된 값을 `SerializedProperty`에 반영합니다.
    - `InitializeSoundNames()`: `SoundManager`에서 BGM 및 SFX 이름을 가져와 드롭다운 목록을 초기화합니다.

### 4. `SoundNameAttribute.cs`
- **기능**: `SoundNameDrawer`와 함께 사용되는 커스텀 속성으로, `string` 필드를 사운드 이름 선택 드롭다운으로 표시하도록 마킹합니다.

### 5. `GameManager.cs`
- **기능**: 게임의 전반적인 상태(타이틀, 플레이 중, 게임 오버, 게임 클리어)를 관리하고, 게임 시작/종료 시의 UI 전환, 카메라 흔들림 효과 등을 제어합니다.
- **주요 메서드**:
    - `StartGameSequenceAsync()`: 게임 시작 시 타이틀 화면 전환, 카운트다운, 게임 활성화 등 일련의 비동기 시퀀스를 처리합니다.
    - `ShakeCamera(float duration, float strength, int vibrato = 10, float randomness = 90)`: 메인 카메라를 지정된 시간과 강도로 흔드는 효과를 재생합니다.
    - `EndGame(GameState endState, string logMessage, Action endEvent)`: 게임 오버 또는 게임 클리어 시 공통적으로 호출되는 종료 로직을 처리하고, 관련 UI를 활성화합니다.

### 6. `ScreenResolutionManager.cs`
- **기능**: WebGL 빌드 환경을 포함하여 다양한 화면 크기에서 게임 화면의 16:9 비율을 유지하고, 남는 공간을 검은색 레터박스로 처리합니다.
- **주요 메서드**:
    - `UpdateAspectRatio()`: 현재 화면 비율을 계산하고, 목표 16:9 비율에 맞춰 메인 카메라의 Viewport Rect를 동적으로 조절합니다.
    - `CreateLetterboxCamera()`: 레터박스 영역을 검은색으로 채우기 위한 별도의 카메라를 생성하고 설정합니다.

### 7. `BackgroundManager.cs`
- **기능**: 여러 개의 레이어로 구성된 배경을 스크롤하여 시차 효과(Parallax Scrolling)를 구현하고, 배경 테마를 부드럽게 전환하는 기능을 제공합니다.
- **주요 메서드**:
    - `SwitchBackground(int newIndex)`: 지정된 인덱스의 배경 테마로 부드럽게 전환하는 애니메이션을 실행합니다.
    - `ResizeBackgroundsToFitScreen()`: 카메라의 뷰포트에 맞춰 모든 배경 레이어의 크기를 동적으로 조절하여 화면을 완벽하게 채우도록 합니다.
    - `ScrollLayer(SceneLayer layer)`: 각 배경 레이어의 `speedFactor`에 따라 스크롤 오프셋을 업데이트하여 시차 효과를 만듭니다.

### 8. `UIManager.cs`
- **기능**: 플레이어 및 적의 체력 UI, 스킬 버튼, 이동 버튼, 설정 팝업 등 게임 내 모든 UI 요소의 표시 및 상호작용을 관리합니다. 키보드 입력 처리도 담당합니다.
- **주요 메서드**:
    - `HandleKeyboardInput()`: `UnityEngine.InputSystem`을 사용하여 키보드 입력을 감지하고, 플레이어의 이동 및 대쉬를 제어합니다.
    - `UpdatePlayerHpUI(int currentHp, int tempHp, int maxHp)`: 플레이어의 체력 변화에 따라 체력 바(메인/예비)와 텍스트를 업데이트하고, 예비 체력 감소 애니메이션을 처리합니다.
    - `UpdateSingleSkillUI(SkillUIElements ui, float remainingTime, float totalCooldown)`: 개별 스킬 버튼의 쿨타임 텍스트와 마스크 이미지를 업데이트하여 쿨타임 진행 상황을 시각적으로 표시합니다.

### 9. `PlayerControl.cs`
- **기능**: 플레이어 캐릭터의 이동, 공격, 대쉬, 체력 관리, 피격 및 사망 처리 등 핵심적인 조작 및 상태 로직을 담당합니다. UniTask와 DOTween을 활용하여 부드러운 움직임과 비동기 처리를 구현합니다.
- **주요 메서드**:
    - `TakeDamage(int damage)`: 플레이어가 데미지를 입었을 때 체력을 감소시키고, 피격 애니메이션, 사운드, 데미지 텍스트 표시 등을 처리합니다. 무적 상태를 확인합니다.
    - `StartMoving(float direction)`: 지정된 방향으로 플레이어 이동을 시작하고, 가속 트윈을 적용하며, 이동 애니메이션을 재생합니다.
    - `StopMoving()`: 플레이어 이동을 멈추고, 감속 트윈을 적용한 후 IDLE 상태로 전환하여 자동 공격을 재개합니다.
    - `Dash(float direction)`: 지정된 방향으로 대쉬를 실행합니다. 지면 체크, 벽/절벽 예측, 카메라 경계 제한 로직을 포함하여 안전한 대쉬 거리를 계산합니다.
    - `SetSkillUsageState(bool isUsing, bool stopMovement = true)`: 스킬 사용 상태를 설정하고, 스킬의 `stopMovement` 파라미터에 따라 플레이어의 이동 가능 여부와 공격 루프 중단 여부를 제어합니다.
    - `IsActionableState()`: 플레이어가 현재 새로운 행동(이동, 공격, 대쉬)을 시작할 수 있는 상태인지 여부를 반환합니다. 스킬 사용 중 이동 허용 플래그를 고려합니다.
    - `RepeatingFireLoopAsync(CancellationToken token)`: 가장 가까운 적을 찾아 주기적으로 화살을 발사하는 비동기 루프를 실행합니다.
    - `MoveLoopAsync(float direction, CancellationToken token)`: 플레이어를 지정된 방향으로 지속적으로 이동시키는 비동기 루프입니다. 카메라 경계 및 절벽 예측을 통해 안전한 이동을 보장합니다.
    - `DashAsync(float targetX, float duration)`: 지정된 목표 X 좌표까지 대쉬를 비동기적으로 실행합니다. 대쉬 중 중력 무시, 잔상 효과, 정확한 위치 제어 로직을 포함합니다.

### 10. `SkillManager.cs`
- **기능**: 플레이어와 적이 사용할 수 있는 다양한 스킬(배리어, 힐, 추적 미사일, 바주카, 적 다발 사격)의 쿨타임 관리 및 실행 로직을 담당합니다.
- **주요 메서드**:
    - `PlayerSkill1_BarrierAsync(CancellationToken token)`: 플레이어에게 무적 배리어를 생성하고, 지정된 시간 동안 유지한 후 소멸 애니메이션과 함께 제거합니다.
    - `PlayerSkill2_HealAsync(CancellationToken token)`: 플레이어의 체력을 즉시 회복하고, 추가로 일정 시간 동안 점진적으로 체력을 회복시키는 효과를 적용합니다.
    - `PlayerSkill3_HomingMissilesAsync(Transform target, CancellationToken token)`: 지정된 적을 향해 여러 발의 추적 미사일을 순차적으로 발사합니다.
    - `ExecuteBazookaSkillAsync(Transform firePoint, GameObject hand, Transform target, CancellationToken token)`: 플레이어가 바주카를 장착하고, 조준 후 폭발탄을 발사하는 비동기 시퀀스를 처리합니다.
    - `EnemyAKSkillAsync(Transform handPoint, Transform target, CancellationToken token)`: 적이 AK47을 장착하고, 조준 후 다발 사격을 가하는 비동기 시퀀스를 처리합니다.

### 11. `BuildScript.cs` (Editor Script)
- **기능**: Jenkins와 같은 CI/CD 환경에서 Unity 프로젝트를 커맨드 라인을 통해 자동으로 빌드할 수 있도록 지원합니다. WebGL, Android 등 다양한 플랫폼 빌드와 클린 빌드 옵션을 제공합니다.
- **주요 메서드**:
    - `PerformBuild()`: 커맨드 라인 인자를 파싱하여 빌드 타겟, 출력 경로, 클린 빌드 여부 등을 결정하고 `BuildPlayer`를 호출하는 메인 진입점입니다.
    - `BuildPlayer(BuildTarget buildTarget, string outputPath, bool cleanBuild)`: 실제 Unity 빌드 프로세스를 실행하는 메서드입니다. 클린 빌드 옵션이 활성화된 경우 출력 디렉토리를 삭제하고, 빌드 결과를 로깅합니다.
    - `SetupAndroidBuildSettings(string buildType)`: Android 빌드 시 필요한 키스토어 정보(환경 변수에서 로드) 및 빌드 타입(AAB/APK)을 설정합니다.

## 빌드 자동화 (Jenkins 연동)

`BuildScript.cs`를 사용하여 Jenkins 파이프라인 또는 쉘 스크립트에서 Unity 프로젝트를 자동으로 빌드할 수 있습니다.

### 기본 커맨드 구조

```sh
/path/to/Unity -quit -batchmode -projectPath /path/to/YourProject -executeMethod BuildScript.PerformBuild -buildTarget <Target> -outputPath <Path> -logFile /path/to/build.log
```

- `/path/to/Unity`: Unity 에디터 실행 파일의 경로입니다. (예: `C:\Program Files\Unity\Hub\Editor\2022.3.10f1\Editor\Unity.exe`)
- `/path/to/YourProject`: Unity 프로젝트의 루트 폴더 경로입니다.
- `<Target>`: 빌드할 플랫폼입니다. (예: `WebGL`, `StandaloneWindows64`, `Android`)
- `<Path>`: 빌드 결과물이 저장될 경로와 파일 이름입니다.
- `-cleanBuild`: (선택 사항) 이 플래그를 추가하면 빌드 전에 출력 디렉토리를 삭제합니다.
- `-androidBuildType [APK/AAB]`: (Android 빌드 시에만 사용) APK 또는 AAB 중 빌드 타입을 지정합니다.

### 예시: WebGL 클린 빌드

```sh
/Applications/Unity/Hub/Editor/2022.3.10f1/Unity.app/Contents/MacOS/Unity \
  -quit \
  -batchmode \
  -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget WebGL \
  -outputPath "Builds/WebGL" \
  -cleanBuild \
  -logFile "build_webgl.log"
```

### 예시: Android AAB 클린 빌드

Jenkins Credential Binding을 통해 다음 환경 변수를 설정해야 합니다:
- `UNITY_KEYSTORE_NAME`
- `UNITY_KEYSTORE_PASS`
- `UNITY_KEYALIAS_NAME`
- `UNITY_KEYALIAS_PASS`

```sh
/path/to/Unity -quit -batchmode -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget Android \
  -androidBuildType AAB \
  -outputPath "Builds/Android/OniBow.aab" \
  -cleanBuild \
  -logFile "build_android.log"
```

### 예시: Android APK 빌드

```sh
/path/to/Unity -quit -batchmode -projectPath . \
  -executeMethod BuildScript.PerformBuild \
  -buildTarget Android \
  -androidBuildType APK \
  -outputPath "Builds/Android/OniBow.apk" \
  -logFile "build_android.log"
```