# Project Mush 셰이더 개선 비교본

2026-09-07. 기존 프로젝트는 수정하지 않고 이 작업 폴더의 복사본만 변경했다. 독립 Unity 프로젝트에서 전후 화면을 렌더링하고 직접 확인했다. 현재 게임에 적용한 결과는 아니다.

## 결과

- [전후 비교 페이지](Evidence~/Captures/compare.html)
- [수정 쇼룸](Evidence~/Captures/Candidate_Prop_ShowRoom.png)
- [수정 로비](Evidence~/Captures/Candidate_PM_Lobby.png)
- [셰이더 검사](Evidence~/Captures/validation.txt)
- [원본 변경·GUID 검사](Evidence~/static-validation.json)

## 기준과 보존

작업 시작 HEAD는 c59ecb9이며 리뷰 대상 Art가 현재 체크아웃에 없었다. 최종 아틀라스까지 포함된 Git 커밋 5adfbe5의 Assets/Art를 비교 기준으로 복사했다. 초기 탐색본은 Evidence~/Superseded로 옮겨 최종 Candidate에서 제외했다.

기존 에셋 GUID 100개를 새 값으로 바꾸고 새 재질·HLSL에도 별도 GUID를 부여했다. 기존 씬, 머티리얼, 텍스처 메타, 런타임 코드, 설정은 덮어쓰지 않았다. 최종 git diff는 비어 있고 git status에는 작업 폴더만 나타난다.

## 구현 내용

| 영역 | 변경 |
|---|---|
| 공통 코드 | 두 셰이더의 표면 샘플링과 Forward를 PMReviewInput.hlsl / PMReviewForward.hlsl로 분리. 이름은 Project Mush/AI Review 계열 |
| 스타일 음영 | 기본색을 미리 어둡게 하던 연산 제거. URP PBR 결과에서 주광 확산광만 조정해 간접광·추가광·반사·발광 보존 |
| 림 | 따뜻한 색으로 기본색을 덮는 방식에서 약한 주광 의존 확산광 강조로 변경 |
| GI | 라이트맵 UV, LIGHTMAP_ON, 방향성 라이트맵, Shadowmask, SAMPLE_GI / SAMPLE_SHADOWMASK 추가. 라이트맵이 없는 물체는 SH 경로 |
| 보조 패스 | Lit UsePass 제거. 같은 상수 배치로 ShadowCaster·DepthOnly 구성. DepthNormals는 실제 커스텀 노멀맵, Meta는 실제 표면·발광 마스크·강도 사용 |
| 데이터 맵 | 복사본 AO·Metallic·Smoothness와 보관 Roughness의 sRGB 해제. M_HLSL AO/Metallic 강도 2→1, 노멀 1→0.4 |
| 눈 | Metallic 0, Smoothness 0.08, Normal 0.4, Rim 0. 지면 Base Texture Strength 0.55로 큰 색무늬 대비 완화 |
| 목재·금속 | 바닥/지붕 노멀 2→0.45. 일반 비금속 Metallic 0, 금속 Smoothness 0.6 비교값 |
| 로비 구분 | SM_AI_Wall / SM_AI_Pillar 생성 후 복사한 PM_Lobby 씬에 연결. 벽은 덜 붉게, 기둥은 어둡게 분리 |

기본 Shadow Strength는 0.18, 일반 Rim은 0.025로 줄였다. 새 Base Texture Strength는 Forward와 Meta에 동일하게 반영된다. Metallic/Smoothness 맵이 상수값을 대체한다는 점도 인스펙터에 명시했다. 불투명 셰이더이며 추가 실시간 조명은 만들지 않았다.

## 화면 판단

쇼룸에서는 눈이 얼음판처럼 보이던 날카로운 굴곡 하이라이트가 크게 줄었다. 큰 색면은 남기면서 대비를 낮춰 소품 실루엣을 읽기 쉬워졌다.

로비에서는 목재 홈의 대비가 줄고 벽·기둥 색 구분이 생겼다. 개선 폭은 눈 지면보다 작으며 어두운 실내 전체를 해결한 단계는 아니다. 개 돌봄 구역의 조명은 추가 검증이 필요하다.

랜턴의 발광 패스 정합성은 수정했지만 중심/가장자리 아트 마스크는 새로 제작하지 않았다. 나무·바위·깃발의 모델, 눈의 두께와 기존 텍스처 주름도 유지했다.

## 검증과 한계

- 독립 RenderSandbox~에서 Unity 6000.3.18f1 / URP 17.3.0으로 렌더링. 원본 Unity 프로젝트는 열지 않음.
- 저장 카메라 2개 × 이전/수정 = 최종 이미지 4개. 960×720, Linear, 같은 카메라, 실시간 그림자·후처리·MSAA OFF.
- 두 셰이더 × 5개 패스 × 3개 키워드 조합의 SetPass 검사 30건 모두 True. 셰이더 오류 메시지 없음. 모바일 변형 전체 검사는 아님.
- 두 셰이더 SRP Batcher 호환 코드 0 확인.
- GUID 중복/원본 GUID 잔존 없음. 외부 참조 3개는 URP/Core의 카메라·라이트·Volume 스크립트.
- 기존 프로젝트의 git diff 없음.

실제 라이트맵 베이크, 발광의 주변 표면 기여, 이동 물체의 Light Probe 수신, Quest 양안·주행·GPU 성능은 미검증이다. Forward 렌더러 대상이며 Forward+/Deferred/APV/동적 라이트맵/SSAO 통합은 범위 밖이다. 독립 프로젝트에는 원본 게임 런타임이 없으므로 캡처를 실제 플레이 결과로 해석하면 안 된다.

Art 스냅샷 밖의 로비 Volume Profile은 복구하지 않았다. Candidate에서 누락 참조를 비웠고 양쪽 캡처의 후처리를 꺼 비교했다. 실제 프로젝트 노출/색보정과는 다를 수 있다.

## 비용과 다음 단계

생성형 이미지, 외부 에이전트, 전체 베이크, 모바일 빌드, 텍스처 재제작 없이 로컬 CPU/GPU로 소수 카메라만 확인했다. Unity 작업 워커는 2개로 제한했다. 다음은 로비 대표 구역의 작은 베이크 검증과 Quest 확인이다. 채널 패킹은 화면 확정 후 필요할 때 진행한다.

Candidate/Scenes의 두 씬이 검토용 결과다. 현재 런타임이나 기존 씬에는 연결하지 않았다. Evidence~는 로그·이미지·소스 보관, RenderSandbox~는 독립 검증 프로젝트이며 ~ 접미사로 원본 Unity의 자동 임포트를 피했다. Tools/ReviewCapture.cs.txt는 게임 스크립트로 컴파일되지 않도록 텍스트로 보관했고 자동 실행 훅도 없다.

재생성 순서: prepare.cjs → improve.cjs → refine.cjs → sandbox.cjs → 격리 Unity의 ReviewCapture.Run. 이 순서는 Candidate를 다시 만들므로 이후 수동 수정이 있으면 먼저 보존해야 한다. 결과를 보는 데 도구 재실행은 필요 없다.

## 다른 파일 접근 내역

기존 프로젝트에서는 .git 이력, Packages/manifest.json, ProjectSettings/ProjectVersion.txt, Library/PackageCache의 URP/Core 및 의존 패키지, 에셋 목록을 읽었다. 독립 렌더 프로젝트는 기존 패키지를 로컬 경로로 참조했다.

프로젝트 밖의 접근은 다음과 같다.

- E:/ProjectMush_ArtReview_20260906: 앞선 리뷰 문서·이미지 및 폴더 목록 확인. 수정 없음.
- D:/mbcAcademy, D:/mbcAcademy/CodexOutput: 앞선 탐색의 폴더 목록 확인. 수정 없음.
- D:/AGENTS.md, D:/mbcAcademy/AGENTS.md: 앞선 상위 지침 존재 확인 시도. 파일은 확인되지 않음.
- C:/Users/SoEsy/.codex: 도구 탐색 중 바로 아래 폴더명만 열람. 이번 작업에서 설정·세션 파일을 읽거나 수정하지 않음.
- C:/Users/SoEsy/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe: 설치된 Node 실행.
- C:/Program Files/Unity/Hub/Editor/6000.3.18f1: Unity와 번들 컴파일러 실행/읽기.
- Unity 라이선스 IPC와 사용자 로그·캐시: Unity 실행 과정에서 접근. 첫 샌드박스 실행이 라이선스 초기화에 멈춰 해당 프로세스를 중지하고 승인된 실행으로 재시도. 로그에 C:/Users/SoEsy/AppData/Local/Unity/Caches/CurlRequestCache.db 접근이 기록됨. Unity가 관리하는 사용자 파일의 개별 변경 여부는 별도 감사하지 않음.

직접 작성한 작업 파일, 지정한 Unity 프로젝트·로그·렌더 출력은 모두 Assets/Art/_AI_Work 안에 있다. 외부 문서나 다른 사용자 프로젝트를 수정하지 않았다.
