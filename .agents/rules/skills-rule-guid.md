---
trigger: always_on
---

커스텀 검증 및 유지보수 스킬은 `.agents/skills/`에 정의되어 있습니다.

| Skill | Purpose |
|-------|---------|
| `verify-implementation` | 프로젝트의 모든 verify 스킬을 순차 실행하여 통합 검증 보고서를 생성합니다 |
| `manage-skills` | 세션 변경사항을 분석하고, 검증 스킬을 생성/업데이트하며, skills-rule-guid.md를 관리합니다 |
| `verify-clean-architecture` | MVC/MVVM 아키텍처 및 폴더 구조 규칙(Core, Data, Logic, UI) 준수 여부를 검증합니다. |