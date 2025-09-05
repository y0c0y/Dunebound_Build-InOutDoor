using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Data;
using Data.Building;
using Fusion;
using Placeable;
using UnityEngine;
using VolumetricFogAndMist2;

namespace Manager
{
	public class InOutDoorSystem : NetworkBehaviour
	{
		[Header("Chunk Settings")] [SerializeField]
		private float chunkSize = 50f;

		[SerializeField] private int activeChunkRadius = 2;
		[SerializeField] private float chunkUpdateInterval = 1f;

		[Header("Network Settings")] [SerializeField]
		private int maxRoomsPerChunk = 20;

		[Header("Fog of War Settings")]
		public float fogOfWarSize;

		[Header("Room effects")]
		public List<StatModifier> roomEffects;
		
		[Header("Volumetric Fog Settings")]
		[Tooltip("방 주변에서 VolumetricFog를 찾을 검색 반경")]
		public float fogSearchRadius = 10f;
		
		private readonly Dictionary<Vector2Int, ChunkData> _chunkMap = new();

		private RoomCaches _roomCache;
		private GameObject _roomCollidersParent;
		private static BuildingSystem _buildingSystem;


		[Networked] [Capacity(100)] public NetworkLinkedList<int> ActiveRoomIds { get; }

		public static InOutDoorSystem Instance { get; private set; }

		// 룸이 생성/파괴될 때 이벤트
		public static event Action<GameObject> OnRoomCreated;
		public static event Action<int> OnRoomDestroyed;

		public override void Spawned()
		{
			if (!HasStateAuthority) return;
			Instance = this;
			_roomCache = new RoomCaches();
			_roomCollidersParent = new GameObject("RoomColliders");
			_roomCollidersParent.transform.SetParent(transform);

			if (BuildingSystem.Instance != null)
			{
				_buildingSystem = BuildingSystem.Instance;

				// BuildingSystem 이벤트 구독
				_buildingSystem.OnBuildingPlaced += HandleBuildingPlaced;
				_buildingSystem.OnBuildingRemoved += HandleBuildingRemoved;
				_buildingSystem.OnBuildingsConnected += HandleBuildingsConnected;
				_buildingSystem.OnBuildingsDisconnected += HandleBuildingsDisconnected;
			}

			InvokeRepeating(nameof(UpdateChunkSystem), 1f, chunkUpdateInterval);
			InvokeRepeating(nameof(UpdateActiveRooms), 3f, 5f);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (!hasState) return;

			// 이벤트 구독 해제
			if (_buildingSystem != null)
			{
				_buildingSystem.OnBuildingPlaced -= HandleBuildingPlaced;
				_buildingSystem.OnBuildingRemoved -= HandleBuildingRemoved;
				_buildingSystem.OnBuildingsConnected -= HandleBuildingsConnected;
				_buildingSystem.OnBuildingsDisconnected -= HandleBuildingsDisconnected;
			}

			Destroy(Instance);
		}

		private void UpdateActiveRooms()
		{
			if (!HasStateAuthority) return;

			// 현재 활성화된 모든 룸을 순회
			foreach (var roomId in ActiveRoomIds)
			{
				var room = _roomCache.GetRoom(roomId);
				if (room == null) continue;

				// 1. 방의 환경 상태 주기적 갱신
				// (예: 낮/밤 사이클에 따른 빛 변화, 문 개폐 상태 반영 등)
				UpdateRoomEnvironment(room);

				// 2. (선택) 방 무결성 검사
				// 벽이 유효하지 않은 경우 방을 제거하는 로직
				if (room.walls.All(wall => wall != null)) continue;
				Debug.LogWarning($"[UpdateActiveRooms] 룸 {roomId}에서 유효하지 않은 벽 발견. 룸을 재검사합니다.");
				var chunkCoord = GetChunkCoordinate(room.roomBounds.center);

				if (!_chunkMap.TryGetValue(chunkCoord, out var chunk)) return;

				UpdateRoomInChunk(roomId, chunk); // 기존의 룸 업데이트/제거 로직 재사용

				// 3. (선택) 네트워크로 동기화가 필요한 다른 속성들 업데이트
				// 예: room.Temperature = CalculateTemperature(room);
			}
		}

		// 청크 좌표 계산
		private Vector2Int GetChunkCoordinate(Vector3 worldPosition)
		{
			return new Vector2Int(
				Mathf.FloorToInt(worldPosition.x / chunkSize),
				Mathf.FloorToInt(worldPosition.z / chunkSize)
			);
		}

		// 디버그 정보
		[ContextMenu("Show Chunk Statistics")]
		public void ShowChunkStatistics()
		{
			Debug.Log("=== 청크 시스템 통계 ===");
			Debug.Log($"총 청크 수: {_chunkMap.Count}");
			Debug.Log($"활성 청크 수: {_chunkMap.Count(kvp => kvp.Value.IsActive)}");
			Debug.Log($"총 룸 수: {_roomCache.Count}");
			Debug.Log($"네트워크 동기화 룸: {ActiveRoomIds.Count}/{ActiveRoomIds.Capacity}");

			foreach (var kvp in _chunkMap.Where(kvp => kvp.Value.IsActive))
			{
				var chunk = kvp.Value;
				Debug.Log($"  청크 {chunk.Coordinate}: 룸 {chunk.RoomIds.Count}개, 건물 {chunk.Buildings.Count}개");
			}
		}

		// =============================================================================
		// 공개 API - 이제 모든 메서드가 통합된 캐시를 사용
		// =============================================================================

		// 위치 기반 룸 찾기 (빠른 조회)
		public WallRoom GetRoom(Vector3 position)
		{
			var chunkCoord = GetChunkCoordinate(position);

			return !_chunkMap.TryGetValue(chunkCoord, out var chunk)
				? null
				: chunk.RoomIds.Select(roomId => _roomCache.GetRoom(roomId))
					.FirstOrDefault(room => room != null && room.roomBounds.Contains(position));
		}


		public RoomInfo GetRoomInfo(int roomId)
		{
			var roomInfo = _roomCache.GetRoomInfo(roomId);
			return roomInfo == null ? null : roomInfo;
		}

		// 실내 확인 (빠른 조회)
		public bool IsIndoor(Vector3 position)
		{
			var room = GetRoom(position);
			return room is { isEnclosed: true };
		}

		public WallRoom GetRoomById(int roomId)
		{
			return _roomCache.GetRoom(roomId);
		}

		public List<int> GetAllRoomIds()
		{
			return _roomCache.AllRoomIds.ToList();
		}

		public void NotifyRoomInfoUpdate(int roomId)
		{
			_roomCache.RefreshRoomInfo(roomId);
		}

		// RoomInfo 컴포넌트 등록 (RoomInfo에서 호출)
		private void RegisterRoomInfo(int roomId, RoomInfo roomInfo)
		{
			_roomCache.RegisterRoomInfo(roomId, roomInfo);
		}

		private void UnregisterRoomInfo(int roomId)
		{
			_roomCache.UnregisterRoomInfo(roomId);
		}

		private void HandleBuildingRemoved(NetworkBuilding removedBuilding)
		{
			if (!removedBuilding.IsWallRelated()) return;

			var chunkCoord = GetChunkCoordinate(removedBuilding.transform.position);
			if (_chunkMap.TryGetValue(chunkCoord, out var chunk))
			{
				RemoveBuildingFromChunk(removedBuilding, chunk);
			}
		}

		private void HandleBuildingPlaced(NetworkBuilding placedBuilding)
		{
			Debug.Log($"[HandleBuildingPlaced] 건물 배치됨: {placedBuilding.name} (ID: {placedBuilding.Object.Id})");

			if (!placedBuilding.IsWallRelated())
			{
				Debug.Log($"[HandleBuildingPlaced] {placedBuilding.buildingType} 벽 관련 건물 아님");
				return;
			}

			var chunkCoord = GetChunkCoordinate(placedBuilding.transform.position);
			Debug.Log($"[HandleBuildingPlaced] 청크 좌표: {chunkCoord}");

			if (!_chunkMap.TryGetValue(chunkCoord, out var chunk))
			{
				chunk = new ChunkData(chunkCoord);
				_chunkMap[chunkCoord] = chunk;
				Debug.Log($"[HandleBuildingPlaced] 새 청크 생성: {chunkCoord}");
			}

			// 중복 체크를 더 엄격하게
			var alreadyExists = chunk.Buildings.Any(b => b.Object.Id == placedBuilding.Object.Id);
			if (alreadyExists)
			{
				Debug.LogWarning($"[HandleBuildingPlaced] 이미 존재하는 건물: {placedBuilding.name} (ID: {placedBuilding.Object.Id})");
				return;
			}
			
			chunk.Buildings.Add(placedBuilding);
			Debug.Log($"[HandleBuildingPlaced] 청크에 건물 추가. 현재 건물 수: {chunk.Buildings.Count}");
			
			// ⭐ 새로 배치된 건물과 근접한 기존 건물들을 자동으로 연결 등록
			AutoRegisterNearbyConnections(placedBuilding, chunk);
			
		
			
			Debug.Log($"[HandleBuildingPlaced] === 목록 끝 ===");
		}

		/// <summary>
		/// ⭐ 새로 배치된 건물과 물리적으로 연결 가능한 근접 건물들을 자동으로 RegisterSnapConnection 호출
		/// </summary>
		private void AutoRegisterNearbyConnections(NetworkBuilding newBuilding, ChunkData chunk)
		{
			Debug.Log($"[AutoRegisterNearbyConnections] === 자동 연결 등록 시작: {newBuilding.name} ===");
			
			if (!HasStateAuthority)
			{
				Debug.Log($"[AutoRegisterNearbyConnections] StateAuthority가 아니므로 연결 등록 건너뜀");
				return;
			}
			
			// 청크 내의 다른 벽 관련 건물들과 연결 가능성 검사
			var wallRelatedBuildings = chunk.Buildings.Where(b => b != newBuilding && b.IsWallRelated()).ToList();
			
			Debug.Log($"[AutoRegisterNearbyConnections] 검사할 기존 건물: {wallRelatedBuildings.Count}개");
			
			foreach (var existingBuilding in wallRelatedBuildings)
			{
				// 물리적으로 연결 가능한지 확인
				if (IsPhysicallyConnected(newBuilding, existingBuilding))
				{
					Debug.Log($"[AutoRegisterNearbyConnections] ✅ 자동 연결 등록: {newBuilding.name} <-> {existingBuilding.name}");
					
					// RegisterSnapConnection 호출하여 ConnectedBuildings에 등록
					newBuilding.RegisterSnapConnection(existingBuilding);
					
					// BuildingsConnected 이벤트도 발생시켜서 방 생성 로직이 트리거되도록 함
					if (_buildingSystem != null)
					{
						_buildingSystem.NotifyBuildingPlaced(newBuilding, existingBuilding);
					}
					else
					{
						// BuildingSystem이 없는 경우 직접 이벤트 발생
						HandleBuildingsConnected(newBuilding, existingBuilding);
					}
				}
				else
				{
					Debug.Log($"[AutoRegisterNearbyConnections] ❌ 연결 불가: {newBuilding.name} <-> {existingBuilding.name}");
				}
			}
			
			Debug.Log($"[AutoRegisterNearbyConnections] === 자동 연결 등록 완료 ===");
		}

		private void HandleBuildingsConnected(NetworkBuilding newlyConnectedWall, NetworkBuilding existingWall)
		{
			Debug.Log($"[HandleBuildingsConnected] 건물 연결됨: {newlyConnectedWall.name} <-> {existingWall.name}");
			Debug.Log($"[HandleBuildingsConnected] 타입: {newlyConnectedWall.buildingType} <-> {existingWall.buildingType}");

			// ⭐ 둘 중 하나라도 벽 관련 건물이면 처리 (벽+바닥, 벽+지붕 등도 고려)
			if (!newlyConnectedWall.IsWallRelated() && !existingWall.IsWallRelated())
			{
				Debug.Log($"[HandleBuildingsConnected] 둘 다 벽 관련 건물이 아니므로 무시");
				return;
			}

			// ⭐ 벽 관련 건물들만 방 생성 로직에 참여
			var wallRelatedBuildings = new List<NetworkBuilding>();
			if (newlyConnectedWall.IsWallRelated()) wallRelatedBuildings.Add(newlyConnectedWall);
			if (existingWall.IsWallRelated()) wallRelatedBuildings.Add(existingWall);
			
			Debug.Log($"[HandleBuildingsConnected] 방 생성 참여 건물: {string.Join(", ", wallRelatedBuildings.Select(b => $"{b.name}({b.buildingType})"))}");

			// ⭐ 중복 방지: 각 청크에 대해 한 번만 방 생성 시도
			var processedChunks = new HashSet<Vector2Int>();
			
			foreach (var wallBuilding in wallRelatedBuildings)
			{
				var chunkCoord = GetChunkCoordinate(wallBuilding.transform.position);
				
				// 이미 처리된 청크는 스킵
				if (processedChunks.Contains(chunkCoord))
				{
					Debug.Log($"[HandleBuildingsConnected] 청크 {chunkCoord} 이미 처리됨, 스킵");
					continue;
				}
				
				Debug.Log($"[HandleBuildingsConnected] {wallBuilding.name}의 청크: {chunkCoord}");
				
				if (_chunkMap.TryGetValue(chunkCoord, out var chunk))
				{
					// 청크에 건물이 없으면 추가
					if (chunk.Buildings.All(b => b.Object.Id != wallBuilding.Object.Id))
					{
						Debug.LogWarning($"[HandleBuildingsConnected] {wallBuilding.name}이 청크에 없음! 추가합니다.");
						chunk.Buildings.Add(wallBuilding);
					}
					
					Debug.Log($"[HandleBuildingsConnected] CheckWallGroupForRoom 호출 - 청크 {chunkCoord} (트리거: {wallBuilding.name})");
					CheckWallGroupForRoom(wallBuilding, chunk);
					processedChunks.Add(chunkCoord);
				}
				else
				{
					Debug.LogError($"[HandleBuildingsConnected] 청크 {chunkCoord}를 찾을 수 없음!");
				}
			}
    
			// 크로스 청크 룸 처리는 그대로 유지
			Debug.Log($"[HandleBuildingsConnected] CheckCrossChunkRoom 호출");
			CheckCrossChunkRoom(newlyConnectedWall, existingWall);
			
			Debug.Log($"[HandleBuildingsConnected] 처리 완료");
		}

		// 청크 경계에 걸친 룸 확인
		private void CheckCrossChunkRoom(NetworkBuilding wall1, NetworkBuilding wall2)
		{
			var chunk1Coord = GetChunkCoordinate(wall1.transform.position);
			var chunk2Coord = GetChunkCoordinate(wall2.transform.position);

			// 같은 청크면 이미 처리됨
			if (chunk1Coord == chunk2Coord) return;

			Debug.Log($"[CheckCrossChunkRoom] 청크 경계 룸 체크: {chunk1Coord} <-> {chunk2Coord}");

			// 두 청크의 모든 벽을 합쳐서 체크
			var allWalls = new List<NetworkBuilding>();

			if (_chunkMap.TryGetValue(chunk1Coord, out var chunk1))
			{
				allWalls.AddRange(chunk1.Buildings.Where(b => b.IsWallRelated()));
			}

			if (_chunkMap.TryGetValue(chunk2Coord, out var chunk2))
			{
				allWalls.AddRange(chunk2.Buildings.Where(b => b.IsWallRelated()));
			}

			var connectedGroup = FindConnectedGroupFor(wall1, allWalls);

			if (connectedGroup.Count < 4) return;
			Debug.Log($"[CheckCrossChunkRoom] 청크 경계 룸 가능성: {connectedGroup.Count}개 벽");

			var formationResult = CheckWallsCanFormRoom(connectedGroup);
			if (!formationResult.CanFormRoom) return;
			// 룸의 중심 청크에 생성
			var roomCenter = GetRoomCenter(connectedGroup);
			var roomChunkCoord = GetChunkCoordinate(roomCenter);

			if (!_chunkMap.TryGetValue(roomChunkCoord, out var roomChunk)) return;
			Debug.Log($"[CheckCrossChunkRoom] 청크 {roomChunkCoord}에 크로스 청크 룸 생성!");
			_ = TryCreateRoomInChunkAsync(connectedGroup, roomChunk, formationResult.SnapGroups);
		}
		
		private Vector3 GetRoomCenter(List<NetworkBuilding> walls)
		{
			var center = Vector3.zero;
			foreach (var wall in walls)
			{
				center += wall.transform.position;
			}

			return center / walls.Count;
		}

// FindConnectedGroupFor 메서드도 개선
		private static List<NetworkBuilding> FindConnectedGroupFor(NetworkBuilding startWall,
			List<NetworkBuilding> allWalls)
		{
			var group = new List<NetworkBuilding>();
			var visited = new HashSet<NetworkBuilding>();
			var queue = new Queue<NetworkBuilding>();

			queue.Enqueue(startWall);
			visited.Add(startWall);

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				group.Add(current);

				foreach (var other in allWalls.Where(other => !visited.Contains(other) && IsWallsConnected(current, other)))
				{
					visited.Add(other);
					queue.Enqueue(other);
				}
			}

			Debug.Log($"[FindConnectedGroup] 총 {group.Count}개 벽이 연결됨");
			return group;
		}

		private void HandleBuildingsDisconnected(NetworkBuilding disconnectedWall, NetworkBuilding remainingWall)
		{
			if (!disconnectedWall.IsWallRelated() || !remainingWall.IsWallRelated())
				return;

			// 연결이 끊긴 벽이 속한 룸들을 재검사
			var disconnectedChunkCoord = GetChunkCoordinate(disconnectedWall.transform.position);
			if (_chunkMap.TryGetValue(disconnectedChunkCoord, out var disconnectedChunk))
			{
				UpdateAffectedRooms(disconnectedWall, disconnectedChunk);
			}

			// 남은 벽이 속한 룸들도 재검사
			var remainingChunkCoord = GetChunkCoordinate(remainingWall.transform.position);
			if (remainingChunkCoord != disconnectedChunkCoord &&
			    _chunkMap.TryGetValue(remainingChunkCoord, out var remainingChunk))
			{
				UpdateAffectedRooms(remainingWall, remainingChunk);
			}
		}

		private void CheckWallGroupForRoom(NetworkBuilding triggerWall, ChunkData chunk)
		{
			Debug.Log($"[CheckWallGroupForRoom] === 시작 - 트리거: {triggerWall.name} ({triggerWall.buildingType}) ===");
			
			var wallRelatedBuildings = chunk.Buildings.Where(b => b.IsWallRelated()).ToList();
	
			// ⭐ 하이브리드 접근법: Union-Find + BFS
			var optimizer = new UnionFindOptimizer();
			var connectedComponents = optimizer.GroupBuildingsByUnionFind(wallRelatedBuildings);
			
			Debug.Log($"[CheckWallGroupForRoom] Union-Find 결과: {connectedComponents.Count}개 컴포넌트");
			for (int i = 0; i < connectedComponents.Count; i++)
			{
				var component = connectedComponents[i];
				Debug.Log($"  컴포넌트 {i}: {component.Count}개 - [{string.Join(", ", component.Select(b => $"{b.name}({b.buildingType})"))}]");
			}
			
			// 트리거가 포함된 컴포넌트 찾기
			var triggerComponent = connectedComponents.FirstOrDefault(component => component.Contains(triggerWall));
			if (triggerComponent == null)
			{
				Debug.LogWarning($"[CheckWallGroupForRoom] 트리거 {triggerWall.name}이 포함된 컴포넌트를 찾을 수 없음");
				return;
			}

			Debug.Log($"[CheckWallGroupForRoom] 트리거 컴포넌트 발견: {triggerComponent.Count}개 건물 - [{string.Join(", ", triggerComponent.Select(b => $"{b.name}({b.buildingType})"))}]");
			
			// ⭐ 컴포넌트 크기 체크 - 최소 3개의 벽이 필요 (삼각형도 가능한 방)
			if (triggerComponent.Count < 3)
			{
				Debug.Log($"[CheckWallGroupForRoom] ❌ 컴포넌트가 3개 미만이므로 방 생성 불가 ({triggerComponent.Count}개)");
				return;
			}
			
			Debug.Log($"[CheckWallGroupForRoom] BFS로 사이클 찾기 시작 - 컴포넌트 크기: {triggerComponent.Count}개");
			
			// ⭐ 2. 해당 컴포넌트에서만 BFS로 사이클 찾기 (성능 향상!)
			var potentialRooms = FindRoomsInComponent(triggerComponent, triggerWall);

			Debug.Log($"[CheckWallGroupForRoom] {potentialRooms.Count}개의 잠재적인 방을 찾았습니다.");

			// ⭐ 3. 찾은 방 후보들을 하나씩 생성 시도
			foreach (var roomBuildings in potentialRooms)
			{
				Debug.Log($"[CheckWallGroupForRoom] 방 구성 요소: {roomBuildings.Count}개 벽 관련 건물");
				
				// 예비 검사를 위한 스냅 그룹을 다시 계산
				var formationResult = PreCheckRoomWalls(roomBuildings);
				if (!formationResult.CanFormRoom) continue;
				Debug.Log($"[CheckWallGroupForRoom] 방 생성 시도!");
				_ = TryCreateRoomInChunkAsync(roomBuildings, chunk, formationResult.SnapGroups);
			}
		}
		
		/// <summary>
		/// ⭐ Union-Find로 필터링된 연결 컴포넌트에서 사이클을 찾습니다 (성능 최적화됨)
		/// </summary>
		private List<List<NetworkBuilding>> FindRoomsInComponent(List<NetworkBuilding> componentBuildings, NetworkBuilding triggerBuilding)
		{
			Debug.Log($"[FindRoomsInComponent] 시작 - 컴포넌트 크기: {componentBuildings.Count}개, 트리거: {triggerBuilding.name}");
			return FindRoomsInGroup(componentBuildings, triggerBuilding);
		}

		/// <summary>
		/// 주어진 벽 관련 건물 그룹 내에서 새로 추가된 건물을 포함하는 모든 최소 사이클(방)을 찾습니다.
		/// </summary>
		private List<List<NetworkBuilding>> FindRoomsInGroup(List<NetworkBuilding> wallRelatedBuildings, NetworkBuilding triggerBuilding)
		{
			Debug.Log($"[FindRoomsInGroup] 시작 - 벽 관련 건물 {wallRelatedBuildings.Count}개, 트리거: {triggerBuilding.name}");
			
			var rooms = new List<List<NetworkBuilding>>();
			
			// ⭐ 모든 벽 관련 건물을 시작점으로 사용 가능
			var startBuildings = new List<NetworkBuilding>();
			
			if (wallRelatedBuildings.Contains(triggerBuilding))
			{
				startBuildings.Add(triggerBuilding);
				Debug.Log($"[FindRoomsInGroup] 트리거 {triggerBuilding.buildingType} 직접 사용");
			}
			else
			{
				// 트리거가 그룹에 없는 경우, 연결된 건물들을 찾아서 시작점으로 사용
				Debug.Log($"[FindRoomsInGroup] {triggerBuilding.name}이 그룹에 없음. 연결된 건물들을 찾습니다.");
				
				foreach (var building in wallRelatedBuildings)
				{
					if (IsWallsConnected(triggerBuilding, building))
					{
						startBuildings.Add(building);
						Debug.Log($"[FindRoomsInGroup] 연결된 건물 발견: {building.name} ({building.buildingType})");
					}
				}
			}
			
			if (startBuildings.Count == 0)
			{
				Debug.LogWarning($"[FindRoomsInGroup] 시작할 건물을 찾을 수 없음");
				return rooms;
			}
			
			// 각 시작 건물에 대해 방 찾기 수행
			foreach (var startBuilding in startBuildings)
			{
				Debug.Log($"[FindRoomsInGroup] 시작 건물: {startBuilding.name}({startBuilding.buildingType})에 대해 방 찾기 시작");
				var roomsForThisBuilding = FindRoomsForStartWall(wallRelatedBuildings, startBuilding);
				
				foreach (var room in roomsForThisBuilding)
				{
					if (!IsDuplicateRoom(rooms, room))
					{
						rooms.Add(room);
						Debug.Log($"[FindRoomsInGroup] 새로운 방 발견! 총 {room.Count}개 건물");
					}
				}
			}
			
			Debug.Log($"[FindRoomsInGroup] 최종 결과: {rooms.Count}개의 방을 찾음");
			return rooms;
		}

		// ⭐ 특정 시작 건물에 대한 방 찾기 (벽, 문, 창문 모두 가능)
		private List<List<NetworkBuilding>> FindRoomsForStartWall(List<NetworkBuilding> wallRelatedBuildings, NetworkBuilding startBuilding)
		{
			Debug.Log($"[FindRoomsForStartWall] 시작 - 시작건물: {startBuilding.name} ({startBuilding.buildingType})");
			
			var rooms = new List<List<NetworkBuilding>>();
			var graph = BuildGraph(wallRelatedBuildings);

			Debug.Log($"[FindRoomsForStartWall] 그래프 생성 완료. 총 건물 수: {graph.Count}");
			
			// 시작 건물의 이웃들을 기준으로 탐색 시작
			if (!graph.TryGetValue(startBuilding, out var neighbors))
			{
				Debug.LogError($"[FindRoomsForStartWall] 시작 건물 {startBuilding.name}이 그래프에 없습니다!");
				return rooms;
			}
			
			Debug.Log($"[FindRoomsForStartWall] 시작 건물 {startBuilding.name}의 이웃: {neighbors.Count}개");
			foreach (var neighbor in neighbors)
			{
				Debug.Log($"  - 이웃: {neighbor.name} ({neighbor.buildingType})");
			}

			for (int i = 0; i < neighbors.Count; i++)
			{
				for (int j = i + 1; j < neighbors.Count; j++)
				{
					var neighborA = neighbors[i];
					var neighborB = neighbors[j];

					Debug.Log($"[FindRoomsForStartWall] 경로 탐색: {neighborA.name} → {neighborB.name} (시작건물 제외)");
					
					// BFS를 사용해 startBuilding을 제외하고 neighborA에서 neighborB로 가는 최단 경로를 찾음
					var path = FindShortestPath(graph, neighborA, neighborB, startBuilding);

					// 경로를 찾았다면, startBuilding과 합쳐서 하나의 방(사이클)을 완성
					if (path.Count > 0)
					{
						var newRoom = new List<NetworkBuilding> { startBuilding };
						newRoom.AddRange(path);
						
						Debug.Log($"[FindRoomsForStartWall] 사이클 발견! 총 {newRoom.Count}개 건물");
						rooms.Add(newRoom);
					}
					else
					{
						Debug.Log($"[FindRoomsForStartWall] 경로를 찾지 못함");
					}
				}
			}
			
			return rooms;
		}

		/// <summary>
		/// BFS를 이용해 두 벽 사이의 최단 경로를 찾습니다.
		/// </summary>
		private static List<NetworkBuilding> FindShortestPath(Dictionary<NetworkBuilding, List<NetworkBuilding>> graph, 
												   NetworkBuilding start, NetworkBuilding end, NetworkBuilding exclude)
		{
			Debug.Log($"[FindShortestPath] 경로 탐색: {start.name} → {end.name}, 제외: {exclude.name}");
			
			var queue = new Queue<List<NetworkBuilding>>();
			queue.Enqueue(new List<NetworkBuilding> { start });
			var visited = new HashSet<NetworkBuilding> { start, exclude }; // 시작점과 제외할 벽을 방문 처리

			int iterations = 0;
			
			while (queue.Count > 0)
			{
				iterations++;
				var path = queue.Dequeue();
				var lastWall = path.Last();

				if (lastWall == end)
				{
					Debug.Log($"[FindShortestPath] 경로 발견! 길이: {path.Count}, 반복: {iterations}");
					return path; // 목표 도달
				}

				if (!graph.TryGetValue(lastWall, out var neighbors))
				{
					Debug.LogWarning($"[FindShortestPath] {lastWall.name}의 이웃을 찾을 수 없음");
					continue;
				}

				foreach (var neighbor in neighbors.Where(neighbor => !visited.Contains(neighbor)))
				{
					visited.Add(neighbor);
					var newPath = new List<NetworkBuilding>(path) { neighbor };
					queue.Enqueue(newPath);
				}
			}
			
			Debug.Log($"[FindShortestPath] 경로를 찾지 못함. 반복: {iterations}");
			return new List<NetworkBuilding>(); // 경로 없음
		}

		// ⭐ 기타 필요한 헬퍼 메소드들 - 모든 벽 관련 건물 대상
		private Dictionary<NetworkBuilding, List<NetworkBuilding>> BuildGraph(List<NetworkBuilding> wallRelatedBuildings)
		{
			var graph = new Dictionary<NetworkBuilding, List<NetworkBuilding>>();
			
			Debug.Log($"[BuildGraph] 그래프 생성 시작. 벽 관련 건물 수: {wallRelatedBuildings.Count}");
			
			foreach (var building in wallRelatedBuildings)
			{
				graph[building] = new List<NetworkBuilding>();
				foreach (var other in wallRelatedBuildings)
				{
					if (building != other && IsWallsConnected(building, other))
					{
						graph[building].Add(other);
						Debug.Log($"[BuildGraph] 연결 추가: {building.name}({building.buildingType}) <-> {other.name}({other.buildingType})");
					}
				}
				Debug.Log($"[BuildGraph] {building.name}의 연결 수: {graph[building].Count}");
			}
			
			Debug.Log($"[BuildGraph] 그래프 생성 완료");
			return graph;
		}

		private bool IsDuplicateRoom(List<List<NetworkBuilding>> existingRooms, List<NetworkBuilding> newRoom)
		{
			var newRoomSet = new HashSet<NetworkBuilding>(newRoom);
			foreach (var room in existingRooms)
			{
				var roomSet = new HashSet<NetworkBuilding>(room);
				if (roomSet.SetEquals(newRoomSet))
				{
					return true;
				}
			}
			return false;
		}

		// CheckWallsCanFormRoom의 일부 기능을 가져와 예비 검사 함수로 만듭니다.
		private RoomFormationResult PreCheckRoomWalls(List<NetworkBuilding> wallRelatedBuildings)
		{
			Debug.Log($"[PreCheckRoomWalls] === 시작 - {wallRelatedBuildings.Count}개 건물 ===");
			
			// ⭐ 최소 건물 개수를 3개로 설정 (삼각형 방도 가능)
			if (_buildingSystem == null || wallRelatedBuildings.Count < 4)
			{
				Debug.Log($"[PreCheckRoomWalls] 건물 개수 부족: {wallRelatedBuildings.Count}개 < 4개");
				return new RoomFormationResult { CanFormRoom = false };
			}
			
			// ⭐ Union-Find로 사이클 검증 (더 효율적!)
			if (!HasCycleWithUnionFind(wallRelatedBuildings))
			{
				Debug.Log($"[PreCheckRoomWalls] 유효한 사이클이 없음 - 폐쇄 공간 아님");
				return new RoomFormationResult { CanFormRoom = false };
			}
			
			var snapGroups = new Dictionary<NetworkBuilding, SortedSet<SnapInfo>>();
			
			// ⭐ 모든 벽 관련 건물로 스냅 그룹 계산 (문/창문도 스냅 포인트 있음)
			foreach (var building in wallRelatedBuildings)
			{
				Debug.Log($"[PreCheckRoomWalls] {building.name} 스냅 후보 계산 중...");
				
				var provider = building.GetComponent<ISnapProvider>();
				if (provider == null) 
				{
					Debug.LogWarning($"[PreCheckRoomWalls] {building.name}에 ISnapProvider 없음");
					continue;
				}
				
				var candidates = _buildingSystem.FindAllSnapCandidates(provider, building.transform.position, building.transform.rotation, building.transform.position, 10f, LayerMask.GetMask("Building"));
				
				var filteredCandidates = candidates.Where(snap => 
				{
					var snapBuilding = snap.TheirTransform.GetComponent<NetworkBuilding>();
					var isInGroup = snapBuilding != null && wallRelatedBuildings.Contains(snapBuilding);
					return isInGroup;
				}).ToList();
				
				snapGroups[building] = new SortedSet<SnapInfo>(filteredCandidates);
			}
			return new RoomFormationResult { CanFormRoom = true, SnapGroups = snapGroups };
		}
		
		/// <summary>  
		/// ⭐ Union-Find로 사이클 감지 (단일 단계로 완전 검증)
		/// </summary>
		private bool HasCycleWithUnionFind(List<NetworkBuilding> wallRelatedBuildings)
		{
			Debug.Log($"[HasCycleWithUnionFind] 폐쇄회로 검증 시작 - {wallRelatedBuildings.Count}개 건물");
			
			if (wallRelatedBuildings.Count < 3)
			{
				Debug.Log($"[HasCycleWithUnionFind] 건물 수 부족 ({wallRelatedBuildings.Count} < 3)");
				return false;
			}
			
			// 건물 리스트 출력
			Debug.Log($"[HasCycleWithUnionFind] 검사할 건물들:");
			foreach (var building in wallRelatedBuildings)
			{
				Debug.Log($"  - {building.name} (ID: {building.Object.Id})");
			}
			
			// Union-Find 초기화
			var parent = new Dictionary<NetworkBuilding, NetworkBuilding>();
			foreach (var building in wallRelatedBuildings)
			{
				parent[building] = building;
			}
			
			// 실제 연결 관계 수집 (중복 방지)
			var edges = new HashSet<(NetworkBuilding, NetworkBuilding)>();
			foreach (var building1 in wallRelatedBuildings)
			{
				foreach (var building2 in wallRelatedBuildings)
				{
					if (building1 == building2 || !IsActuallyConnected(building1, building2)) continue;
					var str1 = building1.Object.Id.ToString();
					var str2 = building2.Object.Id.ToString();
						
					edges.Add(string.Compare(str1, str2, StringComparison.Ordinal) < 0
						? (building1, building2)
						: (building2, building1));
				}
			}
			
			Debug.Log($"[HasCycleWithUnionFind] 발견된 연결 관계: {edges.Count}개");
			foreach (var (b1, b2) in edges)
			{
				Debug.Log($"  - {b1.name} ↔ {b2.name}");
			}
			
			// 간선을 하나씩 추가하면서 사이클 체크
			foreach (var (building1, building2) in edges)
			{
				var root1 = FindRoot(parent, building1);
				var root2 = FindRoot(parent, building2);
				
				if (root1 == root2)
				{
					Debug.Log($"[HasCycleWithUnionFind] ✅ 사이클 발견: {building1.name} ↔ {building2.name}");
					Debug.Log($"  - 이미 같은 컴포넌트에 있음 (루트: {root1.name})");
					return true;
				}
				
				// Union 연산
				parent[root2] = root1;
				Debug.Log($"[HasCycleWithUnionFind] Union: {building1.name} ↔ {building2.name} (루트: {root1.name})");
			}
			
			Debug.Log($"[HasCycleWithUnionFind] ❌ 사이클 없음");
			return false;
		}
		
		/// <summary>
		/// Union-Find의 Find 연산 (경로 압축 적용)
		/// </summary>
		private NetworkBuilding FindRoot(Dictionary<NetworkBuilding, NetworkBuilding> parent, NetworkBuilding building)
		{
			if (parent[building] != building)
			{
				parent[building] = FindRoot(parent, parent[building]); // 경로 압축
			}
			return parent[building];
		}
		
		
		/// <summary>
		/// ConnectedBuildings를 통해 실제 연결 상태 확인
		/// </summary>
		private bool IsActuallyConnected(NetworkBuilding building1, NetworkBuilding building2)
		{
			if (building1 == null || building2 == null)
			{
				Debug.Log(
					$"[IsActuallyConnected] null 건물: {building1?.name ?? "null"} <-> {building2?.name ?? "null"}");
				return false;
			}

			// 양방향 연결 확인
			var connected1To2 = building1.ConnectedBuildings.Count > 0 && 
			                    building1.ConnectedBuildings.Contains(building2.Object.Id);
			var connected2To1 = building2.ConnectedBuildings.Count > 0 && 
			                    building2.ConnectedBuildings.Contains(building1.Object.Id);
			
			Debug.Log($"[IsActuallyConnected] 최종 결과: {building1.name} -> {building2.name}: {connected1To2}, {building2.name} -> {building1.name}: {connected2To1}");
			
			if (connected1To2 || connected2To1) return true;
			
			var physicalConnection = IsPhysicallyConnected(building1, building2);
			Debug.Log($"[IsActuallyConnected] 물리적 연결 확인: {physicalConnection}");
			return physicalConnection;

		}
		
		/// <summary>
		/// 물리적 거리와 스냅포인트를 기반으로 연결 확인 (ConnectedBuildings 백업)
		/// </summary>
		private bool IsPhysicallyConnected(NetworkBuilding building1, NetworkBuilding building2)
		{
			if (building1 == null || building2 == null) return false;
			
			// 1. 거리 기반 1차 필터링
			var distance = Vector3.Distance(building1.transform.position, building2.transform.position);
			const float maxSnapDistance = 5.0f; // 스냅 가능한 최대 거리
			
			if (distance > maxSnapDistance)
			{
				Debug.Log($"[IsPhysicallyConnected] 거리 초과: {distance:F2}m > {maxSnapDistance}m");
				return false;
			}
			
			// 2. BuildingSystem을 통한 스냅 가능성 확인
			if (_buildingSystem?.Settings == null) return false;
			
			var provider1 = building1.GetComponent<ISnapProvider>();
			var provider2 = building2.GetComponent<ISnapProvider>();
			
			if (provider1?.SnapList == null || provider2?.SnapList == null) return false;
			
			// 3. 실제 스냅포인트들 간의 거리 확인
			foreach (var snap1 in provider1.SnapList.snapPoints)
			{
				var worldPos1 = building1.transform.TransformPoint(snap1.localPosition);
				
				foreach (var snap2 in provider2.SnapList.snapPoints)
				{
					// 스냅 타입 호환성 확인
					if (!_buildingSystem.Settings.AreTypesCompatible(snap1.type, snap2.type)) 
						continue;
					
					var worldPos2 = building2.transform.TransformPoint(snap2.localPosition);
					var snapDistance = Vector3.Distance(worldPos1, worldPos2);
					
					// 스냅 반경 내에 있으면 연결된 것으로 판단
					var maxSnapRadius = Mathf.Max(snap1.snapRadius, snap2.snapRadius);
					if (snapDistance <= maxSnapRadius * 1.5f) // 여유를 둠
					{
						Debug.Log($"[IsPhysicallyConnected] ✅ 물리적 연결 확인: 거리 {snapDistance:F2}m, 허용 {maxSnapRadius:F2}m");
						return true;
					}
				}
			}
			
			Debug.Log($"[IsPhysicallyConnected] ❌ 물리적 연결 없음");
			return false;
		}
		
		/// <summary>
		/// 방 크기가 유효한지 검증
		/// </summary>
		private bool IsValidRoomSize(WallRoom room)
		{
			if (room?.roomBounds == null) return false;
			
			var size = room.roomBounds.size;
			var volume = size.x * size.y * size.z;
			
			// 최소/최대 크기 제한
			const float minVolume = 4f;    // 2x2x1 최소 크기
			const float maxVolume = 1000f; // 10x10x10 최대 크기
			const float minSideLength = 1f; // 각 변의 최소 길이
			
			if (volume < minVolume)
			{
				Debug.Log($"[IsValidRoomSize] 방이 너무 작음: {volume:F1} < {minVolume}");
				return false;
			}
			
			if (volume > maxVolume)
			{
				Debug.Log($"[IsValidRoomSize] 방이 너무 큼: {volume:F1} > {maxVolume}");
				return false;
			}
			
			if (size.x < minSideLength || size.z < minSideLength)
			{
				Debug.Log($"[IsValidRoomSize] 방의 가로/세로가 너무 작음: {size.x:F1}x{size.z:F1}");
				return false;
			}
			
			Debug.Log($"[IsValidRoomSize] ✅ 유효한 방 크기: {size.x:F1}x{size.y:F1}x{size.z:F1} (부피: {volume:F1})");
			return true;
		}

		// 추가: 영향받은 룸들 업데이트
		private void UpdateAffectedRooms(NetworkBuilding affectedWall, ChunkData chunk)
		{
			var roomsToUpdate = chunk.RoomIds
				.Where(roomId =>
				{
					var room = _roomCache.GetRoom(roomId);
					return room != null && room.walls.Contains(affectedWall);
				})
				.ToList();

			foreach (var roomId in roomsToUpdate)
			{
				UpdateRoomInChunk(roomId, chunk);
			}
		}

		// 청크에서 건물 제거
		private void RemoveBuildingFromChunk(NetworkBuilding building, ChunkData chunk)
		{
			chunk.Buildings.Remove(building);
			var affectedRooms = chunk.RoomIds.Where(roomId =>
			{
				var room = _roomCache.GetRoom(roomId);
				return room != null && room.walls.Any(w => w != null && w.Object.Id == building.Object.Id);
			}).ToList();

			foreach (var roomId in affectedRooms) UpdateRoomInChunk(roomId, chunk);
		}

		// TryCreateRoomInChunkAsync는 기존과 동일 - 여기서 폐쇄 공간 확인!
		private async UniTask TryCreateRoomInChunkAsync(
			List<NetworkBuilding> walls,
			ChunkData chunk,
			Dictionary<NetworkBuilding, SortedSet<SnapInfo>> snapGroups) // ⭐ 파라미터 추가
		{
			Debug.Log($"[TryCreateRoomInChunkAsync] 룸 생성 시작 - 벽 {walls.Count}개");

			if (chunk.RoomIds.Count >= maxRoomsPerChunk)
			{
				Debug.LogWarning($"[TryCreateRoomInChunkAsync] 청크 {chunk}의 룸 한계 도달");
				return;
			}

			// ⭐ 중복 방 생성 방지 강화: 겹치는 벽이 있는 방 검사
			var wallIdSet = new HashSet<NetworkId>(walls.Select(w => w.Object.Id));
			
			// 1. 동일한 건물 세트 체크
			foreach (var existingRoomId in chunk.RoomIds)
			{
				var existingRoom = _roomCache.GetRoom(existingRoomId);
				if (existingRoom == null) continue;
				
				var existingWallIdSet = new HashSet<NetworkId>(existingRoom.walls.Where(w => w != null).Select(w => w.Object.Id));
				
				if (wallIdSet.SetEquals(existingWallIdSet))
				{
					Debug.LogWarning($"[TryCreateRoomInChunkAsync] 동일한 건물 세트로 이미 방 {existingRoomId}가 존재함. 생성 중단.");
					return;
				}
				
				// 2. 벽의 50% 이상이 겹치면 중복으로 간주
				var overlappingWalls = wallIdSet.Intersect(existingWallIdSet).Count();
				var minWalls = Math.Min(wallIdSet.Count, existingWallIdSet.Count);
				if (overlappingWalls >= minWalls * 0.5f)
				{
					Debug.LogWarning($"[TryCreateRoomInChunkAsync] 기존 방 {existingRoomId}와 {overlappingWalls}/{minWalls}개 벽이 겹침. 생성 중단.");
					return;
				}
			}
			
			// 3. 전체 캐시에서도 중복 체크 (크로스 청크 방지)
			foreach (var globalRoomId in _roomCache.AllRoomIds)
			{
				var globalRoom = _roomCache.GetRoom(globalRoomId);
				if (globalRoom == null) continue;
				
				var globalWallIdSet = new HashSet<NetworkId>(globalRoom.walls.Where(w => w != null).Select(w => w.Object.Id));
				if (wallIdSet.SetEquals(globalWallIdSet))
				{
					Debug.LogWarning($"[TryCreateRoomInChunkAsync] 전역 방 {globalRoomId}와 동일한 벽 세트. 생성 중단.");
					return;
				}
			}

			var room = new WallRoom
			{
				roomId = GetNextRoomId(),
				walls = new List<NetworkBuilding>(walls),
				windows = FindPartsInWalls(walls, BuildingPartType.Window),
				doors = FindPartsInWalls(walls, BuildingPartType.Door),
				SnapGroups = snapGroups,
			};

			Debug.Log($"[TryCreateRoomInChunkAsync] 룸 ID {room.roomId} 생성 중...");

			// ⭐ 여기서 폐쇄 공간 확인!
			room.UpdateRoom(); // isEnclosed 계산 포함

			// ⭐ 폐쇄되지 않은 공간이면 리턴
			if (!room.isEnclosed)
			{
				Debug.Log($"[TryCreateRoomInChunkAsync] 룸 {room.roomId}는 폐쇄되지 않은 공간");
				return;
			}
			
			// ⭐ 방 크기 검증 (너무 작거나 큰 방 방지)
			if (!IsValidRoomSize(room))
			{
				Debug.Log($"[TryCreateRoomInChunkAsync] 룸 {room.roomId}는 유효하지 않은 크기");
				return;
			}

			// 룸 캐시에 추가
			_roomCache.AddRoom(room);
			chunk.RoomIds.Add(room.roomId);

			GameObject roomColliderObj = null;
			if (chunk.IsActive)
			{
				roomColliderObj = CreateOptimalRoomCollider(room);
				Debug.Log("[TryCreateRoomInChunkAsync] 콜라이더 생성됨");
			}

			if (HasStateAuthority && ActiveRoomIds.Count < ActiveRoomIds.Capacity)
			{
				ActiveRoomIds.Add(room.roomId);
				Debug.Log("[TryCreateRoomInChunkAsync] 네트워크 리스트에 추가됨");
			}

			// ⭐ 룸이 성공적으로 생성된 시점에 OnRoomCreated 이벤트 발생
			if (roomColliderObj != null)
			{
				Debug.Log($"[TryCreateRoomInChunkAsync] OnRoomCreated 이벤트 발생 - 룸 {room.roomId}");
				OnRoomCreated?.Invoke(roomColliderObj);
			}

			Debug.Log($"[TryCreateRoomInChunkAsync] 룸 {room.roomId} 생성 완료 - 밀폐: {room.isEnclosed}");
		}
		
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
				// ⭐ 3. BuildingSystem을 통한 실제 스냅 연결 가능 여부 확인
				if (_buildingSystem != null)
				{
					var canSnap = AreBuildingsActuallySnapConnected(wall1, wall2);
					if (canSnap)
					{
						Debug.LogWarning($"[IsWallsConnected] 거리 기반 연결 감지: {wall1.name} <-> {wall2.name} (거리: {distance:F2}m)");
						return true;
					}
				}
			}

			return false;
		}
		
		/// <summary>
		/// ⭐ 두 건물이 실제로 스냅 연결되어 있는지 BuildingSystem을 통해 확인
		/// </summary>
		private static bool AreBuildingsActuallySnapConnected(NetworkBuilding building1, NetworkBuilding building2)
		{
			if (_buildingSystem?.Settings == null) return false;
			
			var provider1 = building1.GetComponent<ISnapProvider>();
			var provider2 = building2.GetComponent<ISnapProvider>();
			
			if (provider1?.SnapList == null || provider2?.SnapList == null) return false;

			// 각 건물의 스냅 포인트들 간의 연결 가능성 체크
			foreach (var snap1 in provider1.SnapList.snapPoints)
			{
				var worldPos1 = building1.transform.TransformPoint(snap1.localPosition);
				
				foreach (var snap2 in provider2.SnapList.snapPoints)
				{
					// 스냅 타입 호환성 확인
					if (!_buildingSystem.Settings.AreTypesCompatible(snap1.type, snap2.type)) 
						continue;

					var worldPos2 = building2.transform.TransformPoint(snap2.localPosition);
					var snapDistance = Vector3.Distance(worldPos1, worldPos2);
					
					// 스냅 반경 내에 있으면 연결된 것으로 판단
					var maxSnapRadius = Mathf.Max(snap1.snapRadius, snap2.snapRadius);
					if (snapDistance <= maxSnapRadius * 1.2f) // 약간의 여유 허용
					{
						Debug.Log($"[AreBuildingsActuallySnapConnected] 스냅 연결 확인: {building1.name} <-> {building2.name} (거리: {snapDistance:F2}m, 허용: {maxSnapRadius:F2}m)");
						return true;
					}
				}
			}

			return false;
		}

		private static RoomFormationResult CheckWallsCanFormRoom(List<NetworkBuilding> walls)
		{
			// 최소 벽 개수 확인
			if (_buildingSystem == null || walls.Count < 4)
				return new RoomFormationResult { CanFormRoom = false };


			var snapGroups = new Dictionary<NetworkBuilding, SortedSet<SnapInfo>>();

			// 각 벽의 모든 스냅 후보 수집 (기존과 동일)
			foreach (var wall in walls)
			{
				var provider = wall.GetComponent<ISnapProvider>();
				if (provider == null) continue;

				var candidates = _buildingSystem.FindAllSnapCandidates(
					provider, wall.transform.position, wall.transform.rotation,
					wall.transform.position, 10f, LayerMask.GetMask("Building"));

				var filteredCandidates = new SortedSet<SnapInfo>(
					candidates.Where(snap =>
					{
						var otherWall = snap.TheirTransform.GetComponent<NetworkBuilding>();
						return otherWall != null && walls.Contains(otherWall);
					})
				);
				snapGroups[wall] = filteredCandidates;
			}
			
			// 각 벽이 최소 2개의 이웃을 갖는지 '예비 검사'만 수행
			return new RoomFormationResult { CanFormRoom = true, SnapGroups = snapGroups };

		}

		private void OnDrawGizmos()
		{
			if (_roomCache == null || _roomCache.Count == 0) return;

			Gizmos.color = Color.cyan;
			foreach (var roomId in _roomCache.AllRoomIds)
			{
				var room = _roomCache.GetRoom(roomId);
				if (room != null)
				{
					// 생성된 룸의 경계를 와이어 큐브로 그립니다.
					Gizmos.DrawWireCube(room.roomBounds.center, room.roomBounds.size);
				}
			}
		}

		/// <summary>
		///     회전된 경계 상자에 맞춰 콜라이더를 생성하고 GameObject를 반환합니다.
		/// </summary>
		private GameObject CreateOptimalRoomCollider(WallRoom room)
		{
			var colliderObj = new GameObject($"Room_{room.roomId}_Collider");
			colliderObj.transform.SetParent(_roomCollidersParent.transform);

			// WallRoom에 저장된 회전 및 위치 정보 사용

			colliderObj.transform.position = room.roomCenter;
			colliderObj.transform.rotation = Quaternion.identity;

			var boxCollider = colliderObj.AddComponent<BoxCollider>();
			boxCollider.isTrigger = true;
			boxCollider.size = room.roomSize;
			boxCollider.center = Vector3.zero; // 부모 오브젝트가 이미 중심에 있으므로

			room.roomCollider = boxCollider;

			var roomInfo = colliderObj.AddComponent<RoomInfo>();
			roomInfo.roomId = room.roomId;
			roomInfo.indoorSystem = this;
			
			var effectArea = colliderObj.AddComponent<EffectArea>();
			effectArea.ModifiersToApply = new();
			foreach (var effect in roomEffects)
			{
				effectArea.ModifiersToApply.Add(effect);
			}
			
			var nearbyFog = FindClosestVolumetricFog(room.roomBounds.center, fogSearchRadius);
			if (nearbyFog != null)
			{
				roomInfo.SetNearbyVolumetricFog(nearbyFog, fogOfWarSize);
				Debug.Log($"[CreateRoomCollider2] 룸 {room.roomId}에 VolumetricFog {nearbyFog.name} 연결됨");
			}
			else
			{
				Debug.LogWarning($"[CreateRoomCollider] 룸 {room.roomId} 근처에 VolumetricFog를 찾을 수 없음");
			}

			RegisterRoomInfo(room.roomId, roomInfo);
			
			return colliderObj;
		}

		// 주기적 청크 시스템 업데이트
		private void UpdateChunkSystem()
		{
			if (!HasStateAuthority) return;

			// 모든 플레이어의 위치 기반으로 청크 활성화
			var allActiveChunks = new HashSet<Vector2Int>();

			foreach (var player in Runner.ActivePlayers)
				if (Runner.TryGetPlayerObject(player, out var playerObj))
				{
					var playerPos = playerObj.transform.position;
					var playerChunk = GetChunkCoordinate(playerPos);

					// 플레이어 주변 청크들을 활성 목록에 추가
					for (var x = -activeChunkRadius; x <= activeChunkRadius; x++)
					for (var z = -activeChunkRadius; z <= activeChunkRadius; z++)
					{
						var chunkCoord = new Vector2Int(playerChunk.x + x, playerChunk.y + z);
						allActiveChunks.Add(chunkCoord);
					}
				}

			// 청크 활성화/비활성화
			foreach (var (coord, chunk) in _chunkMap)
			{
				var shouldBeActive = allActiveChunks.Contains(coord);

				switch (shouldBeActive)
				{
					case true when !chunk.IsActive:
						ActivateChunk(chunk);
						break;
					case false when chunk.IsActive:
						DeactivateChunk(chunk);
						break;
				}
			}

			// 네트워크 동기화 업데이트
			UpdateNetworkSyncList(allActiveChunks);
		}

		// 청크 활성화
		private void ActivateChunk(ChunkData chunk)
		{
			chunk.IsActive = true;
			chunk.LastActiveTime = Time.time;

			// 이 청크의 룸 콜라이더 생성
			foreach (var room in chunk.RoomIds.Select(roomId => _roomCache.GetRoom(roomId))
				         .Where(room => room != null && room.roomCollider == null))
			{
				CreateRoomCollider(room);
			}

		}

		// 청크 비활성화
		private void DeactivateChunk(ChunkData chunk)
		{
			chunk.IsActive = false;

			// 이 청크의 룸 콜라이더 제거
			foreach (var roomId in chunk.RoomIds)
			{
				var room = _roomCache.GetRoom(roomId);
				if (room != null && room.roomCollider != null) DestroyRoomCollider(room);
			}

			Debug.Log($"[ChunkedSystem] 청크 {chunk.Coordinate} 비활성화");
		}

		// 네트워크 동기화 리스트 업데이트
		private void UpdateNetworkSyncList(HashSet<Vector2Int> activeChunks)
		{
			var newActiveRoomIds = new HashSet<int>();

			// 활성 청크의 모든 룸 ID 수집
			foreach (var coord in activeChunks)
				if (_chunkMap.TryGetValue(coord, out var chunk))
					foreach (var roomId in chunk.RoomIds)
						newActiveRoomIds.Add(roomId);

			// 네트워크 리스트 업데이트 (변경사항만)
			var toAdd = newActiveRoomIds.Except(ActiveRoomIds).ToList();
			var toRemove = ActiveRoomIds.Where(id => !newActiveRoomIds.Contains(id)).ToList();

			foreach (var roomId in toRemove.Where(roomId => ActiveRoomIds.Contains(roomId)))
				ActiveRoomIds.Remove(roomId);

			foreach (var roomId in toAdd)
				if (ActiveRoomIds.Count < ActiveRoomIds.Capacity)
					ActiveRoomIds.Add(roomId);
				else
					Debug.LogWarning("[ChunkedSystem] ActiveRoomIds 용량 초과!");
		}


		// 룸 업데이트 메서드 (청크별로 처리)
		private void UpdateRoomInChunk(int roomId, ChunkData chunk)
		{
			var room = _roomCache.GetRoom(roomId);
			if (room == null) return;

			room.walls.RemoveAll(w => w == null);
			
			Debug.Log($"[UpdateRoomInChunk] 룸 {roomId} 재검사 시작 - {room.walls.Count}개 벽");

			if (room.walls.Count < 3)
			{
				Debug.Log($"[UpdateRoomInChunk] 룸 {roomId} 벽 부족으로 제거");
				// 룸 제거
				NotifyRoomInfoUpdate(roomId);
				_roomCache.RemoveRoom(roomId);
				chunk.RoomIds.Remove(roomId);

				DestroyRoomCollider(room);
			}
			else
			{
				// ⭐ 폐쇄회로 재검사 - 벽이 삭제되었을 때 여전히 사이클이 있는지 확인
				bool stillHasCycle = HasCycleWithUnionFind(room.walls);
				
				if (stillHasCycle)
				{
					Debug.Log($"[UpdateRoomInChunk] 룸 {roomId} 여전히 폐쇄회로 유지 - 룸 업데이트");
					// 룸 업데이트
					room.UpdateRoom(); // 모든 계산을 한 번에!
					UpdateRoomEnvironment(room);
				}
				else
				{
					Debug.Log($"[UpdateRoomInChunk] 룸 {roomId} 폐쇄회로 깨짐 - 룸 제거");
					// 폐쇄회로가 깨진 경우 룸 제거
					NotifyRoomInfoUpdate(roomId);
					_roomCache.RemoveRoom(roomId);
					chunk.RoomIds.Remove(roomId);

					DestroyRoomCollider(room);
				}
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority) return;
			foreach (var roomId in ActiveRoomIds.Where(roomId => !_roomCache.ContainsRoom(roomId)))
				RPC_RequestRoomInfo(roomId, Runner.LocalPlayer);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestRoomInfo(int roomId, PlayerRef requester)
		{
			var room = _roomCache.GetRoom(roomId);
			if (room == null) return;
			var wallIds = room.walls.Select(w => w.Object.Id).ToArray();
			RPC_ReceiveRoomInfo(requester, roomId, wallIds);
		}

		[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
		private void RPC_ReceiveRoomInfo(PlayerRef targetPlayer, int roomId, NetworkId[] wallIds)
		{
			if (Runner.LocalPlayer != targetPlayer) return;

			var walls = new List<NetworkBuilding>();
			foreach (var wallId in wallIds)
				if (Runner.TryFindObject(wallId, out var obj))
				{
					var building = obj.GetComponent<NetworkBuilding>();
					if (building != null) walls.Add(building);
				}

			if (walls.Count < 4) return;
			var room = new WallRoom
			{
				roomId = roomId,
				walls = walls,
				windows = FindPartsInWalls(walls, BuildingPartType.Window),
				doors = FindPartsInWalls(walls, BuildingPartType.Door)
			};

			room.UpdateRoom(); // 모든 계산을 한 번에!
			UpdateRoomEnvironment(room);
			CreateRoomCollider(room);

			_roomCache.AddRoom(room);
			Debug.Log($"클라이언트에서 룸 정보 받음: ID {roomId}");
		}

		private void CreateRoomCollider(WallRoom room)
		{
			var colliderObj = new GameObject($"Room_{room.roomId}_Collider");
			colliderObj.transform.SetParent(_roomCollidersParent.transform);
			colliderObj.transform.position = room.roomBounds.center;

			var boxCollider = colliderObj.AddComponent<BoxCollider>();
			boxCollider.isTrigger = true;
			boxCollider.center = Vector3.zero;
			boxCollider.size = room.roomBounds.size;

			room.roomCollider = boxCollider;

			var roomInfo = colliderObj.AddComponent<RoomInfo>();
			roomInfo.roomId = room.roomId;
			roomInfo.indoorSystem = this;

			var effectArea = colliderObj.AddComponent<EffectArea>();
			effectArea.ModifiersToApply = new();
			foreach (var effect in roomEffects)
			{
				effectArea.ModifiersToApply.Add(effect);
			}
			
			// VolumetricFog를 미리 찾아서 RoomInfo에 저장 (플레이어 입장 시 사용)
			var nearbyFog = FindClosestVolumetricFog(room.roomBounds.center, fogSearchRadius);
			if (nearbyFog != null)
			{
				roomInfo.SetNearbyVolumetricFog(nearbyFog, fogOfWarSize);
				Debug.Log($"[CreateRoomCollider] 룸 {room.roomId}에 VolumetricFog {nearbyFog.name} 연결됨");
			}
			else
			{
				Debug.LogWarning($"[CreateRoomCollider] 룸 {room.roomId} 근처에 VolumetricFog를 찾을 수 없음");
			}
			
			Debug.Log($"[CreateRoomCollider] 룸 {room.roomId} 콜라이더 생성 완료");
			
			RegisterRoomInfo(room.roomId, roomInfo);
		}

		
		/// <summary>
		/// 주변 VolumetricFog 컴포넌트들을 검색
		/// </summary>
		private static VolumetricFog FindClosestVolumetricFog(Vector3 center, float radius)
		{
			var results = new Collider[32];
			var size = Physics.OverlapSphereNonAlloc(center, radius, results);

			VolumetricFog closestFog = null;
			float minDistanceSqr = float.MaxValue;

			for (var i = 0; i < size; i++)
			{
				if (!results[i].TryGetComponent<VolumetricFog>(out var fog))
				{
					continue; // Skip if it doesn't have the fog component.
				}

				// Compare squared distances to find the closest one. This is faster than Vector3.Distance.
				float distanceSqr = (fog.transform.position - center).sqrMagnitude;

				if (distanceSqr < minDistanceSqr)
				{
					minDistanceSqr = distanceSqr;
					closestFog = fog;
				}
			}

			if (closestFog != null)
			{
				// Math.Sqrt is used here only for the debug log, not for the comparison logic.
				Debug.Log($"[FindClosestVolumetricFog] Closest fog found at a distance of {Mathf.Sqrt(minDistanceSqr):F2}m.");
			}

			return closestFog;
		}

		private void DestroyRoomCollider(WallRoom room)
		{
			if (room.roomCollider != null)
			{
				OnRoomDestroyed?.Invoke(room.roomId);

				UnregisterRoomInfo(room.roomId);
				Destroy(room.roomCollider.gameObject);
				room.roomCollider = null;
			}
		}

		private List<NetworkBuilding> FindPartsInWalls(List<NetworkBuilding> walls, BuildingPartType partType)
		{
			var items = new List<NetworkBuilding>();

			// 중복 검사를 피하기 위해 HashSet 사용
			var connectedBuildingIds = walls.SelectMany(wall => wall.ConnectedBuildings).ToHashSet();

			foreach (var connectedId in connectedBuildingIds)
			{
				if (!Runner.TryFindObject(connectedId, out var obj)) continue;

				var building = obj.GetComponent<NetworkBuilding>();

				if (building && building.IsRightPartType(partType)) items.Add(building);
			}

			return items;
		}

		private static void UpdateRoomEnvironment(WallRoom room)
		{
			room.lightLevel = 0.2f;
			room.lightLevel += room.windows.Count * 0.4f;
			room.lightLevel += room.doors.Count * 0.2f;
			room.lightLevel = Mathf.Clamp01(room.lightLevel);
			room.hasVentilation = room.windows.Count > 0 || room.doors.Count > 0;
		}

		private int GetNextRoomId()
		{
			var max = ActiveRoomIds.Prepend(0).Max();

			return max + 1;
		}

		// 강제 캐시 업데이트 (디버그용)
		[ContextMenu("Force Update Cache")]
		public void ForceUpdateCache()
		{
			_roomCache.ForceUpdatePositionCache();
		}

		// 캐시 통계 출력 (디버그용)
		[ContextMenu("Log Cache Stats")]
		public void LogCacheStats()
		{
			_roomCache.LogCacheStats();
		}

		// 청크 데이터
		private class ChunkData
		{
			public readonly List<NetworkBuilding> Buildings = new();
			public readonly Vector2Int Coordinate;
			public readonly HashSet<int> RoomIds = new();
			public bool IsActive;
			public float LastActiveTime;

			public ChunkData(Vector2Int coord)
			{
				Coordinate = coord;
				IsActive = false;
				LastActiveTime = 0f;
			}
		}

		// RoomFormationResult 구조체 추가
		private struct RoomFormationResult
		{
			public bool CanFormRoom;
			public Dictionary<NetworkBuilding, SortedSet<SnapInfo>> SnapGroups;
		}
	}
}