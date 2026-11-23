# 에셋 의존성 — 이 저장소의 프리팹·프로파일은 단독으로 열리지 않는다

`assets/` 의 프리팹·프로파일은 팀 Unity 프로젝트 안에서만 동작한다. 아래 GUID 대상 중 **본인 신규 스크립트·프로파일만** 이 저장소에 있고, 나머지(팀 공통 스크립트, 이벤트 채널·설정 SO, Walker 아트)는 포함하지 않았다.

## 포함하지 않은 Walker 아트 (임시 외형)
두 프리팹(팀 커밋 시점 버전)은 팀의 `04. Arts/Walker/Walker.prefab` 을 그대로 중첩한다. 전용 외형·모션은 별도로 제작해 작업 브랜치에서 연결했으며 팀 저장소에는 미반영이다([docs/06](../docs/06_CHARACTER_PIPELINE.md)).

> 정정(2026-11-23): 이전 판에서 Walker 아트를 "스캔 기반 제3자 모델 · Mixamo 계열 애니메이션"이라고 적었으나, 팀 반입 도구(`ZombieSmplBuilder`) 기록상 현재 Walker 는 신규 캐릭터와 **같은 영상→3D(SMPL 리그) 파이프라인 산출물**이다. 제외 이유는 용량이며, SMPL 상업 라이선스 사안은 신규 캐릭터와 같다.

| 대상 | 크기(대략) | 제외 이유 |
|---|---|---|
| `Walker_Skinned.asset` · `Walker_LOD1.asset` · `Walker_LOD2.asset` (메시) | 58MB · 33MB · 27MB | 용량 |
| `ZombieLocomotion_Walker.controller` + `Clips/*.anim` 11개 | 약 18MB | 용량(아트와 한 세트) |
| `Walker.mat` · `WalkerAvatar.asset` · `Walker_Upper.mask` · 텍스처 | 소량 | 위 모델과 한 세트 |

## 프리팹·프로파일이 참조하는 GUID

### SO_Zombie_Student.asset
| GUID | 원래 경로 | 이 저장소 |
|---|---|---|
| `2bafd056288af7c46b8c4c13300dbf28` | `03. ScriptableObjects/Zombies/SO_ZombieAudio_Walker.asset` | 미포함 |
| `e5ad913f7035344bf92373d1271d44d0` | `01. Scripts/Game/Zombie/Data/StudentProfileSO.cs` | 포함 |

### SO_Zombie_Teacher.asset
| GUID | 원래 경로 | 이 저장소 |
|---|---|---|
| `1ea509cc98f6040998188cd1d1bce6fb` | `01. Scripts/Game/Zombie/Data/TeacherProfileSO.cs` | 포함 |
| `2bafd056288af7c46b8c4c13300dbf28` | `03. ScriptableObjects/Zombies/SO_ZombieAudio_Walker.asset` | 미포함 |

### Zombie_Student.prefab
| GUID | 원래 경로 | 이 저장소 |
|---|---|---|
| `0000000000000000d000000000000000` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `00e7396c53586084e8363cca6ed6f29c` | `03. ScriptableObjects/Interaction/SO_Desc_Zombie_Walker.asset` | 미포함 |
| `02298218df915428199e3a1ba4cd32f0` | `03. ScriptableObjects/Zombies/SO_Zombie_Student.asset` | 포함 |
| `0338426556d75b241bee37e3eca8f958` | `03. ScriptableObjects/Events/Audio/SO_Event_Sfx.asset` | 미포함 |
| `03bcb361216443aaacf8722ae0f347cc` | `03. ScriptableObjects/Events/SO_Event_PlayerCaught.asset` | 미포함 |
| `0a8d756587604aeca8f072b58cf72daf` | `03. ScriptableObjects/Events/Noise/SO_Event_NoiseDetected.asset` | 미포함 |
| `0dce9043d74cc3c4ab38fcb78e07976b` | `03. ScriptableObjects/MapGen/SO_MapRuntimeGraph.asset` | 미포함 |
| `168c5858aa7bcb74299196796de8e9d4` | `01. Scripts/Game/CharacterEmission.cs` | 미포함 |
| `1e3fdca004f2d45fe8abbed571a8abd5` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `22360f3f2e054bf0afcd98bc8c6a0121` | `03. ScriptableObjects/Events/Noise/SO_Event_NoiseEmitted.asset` | 미포함 |
| `2a5fc2eac15ac45548bf52ad248d8ced` | `01. Scripts/Game/Zombie/Species/ZombieControllerStudent.cs` | 포함 |
| `37aaecc501a16704cbdda561f5d25c07` | `03. ScriptableObjects/Journal/Zombie/SO_Journal_Zombie_Walker.asset` | 미포함 |
| `3c7d63c6a8d742328f0271dbd3978d58` | `01. Scripts/Game/Zombie/ZombieSfx.cs` | 미포함 |
| `4dbeab64ebec9ec4390a047f0e381c60` | `04. Arts/Walker/Walker.prefab` | 미포함 |
| `69485042b46c43a42ad675521fa8f1f3` | `03. ScriptableObjects/Events/Disguise/SO_Event_DisguiseAbsorption.asset` | 미포함 |
| `69ed85ede01ba8e43bfb9235afa327af` | `03. ScriptableObjects/Events/SO_Event_ZombieAudio.asset` | 미포함 |
| `70e2a30532dd6ff48b347609573cdcc1` | `03. ScriptableObjects/Events/SO_Event_ZombieWeaponHit.asset` | 미포함 |
| `88ad9eebeb6b4189853496f17bf99c05` | `03. ScriptableObjects/Events/SO_Event_ZombieStateChanged.asset` | 미포함 |
| `90ae37c0e3d050f41941a5441f6cba24` | `03. ScriptableObjects/Disguise/SO_DisguiseSettings.asset` | 미포함 |
| `927db03af740d4f4d88fc881a8125143` | `01. Scripts/Game/Zombie/ZombieAnimator.cs` | 참고 코드 포함(src/TeamSystems) |
| `a1f48fbd7d4b4d33a5a1c7e6b7f00003` | `03. ScriptableObjects/Zombies/SO_Zombie_RuntimeRegistry.asset` | 미포함 |
| `b2a9e8b254a2de84785c4ffa4d34da54` | `01. Scripts/Game/Zombie/ZombieSquadSync.cs` | 미포함 |
| `ba211ed880873c943a621ae9d47b1de3` | `01. Scripts/Game/Interaction/ZombieInspectable.cs` | 미포함 |
| `c1f48fbd7d4b4d33a5a1c7e6b7f10006` | `03. ScriptableObjects/Events/Disguise/SO_Event_DisguiseRefillRequested.asset` | 미포함 |
| `d5a57f767e5e46a458fc5d3c628d0cbb` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `de0ae5c689e4c6a47803bbb3053bc30d` | `01. Scripts/Game/Zombie/ZombieSensorySystem.cs` | 참고 코드 포함(src/TeamSystems) |
| `df8d8a8cd8793f046bdf5058165c529b` | `03. ScriptableObjects/Events/Achievements/SO_AchievementFacts.asset` | 미포함 |
| `e68a401f977493344b91f72c8c0e8e30` | `01. Scripts/Game/Zombie/ZombieStateMachine.cs` | 참고 코드 포함(src/TeamSystems) |
| `e96cb6065543e43c4a752faaa1468eb1` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `ebc265c347251fa4c84c376b1de5ce11` | `03. ScriptableObjects/Events/Journal/SO_Event_JournalRecordRequestedServer.asset` | 미포함 |
| `f98030f04d614a5aadd0118115f3237c` | `03. ScriptableObjects/NoiseSystem/SO_NoisePropagationSettings.asset` | 미포함 |

### Zombie_Teacher.prefab
| GUID | 원래 경로 | 이 저장소 |
|---|---|---|
| `0000000000000000d000000000000000` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `00e7396c53586084e8363cca6ed6f29c` | `03. ScriptableObjects/Interaction/SO_Desc_Zombie_Walker.asset` | 미포함 |
| `0338426556d75b241bee37e3eca8f958` | `03. ScriptableObjects/Events/Audio/SO_Event_Sfx.asset` | 미포함 |
| `03bcb361216443aaacf8722ae0f347cc` | `03. ScriptableObjects/Events/SO_Event_PlayerCaught.asset` | 미포함 |
| `0a8d756587604aeca8f072b58cf72daf` | `03. ScriptableObjects/Events/Noise/SO_Event_NoiseDetected.asset` | 미포함 |
| `0dce9043d74cc3c4ab38fcb78e07976b` | `03. ScriptableObjects/MapGen/SO_MapRuntimeGraph.asset` | 미포함 |
| `168c5858aa7bcb74299196796de8e9d4` | `01. Scripts/Game/CharacterEmission.cs` | 미포함 |
| `1e3fdca004f2d45fe8abbed571a8abd5` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `22360f3f2e054bf0afcd98bc8c6a0121` | `03. ScriptableObjects/Events/Noise/SO_Event_NoiseEmitted.asset` | 미포함 |
| `295b2be7ae30a4ae3b7aea1697482352` | `01. Scripts/Game/Zombie/Species/ZombieControllerTeacher.cs` | 포함 |
| `37aaecc501a16704cbdda561f5d25c07` | `03. ScriptableObjects/Journal/Zombie/SO_Journal_Zombie_Walker.asset` | 미포함 |
| `3c7d63c6a8d742328f0271dbd3978d58` | `01. Scripts/Game/Zombie/ZombieSfx.cs` | 미포함 |
| `4dbeab64ebec9ec4390a047f0e381c60` | `04. Arts/Walker/Walker.prefab` | 미포함 |
| `6094fcbc111364926af07043ea8677b1` | `03. ScriptableObjects/Zombies/SO_Zombie_Teacher.asset` | 포함 |
| `69485042b46c43a42ad675521fa8f1f3` | `03. ScriptableObjects/Events/Disguise/SO_Event_DisguiseAbsorption.asset` | 미포함 |
| `69ed85ede01ba8e43bfb9235afa327af` | `03. ScriptableObjects/Events/SO_Event_ZombieAudio.asset` | 미포함 |
| `70e2a30532dd6ff48b347609573cdcc1` | `03. ScriptableObjects/Events/SO_Event_ZombieWeaponHit.asset` | 미포함 |
| `88ad9eebeb6b4189853496f17bf99c05` | `03. ScriptableObjects/Events/SO_Event_ZombieStateChanged.asset` | 미포함 |
| `90ae37c0e3d050f41941a5441f6cba24` | `03. ScriptableObjects/Disguise/SO_DisguiseSettings.asset` | 미포함 |
| `927db03af740d4f4d88fc881a8125143` | `01. Scripts/Game/Zombie/ZombieAnimator.cs` | 참고 코드 포함(src/TeamSystems) |
| `a1f48fbd7d4b4d33a5a1c7e6b7f00003` | `03. ScriptableObjects/Zombies/SO_Zombie_RuntimeRegistry.asset` | 미포함 |
| `b2a9e8b254a2de84785c4ffa4d34da54` | `01. Scripts/Game/Zombie/ZombieSquadSync.cs` | 미포함 |
| `ba211ed880873c943a621ae9d47b1de3` | `01. Scripts/Game/Interaction/ZombieInspectable.cs` | 미포함 |
| `c1f48fbd7d4b4d33a5a1c7e6b7f10006` | `03. ScriptableObjects/Events/Disguise/SO_Event_DisguiseRefillRequested.asset` | 미포함 |
| `d5a57f767e5e46a458fc5d3c628d0cbb` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `de0ae5c689e4c6a47803bbb3053bc30d` | `01. Scripts/Game/Zombie/ZombieSensorySystem.cs` | 참고 코드 포함(src/TeamSystems) |
| `df8d8a8cd8793f046bdf5058165c529b` | `03. ScriptableObjects/Events/Achievements/SO_AchievementFacts.asset` | 미포함 |
| `e68a401f977493344b91f72c8c0e8e30` | `01. Scripts/Game/Zombie/ZombieStateMachine.cs` | 참고 코드 포함(src/TeamSystems) |
| `e96cb6065543e43c4a752faaa1468eb1` | `(패키지/내장 — Assets 밖)` | 미포함 |
| `ebc265c347251fa4c84c376b1de5ce11` | `03. ScriptableObjects/Events/Journal/SO_Event_JournalRecordRequestedServer.asset` | 미포함 |
| `f98030f04d614a5aadd0118115f3237c` | `03. ScriptableObjects/NoiseSystem/SO_NoisePropagationSettings.asset` | 미포함 |

