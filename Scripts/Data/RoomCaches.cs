using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

namespace Data
{
	[System.Serializable]
        public class RoomCaches
        {
            // 메인 룸 데이터 저장소
            private Dictionary<int, WallRoom> _roomDataCache = new Dictionary<int, WallRoom>();
            
            // RoomInfo 컴포넌트 참조 (UI 업데이트용)
            private Dictionary<int, RoomInfo> _roomInfoComponents = new Dictionary<int, RoomInfo>();
            
            // 위치 기반 빠른 검색용 캐시
            private Dictionary<Vector3, int> _positionToRoomCache = new Dictionary<Vector3, int>();
            
            // 캐시 업데이트 관리
            private float _lastPositionCacheUpdate;
            private bool _isPositionCacheDirty = true;
            private const float PositionCacheInterval = 2f;
            
            public int Count => _roomDataCache.Count;
            public IEnumerable<WallRoom> AllRooms => _roomDataCache.Values;
            public IEnumerable<int> AllRoomIds => _roomDataCache.Keys;
            
            // 룸 추가
            public void AddRoom(WallRoom room)
            {
                _roomDataCache[room.roomId] = room;
                MarkPositionCacheDirty();
                
                Debug.Log($"룸 추가: ID {room.roomId}, 총 룸 개수: {_roomDataCache.Count}");
            }
            
            // 룸 제거
            public bool RemoveRoom(int roomId)
            {
                if (_roomDataCache.Remove(roomId))
                {
                    _roomInfoComponents.Remove(roomId);
                    MarkPositionCacheDirty();
                    
                    Debug.Log($"룸 제거: ID {roomId}, 남은 룸 개수: {_roomDataCache.Count}");
                    return true;
                }
                return false;
            }
            
            // 룸 데이터 가져오기
            public WallRoom GetRoom(int roomId)
            {
                return _roomDataCache.GetValueOrDefault(roomId);
            }

            public RoomInfo GetRoomInfo(int roomId)
            {
                return _roomInfoComponents.GetValueOrDefault(roomId);
            }
            
            // 위치로 룸 찾기 (빠른 검색)
            public WallRoom GetRoomByPosition(Vector3 position)
            {
                UpdatePositionCacheIfNeeded();
                
                var gridPos = SnapToGrid(position);
                if (_positionToRoomCache.TryGetValue(gridPos, out int roomId))
                {
                    return GetRoom(roomId);
                }
                
                return null;
            }
            
            public bool IsWallInAnyRoom(NetworkId wallId)
            {
                // 모든 룸을 순회
                foreach (var room in _roomDataCache.Values)
                {
                    // 룸의 벽 리스트에 해당 ID를 가진 벽이 있는지 확인
                    // LINQ의 .Any()를 사용하면 더 간결합니다.
                    if (room.walls.Any(wall => wall != null && wall.Object.Id == wallId))
                    {
                        return true; // 찾았으면 즉시 true 반환
                    }
                }
            
                return false; // 모든 룸을 확인했지만 없으면 false 반환
            }
            
            // 위치가 실내인지 확인
            public bool IsPositionIndoor(Vector3 position)
            {
                var room = GetRoomByPosition(position);
                return room is { isEnclosed: true };
            }
            
            // RoomInfo 컴포넌트 등록
            public void RegisterRoomInfo(int roomId, RoomInfo roomInfo)
            {
                _roomInfoComponents[roomId] = roomInfo;
            }
            
            // RoomInfo 컴포넌트 해제
            public void UnregisterRoomInfo(int roomId)
            {
                _roomInfoComponents.Remove(roomId);
            }
            
            // 특정 룸의 RoomInfo 업데이트
            public void RefreshRoomInfo(int roomId)
            {
                if (!_roomInfoComponents.TryGetValue(roomId, out var roomInfo)) return;
                
                roomInfo.RefreshRoomData();
            }
            
            // 모든 RoomInfo 업데이트
            public void RefreshAllRoomInfos()
            {
                foreach (var roomInfo in _roomInfoComponents.Values)
                {
                    roomInfo.RefreshRoomData();
                }
            }
            
            // 룸 존재 확인
            public bool ContainsRoom(int roomId)
            {
                return _roomDataCache.ContainsKey(roomId);
            }
            
            // 룸 업데이트 (데이터만 교체, 위치 캐시는 자동으로 dirty 마킹)
            public void UpdateRoom(WallRoom room)
            {
                if (_roomDataCache.ContainsKey(room.roomId))
                {
                    _roomDataCache[room.roomId] = room;
                    MarkPositionCacheDirty();
                }
            }
            
            // 유효하지 않은 룸들 정리
            public List<int> CleanupInvalidRooms()
            {
                var toRemove = new List<int>();
                
                foreach (var kvp in _roomDataCache)
                {
                    var room = kvp.Value;
                    
                    // null 체크 및 벽 개수 확인
                    room.walls.RemoveAll(wall => wall == null);
                    
                    if (room.walls.Count < 4)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var roomId in toRemove)
                {
                    RemoveRoom(roomId);
                }
                
                return toRemove;
            }
            
            // 위치 캐시 더티 마킹
            private void MarkPositionCacheDirty()
            {
                _isPositionCacheDirty = true;
            }
            
            // 필요시 위치 캐시 업데이트
            private void UpdatePositionCacheIfNeeded()
            {
                if (!_isPositionCacheDirty && 
                    Time.time - _lastPositionCacheUpdate < PositionCacheInterval)
                {
                    return;
                }
                
                UpdatePositionCache();
            }
            
            // 위치 캐시 전체 업데이트
            private void UpdatePositionCache()
            {
                _positionToRoomCache.Clear();
                
                foreach (var kvp in _roomDataCache)
                {
                    var room = kvp.Value;
                    var roomId = kvp.Key;
                    
                    if (!room.isEnclosed) continue;
                    
                    var bounds = room.roomBounds;
                    var gridSize = 1f;
                    
                    for (float x = bounds.min.x; x <= bounds.max.x; x += gridSize)
                    {
                        for (float z = bounds.min.z; z <= bounds.max.z; z += gridSize)
                        {
                            var gridPos = new Vector3(x, bounds.center.y, z);
                            _positionToRoomCache[SnapToGrid(gridPos)] = roomId;
                        }
                    }
                }
                
                _lastPositionCacheUpdate = Time.time;
                _isPositionCacheDirty = false;
                
                Debug.Log($"위치 캐시 업데이트 완료: {_positionToRoomCache.Count}개 위치 캐시됨");
            }
            
            // 강제 위치 캐시 업데이트
            public void ForceUpdatePositionCache()
            {
                UpdatePositionCache();
            }
            
            // 통계 정보
            public void LogCacheStats()
            {
                Debug.Log($"=== 룸 캐시 통계 ===");
                Debug.Log($"총 룸 개수: {_roomDataCache.Count}");
                Debug.Log($"RoomInfo 컴포넌트: {_roomInfoComponents.Count}");
                Debug.Log($"위치 캐시: {_positionToRoomCache.Count}");
                Debug.Log($"밀폐된 룸: {_roomDataCache.Values.Count(r => r.isEnclosed)}");
            }
            
            // 메모리 정리
            public void Clear()
            {
                _roomDataCache.Clear();
                _roomInfoComponents.Clear();
                _positionToRoomCache.Clear();
                _isPositionCacheDirty = true;
            }
            
            private Vector3 SnapToGrid(Vector3 position)
            {
                const float gridSize = 1f;
                return new Vector3(
                    Mathf.Round(position.x / gridSize) * gridSize,
                    Mathf.Round(position.y / gridSize) * gridSize,
                    Mathf.Round(position.z / gridSize) * gridSize
                );
            }
        }
}