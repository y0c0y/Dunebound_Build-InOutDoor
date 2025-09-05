using System.Collections.Generic;
using System.Linq;
using Placeable;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// Union-Find를 사용한 건물 연결성 최적화 도구
    /// </summary>
    public class UnionFindOptimizer
    {
        private readonly Dictionary<NetworkBuilding, int> _buildingToId = new();
        private readonly List<NetworkBuilding> _idToBuilding = new();
        private int[] _parent;
        private int[] _rank;
        private int _nodeCount;

        /// <summary>
        /// 벽 관련 건물들을 연결 컴포넌트별로 그룹화합니다
        /// </summary>
        public List<List<NetworkBuilding>> GroupBuildingsByUnionFind(List<NetworkBuilding> buildings)
        {
            Debug.Log($"[UnionFindOptimizer] === 시작 - 건물 {buildings.Count}개 ===");
            
            // 1. 초기화
            Initialize(buildings);
            
            // 2. 연결된 건물들을 Union 처리
            ProcessConnections(buildings);
            
            // 3. 거리 기반 보완 연결 처리 (네트워크 동기화 지연 대응)
            ProcessFallbackConnections(buildings);
            
            // 4. 각 컴포넌트별로 그룹화
            var components = GroupByComponents();
            
            Debug.Log($"[UnionFindOptimizer] === 완료 - {components.Count}개 컴포넌트 생성 ===");
            return components;
        }
        
        /// <summary>
        /// ⭐ 거리 기반 보완 연결 처리 (ConnectedBuildings 리스트에 누락된 연결 복구)
        /// </summary>
        private void ProcessFallbackConnections(List<NetworkBuilding> buildings)
        {
            Debug.Log($"[UnionFindOptimizer] === 거리 기반 보완 연결 시작 ===");
            
            int fallbackConnections = 0;
            const float maxSnapDistance = 3f;
            
            for (int i = 0; i < buildings.Count; i++)
            {
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    var building1 = buildings[i];
                    var building2 = buildings[j];
                    
                    // 이미 연결된 경우 스킵
                    if (AreConnected(building1, building2)) continue;
                    
                    // 거리 확인
                    var distance = Vector3.Distance(building1.transform.position, building2.transform.position);
                    if (distance <= maxSnapDistance)
                    {
                        Debug.Log($"[UnionFindOptimizer] 거리 기반 연결 발견: {building1.name} <-> {building2.name} (거리: {distance:F2}m)");
                        
                        var id1 = _buildingToId[building1];
                        var id2 = _buildingToId[building2];
                        Union(id1, id2);
                        fallbackConnections++;
                    }
                }
            }
            
            Debug.Log($"[UnionFindOptimizer] 거리 기반 보완 연결 완료 - {fallbackConnections}개 추가 연결");
        }

        private void Initialize(List<NetworkBuilding> buildings)
        {
            _nodeCount = buildings.Count;
            _parent = new int[_nodeCount];
            _rank = new int[_nodeCount];
            
            _buildingToId.Clear();
            _idToBuilding.Clear();
            
            // 각 건물에 ID 할당 및 자기 자신을 부모로 초기화
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                _buildingToId[building] = i;
                _idToBuilding.Add(building);
                _parent[i] = i;
                _rank[i] = 0;
            }
            
            Debug.Log($"[UnionFindOptimizer] 초기화 완료 - {_nodeCount}개 노드");
        }

        private void ProcessConnections(List<NetworkBuilding> buildings)
        {
            int connectionCount = 0;
            int totalConnectionAttempts = 0;
            
            foreach (var building in buildings)
            {
                var buildingId = _buildingToId[building];
                Debug.Log($"[UnionFindOptimizer] {building.name}({building.buildingType}) 연결 확인 중... ConnectedBuildings: {building.ConnectedBuildings.Count}개");
                
                // 연결된 건물들과 Union 수행
                foreach (var connectedId in building.ConnectedBuildings)
                {
                    totalConnectionAttempts++;
                    
                    // 연결된 건물이 현재 그룹에 있는지 확인
                    var connectedBuilding = buildings.FirstOrDefault(b => b.Object.Id == connectedId);
                    if (connectedBuilding != null && _buildingToId.TryGetValue(connectedBuilding, out var connectedBuildingId))
                    {
                        Union(buildingId, connectedBuildingId);
                        connectionCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[UnionFindOptimizer] 연결 실패: {building.name} -> ConnectedId({connectedId}) - 그룹에 없거나 찾을 수 없음");
                    }
                }
            }
            
            Debug.Log($"[UnionFindOptimizer] 연결 처리 완료 - {connectionCount}개 연결 성공 / {totalConnectionAttempts}개 시도");
        }

        private List<List<NetworkBuilding>> GroupByComponents()
        {
            var componentMap = new Dictionary<int, List<NetworkBuilding>>();
            
            // 각 건물의 루트를 찾아서 컴포넌트별로 그룹화
            for (int i = 0; i < _nodeCount; i++)
            {
                var root = Find(i);
                var building = _idToBuilding[i];
                
                if (!componentMap.TryGetValue(root, out var component))
                {
                    component = new List<NetworkBuilding>();
                    componentMap[root] = component;
                }
                
                component.Add(building);
            }
            
            var result = componentMap.Values.ToList();
            
            // 통계 출력
            foreach (var component in result)
            {
                Debug.Log($"[UnionFindOptimizer] 컴포넌트: {component.Count}개 건물");
            }
            
            return result;
        }

        /// <summary>
        /// Find 연산 (Path Compression 적용)
        /// </summary>
        private int Find(int x)
        {
            if (_parent[x] != x)
            {
                _parent[x] = Find(_parent[x]); // Path Compression
            }
            return _parent[x];
        }

        /// <summary>
        /// Union 연산 (Union by Rank 적용)
        /// </summary>
        private void Union(int x, int y)
        {
            var rootX = Find(x);
            var rootY = Find(y);
            
            if (rootX == rootY) return; // 이미 같은 컴포넌트
            
            // Union by Rank
            if (_rank[rootX] < _rank[rootY])
            {
                _parent[rootX] = rootY;
            }
            else if (_rank[rootX] > _rank[rootY])
            {
                _parent[rootY] = rootX;
            }
            else
            {
                _parent[rootY] = rootX;
                _rank[rootX]++;
            }
        }

        /// <summary>
        /// 두 건물이 같은 컴포넌트에 속하는지 확인
        /// </summary>
        public bool AreConnected(NetworkBuilding building1, NetworkBuilding building2)
        {
            if (!_buildingToId.TryGetValue(building1, out var id1) || 
                !_buildingToId.TryGetValue(building2, out var id2))
                return false;
                
            return Find(id1) == Find(id2);
        }

        /// <summary>
        /// 컴포넌트의 크기 반환
        /// </summary>
        public int GetComponentSize(NetworkBuilding building)
        {
            if (!_buildingToId.TryGetValue(building, out var id))
                return 0;
                
            var root = Find(id);
            return Enumerable.Range(0, _nodeCount).Count(i => Find(i) == root);
        }
    }
}