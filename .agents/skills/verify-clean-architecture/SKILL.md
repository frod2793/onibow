---
name: verify-clean-architecture
description: MVC/MVVM 아키텍처 및 폴더 구조 규칙(Core, Data, Logic, UI) 준수 여부를 검증합니다.
---

# 클린 아키텍처 및 폴더 구조 검증

## 목적

1. **디렉토리 역할 준수 확인** — 스크립트가 지정된 4대 계층(`01_Core`, `02_Data`, `03_Logic`, `04_UI`) 내에 올바르게 위치하는지 검사합니다.
2. **의존성 주입(DI) 단방향화 확인** — UI 계층(View)이 독자적으로 비즈니스 로직 연산을 수행하지 않는지 검사합니다.
3. **MonoBehaviour 상속 억제** — `03_Logic` 및 `04_UI/ViewModels` 내의 스크립트가 유니티 `MonoBehaviour`를 과도하게 상속받아 유니티 수명주기에 결합되는 것을 방지합니다.
4. **MVVM 패턴 준수 확인** — View가 ViewModel을 주입받는 `Initialize` 메서드를 가지고 있는지 확인합니다.

## 실행 시점

- 새로운 스크립트 클래스를 생성하거나 위치를 이동시킨 후
- UIManager 등 기존 레거시 코드를 MVVM 뷰/뷰모델로 리팩토링한 직후

## 관련 파일

| File | Purpose |
|------|---------|
| `Assets/OniBow/Scripts/01_Core/**/*.cs` | 프레임워크 베이스, 서비스 클래스, 인터페이스 |
| `Assets/OniBow/Scripts/02_Data/**/*.cs` | DTO, ScriptableObject 데이터 클래스 |
| `Assets/OniBow/Scripts/03_Logic/**/*.cs` | 게임플레이/비즈니스 로직 (POCO 지향) |
| `Assets/OniBow/Scripts/04_UI/Views/**/*.cs` | 화면 시각화 (MonoBehaviour) |
| `Assets/OniBow/Scripts/04_UI/ViewModels/**/*.cs` | 상태 유지 및 로직 브릿지 (POCO 지향) |

## 워크플로우

### Step 1: `03_Logic` 폴더 내 MonoBehaviour 억제 검사

**파일:** `Assets/OniBow/Scripts/03_Logic/**/*.cs`

**검사:** 비즈니스 로직 계층에서는 가능하면 순수 C# 객체(POCO)를 지향하고, 필요시에만 `MonoBehaviour`를 사용해야 합니다. 

```bash
grep -l "public class .* : MonoBehaviour" Assets/OniBow/Scripts/03_Logic/ -R | grep -v "Enemy.cs\|PlayerControl.cs\|Barrier.cs\|Arrow.cs\|Weapon/.*Controller.cs\|Weapon/.*Bullet.cs\|Weapon/HomingMissile.cs\|Weapon/Roket.cs\|FX/.*"
```

**위반:** 순수 로직이 되어야 할 클래스에 불필요하게 `MonoBehaviour`가 상속되어 있으면 구조적 결합도가 높아집니다.
**수정 방법:** 데이터와 로직을 분리하고, 렌더링/물리 처리가 필요한 뷰/트랜스폼(MonoBehaviour) 클래스로부터 의존성을 역전시킵니다.

### Step 2: `04_UI/ViewModels` 파트의 MonoBehaviour 상속 금지 검사

**파일:** `Assets/OniBow/Scripts/04_UI/ViewModels/**/*.cs`

**검사:** ViewModel은 Unity UI 시스템(View)에 대한 직접적인 종속성이 없어야 하며, `MonoBehaviour`를 상속받지 않아야 합니다.

```bash
grep -n "public class .* : MonoBehaviour" Assets/OniBow/Scripts/04_UI/ViewModels/ -R
```

**위반:** `public class MyViewModel : MonoBehaviour`
**수정 방법:** `MonoBehaviour` 상속을 제거하고, 일반 `class`로 변경합니다. View에서 ViewModel을 `new` 키워드로 생성하거나 DI 컨테이너를 통해 주입받도록 구성합니다.

### Step 3: View의 Initialize 메서드 존재 여부 확인

**파일:** `Assets/OniBow/Scripts/04_UI/Views/*View.cs`

**검사:** MVVM 패턴을 따르는 모든 View 클래스는 ViewModel을 주입받는 `Initialize` 메서드를 가져야 합니다.

```bash
grep -L "public void Initialize(" Assets/OniBow/Scripts/04_UI/Views/*View.cs | grep -v "UIManager.cs"
```

**위반:** `Initialize` 메서드가 없는 View 클래스.
**수정 방법:** 해당 View에 대응하는 ViewModel을 매개변수로 받는 `public void Initialize(TViewModel vm)` 메서드를 추가합니다.

### Step 4: [ FormerlySerializedAs ] 사용 권장 확인

**파일:** `Assets/OniBow/Scripts/04_UI/Views/**/*.cs`

**검사:** 리팩토링 중에 필드명을 변경한 경우 `[FormerlySerializedAs]` 어트리뷰트가 사용되었는지 확인합니다. (이 검사는 수동 확인용으로 활용)

```bash
grep -n "FormerlySerializedAs" Assets/OniBow/Scripts/04_UI/Views/ -R
```

**위반:** 필드명 변경 후 레퍼런스 유실 위험이 있는 경우.
**수정 방법:** `using UnityEngine.Serialization;`을 추가하고 필드에 `[FormerlySerializedAs("OldName")]`을 적용합니다.

## 예외사항

1. **레거시 로직 클래스**: 현재 `03_Logic`에 막 옮겨진 `PlayerControl.cs`, `Enemy.cs` 등 아직 물리 충돌 리팩토링이 덜 끝난 기존 핵심 스크립트들의 `MonoBehaviour` 상속은 당분간 예외 처리합니다.
2. **무기 및 이펙트 컨트롤러**: `ArrowController`, `HomingMissile` 등 물리/시각 효과 처리가 핵심인 로직 클래스들은 예외로 둡니다.
3. **에디터 스크립트**: `99_Editor` 내부의 코드는 이 검증에서 제외됩니다.
