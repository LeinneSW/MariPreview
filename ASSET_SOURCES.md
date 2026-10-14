# 에셋 출처

원작은 **블루 아카이브**이며, 캐릭터는 **이오치 마리(아이돌)**입니다. 현재 사용하는 캐릭터·모션과 무대의 원본 배포 경로를 구분해 기록합니다.

## 캐릭터·리그·원본 모션

| 항목 | 출처 |
|---|---|
| 배포 저장소 | [lihaohong6/BlueArchiveModels](https://github.com/lihaohong6/BlueArchiveModels) |
| 원본 파일 | [Mari (Idol).glb](https://github.com/lihaohong6/BlueArchiveModels/blob/525ae0faeb0a89f54ef4023f3be3692dcb529e51/Mari%20%28Idol%29.glb) |
| 사용한 리비전 | `525ae0faeb0a89f54ef4023f3be3692dcb529e51` |
| 원본 Git blob | `526ab60034e75d82158ddd32ca9a25cdf1cff697` |
| 원본 모델 뷰어 | [Blue Archive Wiki Models](https://bluearchive.wiki/wiki/Models#Mari) |
| 뷰어 구현 참고 | [lihaohong6/MirahezeDevScripts의 ModelViewer](https://github.com/lihaohong6/MirahezeDevScripts/tree/master/gadgets/ModelViewer) |

Unity 적용 파일은 `Assets/Models/MariRig.fbx`, `Assets/Prefabs/Mari.prefab`, `Assets/Animations/MariOriginal`입니다. 재질·텍스처는 `Assets/Materials/MariOriginal`과 `Assets/Textures/MariOriginal`에 있으며, 윤곽선 메시·셰이더는 `Assets/Models/MariOriginalOutline`과 `Assets/Shaders/MariOriginal`에 있습니다.

본·웨이트·얼굴 애니메이션을 FBX와 Unity 클립으로 변환하고, 재질 분리·입 채널·윤곽선·시선·장전 혼합을 조정했습니다. 원본에서 가져온 34개 모션과 추가로 조정한 `Jump_Start`, `Jump_Air`, `Land`를 포함해 37개 클립을 사용합니다.

확인한 원본 리비전의 파일 목록에는 별도 LICENSE·COPYING·README 파일이 없으며, GitHub 저장소의 라이선스 메타데이터도 지정돼 있지 않습니다. 원본 파일의 확인 근거는 [해당 리비전의 Git 트리](https://api.github.com/repos/lihaohong6/BlueArchiveModels/git/trees/525ae0faeb0a89f54ef4023f3be3692dcb529e51?recursive=1)입니다.

## 무대·초기 정적 모델 자료

| 항목 | 출처 |
|---|---|
| 자료명 | [\[Blue Archive\] -Mari (Idol)- \| Chibi + Stage](https://sketchfab.com/3d-models/blue-archive-mari-idol-chibi-stage-7412827541324d13a557d2f4a7f8d3c4) |
| 배포자 | [VuckyZ (@VuckyZ123)](https://sketchfab.com/VuckyZ123) |
| 배포 페이지의 라이선스 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| 라이선스 확인 자료 | [Sketchfab 모델 메타데이터](https://api.sketchfab.com/v3/models/7412827541324d13a557d2f4a7f8d3c4) |

현재 무대는 `Assets/Models/IdolStage.obj`, `Assets/Prefabs/IdolStage.prefab`, `Assets/Materials/stage.mat`, `Assets/Textures/my_event073_idolstage.png`를 사용합니다. 초기 모델 자료에서 유지된 `Assets/Materials/Mari`와 `Assets/Textures/Mari`도 이 배포 자료에 기반합니다.

캐릭터와 무대를 분리하고 Unity 좌표·크기·정점·재질 경로를 정리했으며, URP 재질과 프리팹을 구성했습니다. 무대 배치와 마리의 발 높이, 카메라 구도도 조정했습니다. 이 초기 정적 자료에는 리그와 애니메이션이 없었으며, 현재 캐릭터 리그·원본 모션의 출처는 위의 GLB 자료입니다.

## 직접 작성한 소프트웨어

행위 목록과 섹션 표시, 재생 제어, 시선·카메라 조작, 모션 혼합·윤곽선 동기화, Unity 빌드·검증 도구는 MariPreview에서 작성·조정한 부분입니다.

위 CC BY 4.0 표기는 해당 Sketchfab 배포 에셋에 관한 기록입니다. 원본 GLB와 MariPreview 소프트웨어 전체에 같은 라이선스를 지정한 것은 아닙니다.
