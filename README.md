# Dunebound_Build&InOutDoor

## 개요

`DuneBound`는 1~4인 멀티플레이어 건설/생존 게임입니다.<br/>
사막이라는 특수한 환경에서 플레이어들이 협력하여 건물을 건설하고, 생존하는 것을 목표로 합니다.<br/>
${\textsf{\color{gray}(위 프로젝트에서 저의 담당 파트(건축 및 실내외 판단) 코드만 존재하는 레포지토리입니다.)}}$

## 주요 기술 스택

- **Unity 6000.1.2f1** - 게임 엔진
- **Fusion 2.0** - 네트워크 멀티플레이어
- **Odin Inspector** - 에디터 UI 프레임워크
- **UniTask** - 비동기 처리
- **VolumetricFogAndMist2** - 환경 효과

## 📁 프로젝트 구조

```
Scripts/
├── 🏗️ Building/
│   ├── Editor/
│   │   ├── BuildingEditorWindow.cs     # 메인 에디터 도구
│   │   └── GeneratorPreset.cs          # 설정 템플릿
│   └── SnapEditingData.cs              # 편집 중 임시 데이터
├── 📊 Data/
│   ├── Building/
│   │   ├── BuildEnum.cs                # 건물 타입 정의
│   │   ├── SnapInfo.cs                 # 스냅 연결 정보
│   │   └── BuildingSnapData.cs         # 스냅 설정 데이터
│   ├── WallRoom.cs                     # 방 데이터 구조
│   └── RoomInfo.cs                     # 방 메타데이터
├── 🎮 Manager/
│   ├── BuildingSystem.cs               # 핵심 건설 시스템
│   ├── InOutDoorSystem.cs              # 실내외 환경 관리
│   └── UnionFindOptimizer.cs           # 알고리즘 최적화
├── 🔗 Placeable/
│   ├── NetworkBuilding.cs              # 네트워크 건물 객체
│   ├── PreviewBuilding.cs              # 건설 미리보기
│   └── ISnapProvider.cs                # 스냅 인터페이스
└── 📦 SObject/
    ├── PlaceableData.cs                # 건물 에셋 데이터
    ├── PlaceableDatabase.cs            # 건물 데이터베이스
    └── BuildingSystemSettings.cs       # 시스템 설정
```

## 건축 프리팹 제작 에디터
- **3단계 에셋 생성 파이프라인**
- **2D/3D 동시 편집**
- **자동화된 워크플로우**
#### Step 1. 에셋 생성
1. `Tools → Building System → Building Editor` 메뉴 실행
2. 3D 모델을 드래그하고 이름 입력 후 "Generate All Assets" 클릭
3. Preview/Network 프리팹 및 데이터 에셋 자동 생성

#### Step 2. 스냅 포인트 편집
1. 건물의 각 면을 선택하여 스냅 포인트 편집
2. 2D 에디터에서 마우스로 직접 포인트 배치
3. 실시간 3D 미리보기로 결과 확인    

#### Step 3. 데이터베이스 등록
1. 완성된 에셋을 게임 데이터베이스에 등록
2. 중복 이름 자동 처리 및 데이터 검증
  
## 건축
- **실시간 스냅 감지**
- **타입별 호환성**
- **멀티플레이어 동기화**
  
## 실내외 판단
- **청크 기반 최적화**
- **VolumetricFog 연동**
- **버프/디버프 시스템**
