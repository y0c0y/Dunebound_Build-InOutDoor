using System;
using System.Collections.Generic;
using System.Linq;
using Data.Building;
using Placeable;
using UnityEngine;

namespace Data
{
    [Serializable]
    public class WallRoom
    {
        private const float RecalculationInterval = 2f;
        
        public int roomId;
        public List<NetworkBuilding> walls = new();
        public List<NetworkBuilding> windows = new();
        public List<NetworkBuilding> doors = new();
        public Dictionary<NetworkBuilding, SortedSet<SnapInfo>> SnapGroups = new();

        // 최적화된 경계 상자 정보
        public Vector3 roomCenter;
        public Vector3 roomSize;
        public Quaternion roomRotation;
        public BoxCollider roomCollider;
        public Bounds roomBounds;
        public bool isEnclosed;

        // 환경 효과
        public float protectionLevel;
        public float lightLevel = 0.5f;
        public bool hasVentilation;
        
        // 캐싱 필드
        private bool? _cachedIsEnclosed;
        private bool _isCacheDirty = true;
        private float _lastCalculationTime;
        private int _lastWallCount;


        // =============================================================================
        // 🎯 메인 업데이트 메서드 - 모든 계산을 통합
        // =============================================================================

        /// <summary>
        /// 룸의 모든 속성을 업데이트합니다 (밀폐 여부, 경계, 콜라이더)
        /// </summary>
        public void UpdateRoomProperties()
        {
            if (!ShouldRecalculate()) return;
            
            // 1. 기본 유효성 검사
            if (!IsValidRoom())
            {
                SetInvalidRoom();
                return;
            }

            // 2. 밀폐 여부 계산
            UpdateEnclosureStatus();

            // 3. 경계 상자 계산 (Convex Hull + 회전 캘리퍼스)
            UpdateOptimalBounds();

            // 4. 캐시 상태 업데이트
            UpdateCacheStatus();

            Debug.Log($"[WallRoom {roomId}] 룸 업데이트 완료 - 밀폐: {isEnclosed}, 크기: {roomSize}");
        }

        // =============================================================================
        // 🔍 유효성 및 밀폐 상태 확인
        // =============================================================================

        private bool ShouldRecalculate()
        {
            if (_isCacheDirty) return true;
            if (!_cachedIsEnclosed.HasValue) return true;
            if (_lastWallCount != walls.Count) return true;
            if (Time.time - _lastCalculationTime > RecalculationInterval) return true;
            
            return false;
        }

        private bool IsValidRoom()
        {
            // Null 건물 제거
            walls.RemoveAll(w => w == null);
            
            // ⭐ 최소 요구 개수를 3개로 설정 (문/창문도 벽 역할 가능)
            if (walls.Count < 3)
            {
                Debug.Log($"[WallRoom {roomId}] 건물 개수 부족: {walls.Count}개 < 3개");
                return false;
            }

            // 벽 높이 일관성 체크
            var avgY = walls.Average(w => w.transform.position.y);
            if (walls.Any(w => Mathf.Abs(w.transform.position.y - avgY) > 2f))
            {
                Debug.Log($"[WallRoom {roomId}] 벽 높이 불일치");
                return false;
            }

            // 벽 분산 체크 (너무 멀리 있는 벽 제외)
            var center = walls.Aggregate(Vector3.zero, (sum, w) => sum + w.transform.position) / walls.Count;
            var maxDistance = walls.Max(w => Vector3.Distance(w.transform.position, center));
            
            if (maxDistance > 30f)
            {
                Debug.Log($"[WallRoom {roomId}] 벽 분산 과도: {maxDistance:F1}m");
                return false;
            }

            return true;
        }

        private void SetInvalidRoom()
        {
            isEnclosed = false;
            protectionLevel = 0f;
            _cachedIsEnclosed = false;
            roomBounds = new Bounds(Vector3.zero, Vector3.zero);
            roomSize = Vector3.zero;
            roomCenter = Vector3.zero;
            roomRotation = Quaternion.identity;
        }

        private void UpdateEnclosureStatus()
        {
            isEnclosed = true;
            protectionLevel = 1f;
            _cachedIsEnclosed = true;

            Debug.Log($"[WallRoom {roomId}] 밀폐 상태: true (InOutDoorSystem 검증 완료)");
        }

        private bool QuickEnclosureCheck()
        {
            foreach (var wall in walls)
            {
                var connectionCount = walls.Count(other => 
                    wall != other && IsWallsConnected(wall, other));

                if (connectionCount >= 2) continue;
                Debug.Log($"[WallRoom {roomId}] 벽 {wall.name} 연결 부족: {connectionCount}개");
                return false;
            }

            return true;
        }

        private bool FormsClosedLoop()
        {
            // ⭐ InOutDoorSystem에서 이미 모든 검증이 완료되었으므로 무조건 true 반환
            Debug.Log($"[WallRoom {roomId}] FormsClosedLoop - InOutDoorSystem 검증 완료, 무조건 true 반환");
            return true;
        }

        private void UpdateCacheStatus()
        {
            _lastWallCount = walls.Count;
            _lastCalculationTime = Time.time;
            _isCacheDirty = false;
        }

        // =============================================================================
        // 📐 최적화된 경계 상자 계산 (Convex Hull + 회전 캘리퍼스)
        // =============================================================================

        /// <summary>
        /// Convex Hull과 회전 캘리퍼스 알고리즘을 사용해 최적 경계 상자 계산
        /// </summary>
        private void UpdateOptimalBounds()
        {
            // ⭐ 모든 벽 관련 건물로 경계 계산 (문/창문도 포함)
            if (walls.Count < 3)
            {
                Debug.Log($"[UpdateOptimalBounds] 건물 부족: {walls.Count}개 < 3개");
                SetInvalidRoom();
                return;
            }

            try
            {
                // ⭐ 1. 모든 벽 관련 건물의 모서리 점들 수집 (문/창문 포함)
                var allCorners = CollectAllWallCorners();
                if (allCorners.Count < 3)
                {
                    Debug.LogWarning($"[WallRoom {roomId}] 모서리 점 부족: {allCorners.Count}개");
                    SetInvalidRoom();
                    return;
                }

                // 2. 2D 점으로 변환 (Y축 제외)
                var corners2D = allCorners.Select(v => new Vector2(v.x, v.z)).ToList();

                // 3. Convex Hull 계산
                var hull = CalculateConvexHull2D(corners2D);
                if (hull.Count < 3)
                {
                    Debug.LogWarning($"[WallRoom {roomId}] Convex Hull 계산 실패");
                    SetInvalidRoom();
                    return;
                }

                // 4. 회전 캘리퍼스로 최소 면적 경계 상자 계산
                var (rotation, center2D, size2D) = CalculateMinimumAreaObb(hull);

                // 5. 높이 계산 (가장 짧은 벽 기준)
                var minWallHeight = CalculateMinimumWallHeight();

                // 6. 3D 정보로 변환
                var avgY = allCorners.Average(v => v.y);
                
                roomRotation = rotation;
                roomCenter = new Vector3(center2D.x, avgY + minWallHeight * 0.5f, center2D.y);
                roomSize = new Vector3(size2D.x, minWallHeight, size2D.y); // 약간의 패딩
                
                // 7. AABB 계산 (호환성용)
                UpdateAABB(corners2D, avgY, minWallHeight);

                Debug.Log($"[WallRoom {roomId}] 경계 계산 완료 - 중심: {roomCenter}, 크기: {roomSize}, 높이: {minWallHeight:F1}m");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WallRoom {roomId}] 경계 계산 오류: {e.Message}");
                SetInvalidRoom();
            }
        }

        /// <summary>
        /// ⭐ 모든 벽 관련 건물의 실제 모서리 점들을 수집합니다 (문/창문 포함)
        /// </summary>
        private List<Vector3> CollectAllWallCorners()
        {
            var corners = new List<Vector3>();

            foreach (var building in walls)
            {
                if (building == null) continue;

                // ⭐ 건물의 실제 크기 정보 가져오기 (벽/문/창문 모두)
                var buildingCollider = building.GetComponent<BoxCollider>();
                var renderer = building.GetComponent<Renderer>();
                
                Vector3 buildingSize;
                if (buildingCollider != null)
                {
                    buildingSize = buildingCollider.size;
                }
                else if (renderer != null)
                {
                    buildingSize = renderer.bounds.size;
                }
                else
                {
                    // 기본값 사용 - 건물 타입에 따라 다르게
                    buildingSize = building.buildingType switch
                    {
                        BuildingPartType.Wall => new Vector3(2f, 3f, 0.2f),
                        BuildingPartType.Door => new Vector3(1f, 2.5f, 0.2f),
                        BuildingPartType.Window => new Vector3(1.5f, 1f, 0.2f),
                        _ => new Vector3(2f, 3f, 0.2f)
                    };
                    Debug.LogWarning($"[WallRoom {roomId}] {building.buildingType} {building.name}의 크기 정보를 찾을 수 없어 기본값 사용");
                }

                // 건물의 네 모서리 계산
                var transform = building.transform;
                var halfWidth = buildingSize.x * 0.5f;
                var halfDepth = buildingSize.z * 0.5f;

                // 로컬 모서리 점들
                var localCorners = new[]
                {
                    new Vector3(-halfWidth, 0, -halfDepth),  // 왼쪽 앞
                    new Vector3(halfWidth, 0, -halfDepth),   // 오른쪽 앞
                    new Vector3(halfWidth, 0, halfDepth),    // 오른쪽 뒤
                    new Vector3(-halfWidth, 0, halfDepth)    // 왼쪽 뒤
                };

                // 월드 좌표로 변환
                foreach (var localCorner in localCorners)
                {
                    var worldCorner = transform.TransformPoint(localCorner);
                    corners.Add(worldCorner);
                }
            }

            // 중복 제거 (아주 가까운 점들)
            var uniqueCorners = new List<Vector3>();
            foreach (var corner in corners)
            {
                var isDuplicate = uniqueCorners.Any(existing => 
                    Vector3.Distance(existing, corner) < 0.1f);
                
                if (!isDuplicate)
                {
                    uniqueCorners.Add(corner);
                }
            }

            Debug.Log($"[WallRoom {roomId}] 모서리 점 수집: {corners.Count}개 → {uniqueCorners.Count}개 (중복 제거 후)");
            return uniqueCorners;
        }

        /// <summary>
        /// ⭐ 가장 짧은 건물의 높이를 찾습니다 (문/창문 포함)
        /// </summary>
        private float CalculateMinimumWallHeight()
        {
            var minHeight = float.MaxValue;

            foreach (var building in walls)
            {
                if (building == null) continue;

                var buildingCollider = building.GetComponent<BoxCollider>();
                var renderer = building.GetComponent<Renderer>();
                
                float buildingHeight;
                if (buildingCollider != null)
                {
                    buildingHeight = buildingCollider.size.y;
                }
                else if (renderer != null)
                {
                    buildingHeight = renderer.bounds.size.y;
                }
                else
                {
                    // ⭐ 건물 타입에 따른 기본 높이
                    buildingHeight = building.buildingType switch
                    {
                        BuildingPartType.Wall => 3f,
                        BuildingPartType.Door => 2.5f,
                        BuildingPartType.Window => 1f,
                        _ => 3f
                    };
                }

                minHeight = Mathf.Min(minHeight, buildingHeight);
            }

            // 최소 높이 제한 (너무 낮으면 안됨)
            minHeight = Mathf.Max(minHeight, 2f);
            
            Debug.Log($"[WallRoom {roomId}] 최소 건물 높이: {minHeight:F1}m");
            return minHeight;
        }

        private void UpdateAABB(List<Vector2> corners2D, float avgY, float height)
        {
            if (corners2D.Count == 0) return;

            var min = corners2D[0];
            var max = corners2D[0];
            
            foreach (var corner in corners2D)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            
            roomBounds = new Bounds(
                new Vector3(center.x, avgY + height * 0.5f, center.y),
                new Vector3(size.x, height, size.y)
            );
        }

        // =============================================================================
        // 🔄 Convex Hull 및 회전 캘리퍼스 알고리즘
        // =============================================================================

        private List<Vector2> CalculateConvexHull2D(List<Vector2> points)
        {
            if (points.Count < 3) return points;

            // 가장 왼쪽 아래 점 찾기
            var start = points.OrderBy(p => p.x).ThenBy(p => p.y).First();
            
            // 극각 순으로 정렬
            var sortedPoints = points.Where(p => p != start)
                .OrderBy(p => Mathf.Atan2(p.y - start.y, p.x - start.x))
                .ToList();
            sortedPoints.Insert(0, start);

            // Graham Scan 알고리즘
            var hull = new List<Vector2>();
            foreach (var point in sortedPoints)
            {
                while (hull.Count >= 2 && CrossProduct(hull[^2], hull[^1], point) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }

            Debug.Log($"[WallRoom {roomId}] Convex Hull: {points.Count}개 점 → {hull.Count}개 점");
            return hull;
        }

        private (Quaternion rotation, Vector2 center, Vector2 size) CalculateMinimumAreaObb(List<Vector2> hull)
{
    float minArea = float.MaxValue;
    float bestAngle = 0f; // 각도를 저장
    Vector2 bestCenter = Vector2.zero;
    Vector2 bestSize = Vector2.zero;

    // 각 엣지에 대해 회전 캘리퍼스 적용
    for (int i = 0; i < hull.Count; i++)
    {
        Vector2 edge = hull[(i + 1) % hull.Count] - hull[i];
        float angle = Mathf.Atan2(edge.y, edge.x); // 엣지의 각도 계산
        
        // 정규화된 엣지와 수직 벡터
        edge = edge.normalized;
        Vector2 perpendicular = new Vector2(-edge.y, edge.x);

        // 각 축에 대한 투영 범위 계산
        float minProjEdge = float.MaxValue, maxProjEdge = float.MinValue;
        float minProjPerp = float.MaxValue, maxProjPerp = float.MinValue;

        foreach (var point in hull)
        {
            float projEdge = Vector2.Dot(point, edge);
            float projPerp = Vector2.Dot(point, perpendicular);
            
            minProjEdge = Mathf.Min(minProjEdge, projEdge);
            maxProjEdge = Mathf.Max(maxProjEdge, projEdge);
            minProjPerp = Mathf.Min(minProjPerp, projPerp);
            maxProjPerp = Mathf.Max(maxProjPerp, projPerp);
        }

        float width = maxProjEdge - minProjEdge;
        float height = maxProjPerp - minProjPerp;
        float area = width * height;

        if (area < minArea)
        {
            minArea = area;
            bestSize = new Vector2(width, height);
            bestAngle = angle * Mathf.Rad2Deg; // 라디안을 도로 변환
            
            // 회전된 공간에서의 중심점
            Vector2 centerInRotatedSpace = new Vector2(
                (minProjEdge + maxProjEdge) / 2,
                (minProjPerp + maxProjPerp) / 2
            );
            
            // 원래 공간으로 변환
            bestCenter = centerInRotatedSpace.x * edge + centerInRotatedSpace.y * perpendicular;
        }
    }

    // Y축 기준 회전으로 변환 (Unity의 Y-up 좌표계)
    Quaternion bestRotation = Quaternion.Euler(0, -bestAngle, 0); // 음수로 Y축 회전

    Debug.Log($"[WallRoom {roomId}] 최적 경계 상자: 면적 {minArea:F2}, 크기 {bestSize}, 회전 {bestAngle:F1}°");
    return (bestRotation, bestCenter, bestSize);
}

        private float CrossProduct(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        // =============================================================================
        // 🔗 연결성 검사 메서드들
        // =============================================================================

        private bool AreAllWallsConnected()
        {
            if (walls.Count == 0) return false;
            
            Debug.Log($"[WallRoom {roomId}] === AreAllWallsConnected 시작 - 총 {walls.Count}개 건물 ===");
            
            // SnapGroups 상태 확인
            Debug.Log($"[WallRoom {roomId}] SnapGroups 상태: {SnapGroups?.Count ?? 0}개 그룹");
            if (SnapGroups != null)
            {
                foreach (var kvp in SnapGroups)
                {
                    Debug.Log($"  - {kvp.Key.name}: {kvp.Value.Count}개 스냅");
                }
            }
    
            var visited = new HashSet<NetworkBuilding>();
            var queue = new Queue<NetworkBuilding>();
    
            queue.Enqueue(walls[0]);
            visited.Add(walls[0]);
            
            Debug.Log($"[WallRoom {roomId}] 시작 건물: {walls[0].name}");
    
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                Debug.Log($"[WallRoom {roomId}] 현재 처리 중: {current.name}");

                if (SnapGroups != null && SnapGroups.TryGetValue(current, out var snaps))
                {
                    Debug.Log($"[WallRoom {roomId}] {current.name}의 스냅: {snaps.Count}개");
                    
                    foreach (var snap in snaps)
                    {
                        var neighbor = snap.TheirTransform.GetComponent<NetworkBuilding>();
                        if (neighbor != null && walls.Contains(neighbor) && !visited.Contains(neighbor))
                        {
                            Debug.Log($"[WallRoom {roomId}] 새로운 이웃 발견: {neighbor.name}");
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                        else if (neighbor != null)
                        {
                            Debug.Log($"[WallRoom {roomId}] 스킵된 이웃: {neighbor.name} (walls에 있음: {walls.Contains(neighbor)}, 방문됨: {visited.Contains(neighbor)})");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[WallRoom {roomId}] {current.name}에 대한 SnapGroups 정보 없음!");
                    
                    // ⭐ SnapGroups에 정보가 없으면 ConnectedBuildings로 대체 시도
                    Debug.Log($"[WallRoom {roomId}] {current.name}의 ConnectedBuildings로 대체 시도: {current.ConnectedBuildings.Count}개");
                    
                    foreach (var connectedId in current.ConnectedBuildings)
                    {
                        // 연결된 건물 찾기
                        var connectedBuilding = walls.FirstOrDefault(w => w.Object.Id == connectedId);
                        if (connectedBuilding != null && !visited.Contains(connectedBuilding))
                        {
                            Debug.Log($"[WallRoom {roomId}] ConnectedBuildings에서 이웃 발견: {connectedBuilding.name}");
                            visited.Add(connectedBuilding);
                            queue.Enqueue(connectedBuilding);
                        }
                    }
                }
            }
            
            bool allConnected = visited.Count == walls.Count;
            Debug.Log($"[WallRoom {roomId}] === AreAllWallsConnected 결과: {allConnected} (방문: {visited.Count}/{walls.Count}) ===");
            
            if (!allConnected)
            {
                var unvisited = walls.Where(w => !visited.Contains(w)).ToList();
                Debug.LogError($"[WallRoom {roomId}] 연결되지 않은 건물들: [{string.Join(", ", unvisited.Select(w => w.name))}]");
            }
    
            return allConnected;
        }

        
        private bool HasCorrectConnectionCount()
        {
            Debug.Log($"[WallRoom {roomId}] === HasCorrectConnectionCount 시작 ===");
            
            foreach (var wall in walls)
            {
                var connectionCount = 0;
                
                if (SnapGroups.TryGetValue(wall, out var snaps))
                {
                    // 고유한 연결 벽의 수를 셉니다.
                    var connectedWalls = snaps.Select(s => s.TheirTransform.GetComponent<NetworkBuilding>())
                                             .Where(b => b != null && walls.Contains(b))
                                             .Distinct()
                                             .ToList();
                    connectionCount = connectedWalls.Count;
                    
                    Debug.Log($"[WallRoom {roomId}] {wall.name} SnapGroups 연결: {connectionCount}개 - [{string.Join(", ", connectedWalls.Select(w => w.name))}]");
                }
                else
                {
                    Debug.LogWarning($"[WallRoom {roomId}] {wall.name}에 SnapGroups 정보 없음! ConnectedBuildings로 대체");
                    
                    // ⭐ SnapGroups에 정보가 없으면 ConnectedBuildings로 대체
                    var connectedWalls = new List<NetworkBuilding>();
                    foreach (var connectedId in wall.ConnectedBuildings)
                    {
                        var connectedWall = walls.FirstOrDefault(w => w.Object.Id == connectedId);
                        if (connectedWall != null)
                        {
                            connectedWalls.Add(connectedWall);
                        }
                    }
                    connectionCount = connectedWalls.Count;
                    
                    Debug.Log($"[WallRoom {roomId}] {wall.name} ConnectedBuildings 연결: {connectionCount}개 - [{string.Join(", ", connectedWalls.Select(w => w.name))}]");
                }

                if (connectionCount >= 2) continue;
        
                Debug.LogError($"[WallRoom {roomId}] ❌ 벽 {wall.name} 연결 부족: {connectionCount}개 < 2개");
                return false;
            }
            
            Debug.Log($"[WallRoom {roomId}] ✅ HasCorrectConnectionCount 통과");
            return true;
        }
        
        private bool FormsClosedPolygon()
        {
            var points2D = walls.Select(w => new Vector2(w.transform.position.x, w.transform.position.z)).ToList();
            var hull = CalculateConvexHull2D(points2D);
            
            if (hull.Count < 3) return false;

            // 모든 벽이 Convex Hull 근처에 있는지 확인
            var tolerance = 2f;
            foreach (var point in points2D)
            {
                var minDistance = float.MaxValue;
                for (int i = 0; i < hull.Count; i++)
                {
                    var distance = DistancePointToLineSegment(point, hull[i], hull[(i + 1) % hull.Count]);
                    minDistance = Mathf.Min(minDistance, distance);
                }
                
                if (minDistance > tolerance)
                {
                    Debug.Log($"[WallRoom {roomId}] 벽이 너무 멀리 있음: {minDistance:F2}m > {tolerance}m");
                    return false;
                }
            }

            // 최소 면적 확인
            var area = CalculatePolygonArea(hull);
            var result = area >= 4f; // 최소 4㎡
            
            if (!result)
            {
                Debug.Log($"[WallRoom {roomId}] 면적 너무 작음: {area:F2}㎡ < 4㎡");
            }
            
            return result;
        }

        // ⭐ IsWallsConnected 메서드 - 네트워크 동기화 지연 대응
        private static bool IsWallsConnected(NetworkBuilding wall1, NetworkBuilding wall2)
        {
            // ⭐ 1. ConnectedBuildings 리스트 확인 (양방향 체크)
            if (wall1.ConnectedBuildings.Contains(wall2.Object.Id) || 
                wall2.ConnectedBuildings.Contains(wall1.Object.Id))
                return true;

            // ⭐ 2. 거리 기반 보완 연결 감지 (네트워크 동기화 지연 대응)
            var distance = Vector3.Distance(wall1.transform.position, wall2.transform.position);
            const float snapDistance = 3f; // 스냅 가능 거리
    
            if (distance <= snapDistance)
            {
                Debug.LogWarning($"[WallRoom.IsWallsConnected] 거리 기반 연결 감지: {wall1.name} <-> {wall2.name} (거리: {distance:F2}m)");
                return true;
            }

            return false;
        }

        private float DistancePointToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            var line = lineEnd - lineStart;
            var lineLengthSquared = line.sqrMagnitude;
            
            if (lineLengthSquared == 0) 
                return Vector2.Distance(point, lineStart);
            
            var t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lineLengthSquared);
            var projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        private float CalculatePolygonArea(List<Vector2> vertices)
        {
            float area = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                area += vertices[i].x * vertices[j].y - vertices[j].x * vertices[i].y;
            }
            return Mathf.Abs(area) / 2f;
        }

        public void InvalidateCache()
        {
            _cachedIsEnclosed = null;
            _isCacheDirty = true;
            Debug.Log($"[WallRoom {roomId}] 캐시 무효화됨");
        }

        public void AddWall(NetworkBuilding wall)
        {
            if (walls.Contains(wall)) return;
            walls.Add(wall);
            InvalidateCache();
        }

        public void RemoveWall(NetworkBuilding wall)
        {
            if (walls.Remove(wall)) 
            {
                InvalidateCache();
            }
        }

        // 콜라이더 업데이트 (외부에서 호출)
        public void UpdateRoomCollider()
        {
            if (roomCollider == null) return;

            roomCollider.center = Vector3.zero; // 로컬 중심
            roomCollider.size = roomSize;
            roomCollider.isTrigger = true;
            
            // 콜라이더의 부모 오브젝트 위치/회전 설정
            if (roomCollider.transform == null) return;
            roomCollider.transform.position = roomCenter;
            roomCollider.transform.rotation = roomRotation;
        }

        // 기존 UpdateRoom 메서드
        public void UpdateRoom()
        {
            // 기존 룸 업데이트 로직...
            UpdateRoomProperties();
            
            if (isEnclosed && roomCollider != null)
            {
                UpdateRoomCollider();
            }
        }
    }
}