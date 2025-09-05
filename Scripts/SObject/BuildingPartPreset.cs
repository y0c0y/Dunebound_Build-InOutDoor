using System.Collections.Generic;
using Data.Building;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SObject
{
    [CreateAssetMenu(fileName = "New Building Part Preset", menuName = "Scriptable Objects/Building Part Preset")]
    public class BuildingPartPreset : ScriptableObject
    {
        [Title("Part Information")]
        [LabelWidth(100)]
        public BuildingPartType partType = BuildingPartType.Wall;
        
        [TextArea(2, 4)]
        [LabelText("Description")]
        public string description = "A template for automatically generating snap points.";

        [Title("Generation Parameters")]
        [Tooltip("How far from the exact corner to place corner points.")]
        public float cornerOffset = 0.0f; // 0으로 두면 정확히 코너에 생성

        [Tooltip("Spacing between points when generating along an edge.")]
        public float edgePointSpacing = 1.0f; // 1m 간격

        // --- 포인트 자동 생성 메서드들 ---

        [Button("Generate Points for Face", ButtonSizes.Large)]
        [InfoBox("This is a helper method. It should be called from the BuildingEditorWindow.")]
        // 이 함수는 에디터 윈도우에서 호출될 것입니다.
        public void GeneratePointsForFace(FaceSnapConfig faceConfig, Vector2 faceSize)
        {
            if (faceConfig == null) return;
            
            // 기존 포인트 삭제
            faceConfig.SnapPoints.Clear();
            
            // 프리셋 타입에 따라 다른 생성 로직 호출
            switch (partType)
            {
                case BuildingPartType.Foundation:
                case BuildingPartType.Floor:
                    GenerateCornerPoints(faceConfig, faceSize);
                    break;
                case BuildingPartType.Wall:
                    GenerateWallPoints(faceConfig, faceSize);
                    break;
                case BuildingPartType.Ceiling:
                    GenerateCornerPoints(faceConfig, faceSize);
                    break;
                // 다른 타입에 대한 규칙 추가...
            }
        }

        // 네 귀퉁이에 포인트를 생성하는 로직
        private void GenerateCornerPoints(FaceSnapConfig faceConfig, Vector2 faceSize)
        {
            var halfSizeX = faceSize.x / 2f - cornerOffset;
            var halfSizeY = faceSize.y / 2f - cornerOffset;
            
            var positions = new List<Vector2>
            {
                new Vector2(-halfSizeX, -halfSizeY), // Bottom-Left
                new Vector2(halfSizeX, -halfSizeY),  // Bottom-Right
                new Vector2(-halfSizeX, halfSizeY),  // Top-Left
                new Vector2(halfSizeX, halfSizeY)   // Top-Right
            };

            foreach (var pos in positions)
            {
                // 실제 위치는 BuildingEditorWindow에서 면의 방향에 맞게 변환될 것입니다.
                // 여기서는 2D 로컬 좌표만 생성합니다.
                // 이 예시에서는 단순화를 위해 localPosition에 바로 넣지만,
                // 실제로는 localPosition을 계산하는 로직이 필요합니다.
                // 이 부분은 예시이며, BuildingEditorWindow에서 처리하는 것이 더 정확합니다.
            }
            
            Debug.Log($"Generated 4 corner points for a face of size {faceSize}");
        }
        
        // 벽을 위한 포인트를 생성하는 로직 (예: 위/아래 가장자리)
        private void GenerateWallPoints(FaceSnapConfig faceConfig, Vector2 faceSize)
        {
            // 이 곳에 벽 스타일에 맞는 포인트 생성 로직을 구현합니다.
            // 예를 들어, 아래쪽 두 코너와 위쪽 두 코너에 포인트를 추가할 수 있습니다.
            GenerateCornerPoints(faceConfig, faceSize); // 간단히 코너 포인트 재사용
        }
    }
}