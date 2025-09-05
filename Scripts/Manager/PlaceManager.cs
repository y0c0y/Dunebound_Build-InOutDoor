using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Data.Building;
using Fusion;
using Placeable;
using SObject;
using Unity.Cinemachine;
using UnityEngine;

namespace Manager
{
	public class PlaceManager : NetworkBehaviour
	{
		[SerializeField] private GameObject currentLookedAtObject;
		private static BuildingSystemSettings _settings;

		[SerializeField] private Transform playerLook;
		private readonly Dictionary<PlayerRef, UniTaskCompletionSource<bool>> _pendingRequests = new();
		private CinemachineBrain _brain;
		private Camera _camera;

		private CraftingSystemManager _crafting;
		private InOutDoorSystem _inOutDoor;

		private NetworkBuilding _destroyTarget;

		private LayerMask _destructionMask;
		private UniTaskCompletionSource<bool> _hostActionTcs;
		private PlaceableData _item;


		private Vector2 _pointer;
		private PreviewBuilding _preview;

		private PlayerBuildMode CurrentMode => _crafting?.CurrentState ?? PlayerBuildMode.Idle;
		private Collider[] _nearbyColliders;

		public static PlaceManager Instance { get; private set; }

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (hasState) ClearEvent();
		}

		public override void Spawned()
		{
			if (!Object.HasInputAuthority) return;
			if (Instance != null && Instance != this)
			{
				Debug.LogWarning("[PlaceManager] 인스턴스가 중복 생성되었습니다.");
				Destroy(gameObject);
				return;
			}

			Instance = this;

			_destructionMask = LayerMask.GetMask("Building");

			_settings = BuildingSystem.Instance.Settings;
			_crafting = CraftingSystemManager.Instance;
			_inOutDoor = InOutDoorSystem.Instance;
			
			const int maxOverlaps = 32;
			_nearbyColliders = new Collider[maxOverlaps];

			GetCurrentCinemachineLiveCamera();

			SetEvent();
		}

		private void GetCurrentCinemachineLiveCamera()
		{
			var main = Camera.main;
			if (!main) return;

			var brain = main.GetComponent<CinemachineBrain>();
			if (!brain) return;

			_camera = brain.OutputCamera;
		}

		private void OnConfirmDestroyAction()
		{
			_ = OnDestructionItem(_destroyTarget);
		}


		private void OnConfirmBuildAction()
		{
			_ = OnBuildItem();
		}


		private void SetEvent()

		{
			ClearEvent();
			if (_crafting == null) return;

			_crafting.CraftItem += OnEnterBuildMode;

			_crafting.CancelCraftItem += OnCancelAction;

			_crafting.BuildItem += OnConfirmBuildAction;

			_crafting.FindDestructionItem += OnToggleDestroyMode;

			_crafting.DestructionItem += OnConfirmDestroyAction;
		}


		private void ClearEvent()

		{
			if (_crafting == null) return;

			_crafting.CraftItem -= OnEnterBuildMode;

			_crafting.CancelCraftItem -= OnCancelAction;

			_crafting.BuildItem -= OnConfirmBuildAction;

			_crafting.FindDestructionItem -= OnToggleDestroyMode;

			_crafting.DestructionItem -= OnConfirmDestroyAction;
		}


		private void OnEnterBuildMode(PlaceableData data, GameObject parent)
		{
			if (!Object.HasInputAuthority || !data) return;
			
			Debug.LogWarning("item이 왜 나오지 않냐.");
			ExitCurrentMode(); // 다른 모드였다면 먼저 정리
			_item = data;

			var go = Instantiate(_item.previewPrefab, parent.transform, true);

			_preview = go.GetComponent<PreviewBuilding>();
			_preview.Setup(_item);

			var initialPosition = transform.position + transform.forward * 5f;
			_preview.transform.position = initialPosition;

			_crafting.StateChange(PlayerBuildMode.Build); // CraftingSystem의 UI 상태 변경
			if (CameraManager.Instance != null) CameraManager.Instance.SwitchToBuildMode();
		}


		private void OnToggleDestroyMode(bool enable)
		{
			if (!Object.HasInputAuthority) return;


			ExitCurrentMode(); // 다른 모드였다면 먼저 정리

			if (!enable) return;
			_crafting.StateChange(PlayerBuildMode.Destruction);
			if (CameraManager.Instance != null)
				CameraManager.Instance.SwitchToBuildMode();
		}


		private void OnCancelAction()
		{
			if (!Object.HasInputAuthority) return;

			ExitCurrentMode();
		}


		private void ExitCurrentMode()
		{
			if (CurrentMode == PlayerBuildMode.Build && _preview != null)
			{
				Destroy(_preview.gameObject);

				_preview = null;

				_item = null;
			}
			else if (CurrentMode == PlayerBuildMode.Destruction && _destroyTarget != null)
			{
				_destroyTarget.SetHighlight(false);

				_destroyTarget = null;
			}

			_crafting.StateChange(PlayerBuildMode.Idle);
			if (CameraManager.Instance != null)
				CameraManager.Instance.SwitchToPlayerFollowMode();
		}


		public override void FixedUpdateNetwork()
		{
			if (!Object.HasInputAuthority) return;

			if (GetInput(out PlayerGameplayInput input))
				if (CurrentMode != PlayerBuildMode.Idle)
					UpdateTargeting(input);
		}

		private void UpdateTargeting(PlayerGameplayInput input)
		{
			var cameraRay = new Ray(_camera.transform.position, _camera.transform.forward);

			LayerMask targetMask = CurrentMode switch
			{
				PlayerBuildMode.Build => _item?.placementMask ?? 0,
				PlayerBuildMode.Destruction => _destructionMask,
				_ => 0
			};

			var hasHit = Physics.Raycast(cameraRay, out var hitInfo, 20f, targetMask);

			switch (CurrentMode)
			{
				case PlayerBuildMode.Build:
					HandleBuildMode(cameraRay, hitInfo, hasHit);
					break;
				case PlayerBuildMode.Destruction:
					HandleDestructionMode(hitInfo, hasHit);
					break;
			}
		}


		private void HandleBuildMode(Ray cameraRay, RaycastHit hitInfo, bool hasHit)
		{
			if (!_preview || !_preview.IsInitialized) return;

			const float minPlacementDistance = 1f;
			Vector3 finalPreviewPosition;

			if (hasHit)
			{
				var currentHitDistance = Vector3.Distance(cameraRay.origin, hitInfo.point);

				// 수직 표면인지 확인
				bool isVerticalSurface = Mathf.Abs(hitInfo.normal.y) < 0.3f;
				bool isHittingBuilding = hitInfo.collider.gameObject.layer == LayerMask.NameToLayer("Building");

				if (currentHitDistance < minPlacementDistance)
				{
					if (isVerticalSurface)
					{
						// 수직 표면의 경우: 표면에 고정
						finalPreviewPosition = hitInfo.point;

						// 오브젝트의 뒤쪽이 표면에 닿도록 조정
						Vector3 objectBack = new Vector3(Mathf.Abs(-cameraRay.direction.x),
							Mathf.Abs(-cameraRay.direction.y), Mathf.Abs(-cameraRay.direction.z));
						;
						float backOffset = Vector3.Dot(_preview.boundsExtents, objectBack);
						finalPreviewPosition += hitInfo.normal * backOffset;
					}
					else
					{
						// 수평 표면의 경우: 기존 로직 유지
						finalPreviewPosition = cameraRay.origin + cameraRay.direction * minPlacementDistance;
						if (!isHittingBuilding)
						{
							finalPreviewPosition.y = hitInfo.point.y + 0.001f;
						}
					}
				}
				else
				{
					// 충분한 거리가 있을 때
					finalPreviewPosition = hitInfo.point;

					// 표면 타입에 따른 오프셋 적용
					if (isVerticalSurface)
					{
						// 벽의 경우: 오브젝트 두께의 절반만큼 앞으로
						float halfThickness = GetObjectThickness(_preview) * 0.5f;
						finalPreviewPosition += hitInfo.normal * halfThickness;
					}
					else if (!isHittingBuilding && Mathf.Abs(hitInfo.normal.y - 1f) < 0.1f)
					{
						// 평평한 바닥
						finalPreviewPosition.y += 0.001f;
					}
				}
			}
			else
			{
				finalPreviewPosition = cameraRay.GetPoint(10f);
			}

			_preview.SetTargetPoint(finalPreviewPosition, cameraRay, _crafting.UserRotation);
			_preview.UpdatePreviewPosition();
		}

// 오브젝트의 두께 계산 (카메라 방향 기준)
		private float GetObjectThickness(PreviewBuilding preview)
		{
			// 카메라 방향에서 본 오브젝트의 두께
			Vector3 cameraForward = _camera.transform.forward;
			Vector3 absForward = new Vector3(
				Mathf.Abs(cameraForward.x),
				Mathf.Abs(cameraForward.y),
				Mathf.Abs(cameraForward.z)
			);

			// 각 축의 extent와 카메라 방향의 내적으로 두께 계산
			return Vector3.Dot(preview.boundsExtents * 2f, absForward);
		}

		private void HandleDestructionMode(RaycastHit hitInfo, bool hasHit)
		{
			NetworkBuilding newTarget = null;

			if (hasHit && hitInfo.collider.TryGetComponent<NetworkBuilding>(out var building)) newTarget = building;

			if (newTarget == _destroyTarget) return;

			if (_destroyTarget) _destroyTarget.SetHighlight(false);
			if (newTarget) newTarget.SetHighlight(true);
			_destroyTarget = newTarget;
		}

		private async UniTaskVoid OnBuildItem()
		{
			if (!Object.HasInputAuthority) return;

			try
			{
				if (!IsPreviewStateValid()) return;
				if (_preview == null || _item == null)
				{
					Debug.LogError("[PlaceManager] _preview 또는 _item이 null입니다.");
					return;
				}

				if (_preview.transform == null)
				{
					Debug.LogError("[PlaceManager] _preview.transform이 null입니다.");
					return;
				}

				var position = _preview.transform.position;
				var rotation = _preview.transform.rotation;
				var itemId = _item.id;
				
				var snapData = _preview.GetCurrentSnapData();

				Debug.Log($"[PlaceManager] 빌드 요청 준비 - 위치: {position}, ID: {itemId}");

				if (Runner == null)
				{
					Debug.LogError("[PlaceManager] Runner가 null입니다.");
					return;
				}

				if (Object == null)
				{
					Debug.LogError("[PlaceManager] NetworkObject가 null입니다.");
					return;
				}

				var success = await RequestHostActionAsync(() => 
					RpcHostPlaceRequest(position, rotation, itemId, snapData));
				
				if (success)
				{
					Debug.Log("[PlaceManager] 빌드 성공");

					if (Global.BuildingUsesRequirements)
					{
						var localInventory = Player.Local?.inventory;
						if (!localInventory || !localInventory.HasItems(_item.itemRequirements))
						{
							ExitCurrentMode();	
						}
					}
				}
				// TODO: 빌드 성공 후 연속 빌드를 위해 preview를 그대로 둘지, 아니면 모드를 빠져나갈지 결정
				// ExitCurrentMode(); // 만약 빌드 성공 후 모드를 빠져나가고 싶다면 이 줄의 주석을 해제하세요.
				else
					Debug.LogWarning("[PlaceManager] 빌드 실패 또는 타임아웃");
			}
			catch (OperationCanceledException)
			{
				Debug.Log("[PlaceManager] 빌드 요청 취소됨");
			}
			catch (Exception e)
			{
				Debug.LogError($"[PlaceManager] OnBuildItem 처리 중 예상치 못한 오류 발생: {e.Message}\n{e.StackTrace}");
			}
		}
		
		private bool IsPreviewStateValid()
		{
			if (!_preview)
			{
				Debug.LogWarning("[PlaceManager] Preview가 존재하지 않습니다.");
				return false;
			}

			if (_item == null)
			{
				Debug.LogError("[PlaceManager] 현재 선택된 아이템 데이터(_item)가 없습니다. 빌드를 진행할 수 없습니다.");
				return false;
			}


			var result = _preview.CheckBuildAbility();

			Debug.LogWarning($"[PlaceManager] {result.ToString()}");
			if (result == BuildCheckResult.Success) return true;
			Debug.LogWarning("[PlaceManager] 현재 위치에 배치가 불가능합니다.");
			return false;

		}

		private async UniTask<bool> RequestHostActionAsync(Action rpcCall)
		{
			if (!Object.HasInputAuthority) return false;

			if (_hostActionTcs is { Task: { Status: UniTaskStatus.Pending } }) _hostActionTcs.TrySetCanceled();

			_hostActionTcs = new UniTaskCompletionSource<bool>();

			rpcCall?.Invoke();

			var timeoutTask = UniTask.Delay(TimeSpan.FromSeconds(10));
			var (hasResult, result) = await UniTask.WhenAny(_hostActionTcs.Task, timeoutTask);

			if (hasResult) return result;
			Debug.LogWarning("[PlaceManager] Host로부터 응답이 없어 요청이 타임아웃되었습니다.");
			return false;
		}

		[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
		private void Rpc_HostActionResponse(PlayerRef requestingPlayer, bool success)
		{
			if (requestingPlayer == Runner.LocalPlayer) 
				_hostActionTcs?.TrySetResult(success);
		}


		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
		private void RpcHostPlaceRequest(Vector3 pos, Quaternion rot, string itemId, BuildingSnapData snapData, RpcInfo info = default)
		{
			if (!Object.HasStateAuthority) return;

			var requestingPlayer = info.Source;
			if (PlaceableDatabase.Instance == null)
			{
				Debug.LogError("[PlaceManager] PlaceableDatabase.Instance가 null입니다.");
				Rpc_HostActionResponse(requestingPlayer, false);
				return;
			}

			var itemData = PlaceableDatabase.GetById(itemId);
			if (!itemData)
			{
				Debug.LogError($"[PlaceManager] PlaceableData를 찾을 수 없음: {itemId}");
				Rpc_HostActionResponse(requestingPlayer, false);
				return;
			}

			if (Global.BuildingUsesRequirements)
			{
				Runner.TryGetPlayerObject(requestingPlayer, out var playerObject);
				playerObject.TryGetComponent<Player>(out var playerScript);
				if (!playerObject || !playerScript)
				{
					Debug.LogError($"[PlaceManager] Player Script를 찾을 수 없음: {requestingPlayer}");
					Rpc_HostActionResponse(requestingPlayer, false);
					return;
				}

				if (itemData.itemRequirements.Count > 0)
				{
					var inventory = playerScript.inventory;
					if (inventory == null)
					{
						Debug.LogError($"[PlaceManager] Inventory를 찾을 수 없음: {requestingPlayer}");
						Rpc_HostActionResponse(requestingPlayer, false);
						return;
					}

					if (!inventory.HasItems(itemData.itemRequirements))
					{
						Debug.LogError($"[PlaceManager] 인벤토리에 필요한 아이템이 없음: {itemId}");
						Rpc_HostActionResponse(requestingPlayer, false);
						return;
					}
				
					inventory.RemoveItems(itemData.itemRequirements);
				}
			}
			
			CancelPendingRequest(requestingPlayer);

			var tcs = new UniTaskCompletionSource<bool>();
			_pendingRequests.Add(requestingPlayer, tcs);

			var success = TrySpawnItem(itemData, pos, rot, requestingPlayer, snapData);
			Rpc_HostActionResponse(requestingPlayer, success);

			_pendingRequests.Remove(requestingPlayer);
		}

		private void CancelPendingRequest(PlayerRef player)
		{
			if (!_pendingRequests.TryGetValue(player, out var existingTcs)) return;

			switch (existingTcs.Task.Status)
			{
				case UniTaskStatus.Pending:
					Debug.LogWarning($"[PlaceManager] 기존 요청(Pending) → 취소 시도. PlayerId: {player.PlayerId}");
					if (!existingTcs.TrySetCanceled())
						Debug.LogError($"[PlaceManager] 요청 취소 실패. PlayerId: {player.PlayerId}");
					else
						Debug.Log($"[PlaceManager] 기존 요청 정상 취소 완료. PlayerId: {player.PlayerId}");
					break;
				case UniTaskStatus.Succeeded:
					Debug.Log($"[PlaceManager] 기존 요청 성공 완료. PlayerId: {player.PlayerId}");
					break;
				case UniTaskStatus.Canceled:
					Debug.Log($"[PlaceManager] 기존 요청 이미 취소. PlayerId: {player.PlayerId}");
					break;
				case UniTaskStatus.Faulted:
					Debug.LogError($"[PlaceManager] 기존 요청 오류 상태. PlayerId: {player.PlayerId}");
					break;
				default:
					Debug.LogError($"[PlaceManager]  PlayerId: {player.PlayerId}");
					break;
			}

			_pendingRequests.Remove(player);
		}

		// BuildingSystem을 선택적 기능으로 처리
		private bool TrySpawnItem(PlaceableData data, Vector3 pos, Quaternion rot, PlayerRef owner,
			BuildingSnapData snapData)
		{
			try
			{
				var newBuildingObject = Runner.Spawn(data.networkPrefab, pos, rot, owner,
					(runner, o) =>
					{
						var networkBuilding = o.GetComponent<NetworkBuilding>();
						if (networkBuilding == null) return;

						// ⭐ 콜백에서 필수 데이터를 모두 미리 주입합니다.
						networkBuilding.ItemID = data.id;
						networkBuilding.buildingType = data.buildingType; // buildingType 설정
					});
       
				if (snapData is { HasSnapData: true, TargetBuildingId: { IsValid: true } })
				{
					// ✅ 이제 newBuilding의 buildingType은 정확한 값을 가집니다.
					ProcessDirectConnection(newBuildingObject.GetComponent<NetworkBuilding>(), snapData);
				}

				if (QuestManager.Instance)
				{
					if (data.id == "Crafting Table")
					{
						QuestManager.Instance.RPC_ProgressQuest("buildcrafting", 0, 1);
					}
					else if (data.id == "Excavator")
					{
						QuestManager.Instance.RPC_ProgressQuest("buildexcavator", 0, 1);
					}
				}
				
				return newBuildingObject != null;
			}
			catch (Exception e)
			{
				Debug.LogError($"[PlaceManager] Build 실패: {e.Message}");
				return false;
			}
		}

		private void ProcessDirectConnection(NetworkBuilding newBuilding, BuildingSnapData snapData)
		{
			Debug.Log($"[ProcessDirectConnection] 연결 처리 시작 - 새 건물: {newBuilding.name}, 타겟 ID: {snapData.TargetBuildingId}");
			
			if (Runner.TryFindObject(snapData.TargetBuildingId, out var targetObj))
			{
				var targetBuilding = targetObj.GetComponent<NetworkBuilding>();
				if (targetBuilding == null) 
				{
					Debug.LogError($"[ProcessDirectConnection] 타겟 건물 {snapData.TargetBuildingId}에 NetworkBuilding 컴포넌트가 없음");
					return;
				}
				
				Debug.Log($"[ProcessDirectConnection] 연결 시도: {newBuilding.name} <-> {targetBuilding.name}");
				
				// ⭐ 네트워크 동기화 지연 대응을 위해 다음 프레임에 연결 처리
				_ = DelayedConnectionAsync(newBuilding, targetBuilding);
			}
			else
			{
				Debug.LogError($"[ProcessDirectConnection] 타겟 건물 {snapData.TargetBuildingId}를 찾을 수 없음");
			}
		}
		
		/// <summary>
		/// ⭐ 네트워크 동기화 지연을 고려한 연결 처리
		/// </summary>
		private async UniTaskVoid DelayedConnectionAsync(NetworkBuilding newBuilding, NetworkBuilding targetBuilding)
		{
			// 한 프레임 대기 (네트워크 동기화 완료 대기)
			await UniTask.NextFrame();
			
			if (newBuilding == null || targetBuilding == null)
			{
				Debug.LogError("[DelayedConnectionAsync] 건물이 null로 변경됨");
				return;
			}
			
			Debug.Log($"[DelayedConnectionAsync] 지연된 연결 처리: {newBuilding.name} <-> {targetBuilding.name}");
			
			newBuilding.RegisterSnapConnection(targetBuilding);
            
			// BuildingSystem이 있으면 이벤트 발생
			if (BuildingSystem.Instance != null)
			{
				BuildingSystem.Instance.NotifyBuildingPlaced(newBuilding, targetBuilding);
			}
		}

// 두 빌딩이 실제로 스냅 가능한지 확인하는 헬퍼 메서드
		private static bool AreBuildingsSnapable(NetworkBuilding buildingA, NetworkBuilding buildingB)
		{
			var providerA = buildingA.GetComponent<ISnapProvider>();
			var providerB = buildingB.GetComponent<ISnapProvider>();

			if (providerA?.SnapList == null || providerB?.SnapList == null) return false;

			foreach (var snapA in providerA.SnapList.snapPoints)
			{
				var worldPosA = buildingA.transform.TransformPoint(snapA.localPosition);
				foreach (var snapB in providerB.SnapList.snapPoints)
				{
					if (!_settings.AreTypesCompatible(snapA.type, snapB.type)) continue;

					var worldPosB = buildingB.transform.TransformPoint(snapB.localPosition);

					// 두 스냅 포인트가 충분히 가까운지 확인
					if (Vector3.Distance(worldPosA, worldPosB) <
					    (snapA.snapRadius + snapB.snapRadius) * 0.5f) // 약간의 허용치
					{
						// 방향성 체크도 추가하면 더 정확해짐
						return true;
					}
				}
			}

			return false;
		}

		private async UniTaskVoid OnDestructionItem(NetworkBuilding target)
		{
			Debug.LogWarning("item 삭제해야한다");
			if (!Object.HasInputAuthority || !target) return;


			// (선택 사항) '요청 중'임을 시각적으로 표시할 수 있음
			// target.SetHighlight(true, Color.yellow); 

			var networkObject = target.GetComponent<NetworkObject>();

			try
			{
				var success = await RequestHostActionAsync(() => RpcHostDestructionRequest(networkObject.Id));

				if (success)
				{
					Debug.Log($"[PlaceManager] 파괴 성공: {target.name}");
				}
				else
				{
					Debug.LogWarning($"[PlaceManager] 파괴 실패 또는 타임아웃: {target.name}");
					if (target != null) target.SetHighlight(true);
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[PlaceManager] 파괴 처리 중 오류: {e.Message}");
				if (target != null) target.SetHighlight(true); // 예외 발생 시에도 하이라이트 복원
			}
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
		private void RpcHostDestructionRequest(NetworkId objectId, RpcInfo info = default)
		{
			if (!Object.HasStateAuthority) return;

			var requestingPlayer = info.Source;

			var success = TryDespawnItem(objectId, requestingPlayer);

			Rpc_HostActionResponse(requestingPlayer, success);
		}

		private bool TryDespawnItem(NetworkId objectId, PlayerRef requestingPlayer)
		{
			// Runner null 및 상태 체크
			if (Runner == null)
			{
				Debug.LogError($"[PlaceManager] Runner가 null입니다.");
				return false;
			}

			if (!Runner.isActiveAndEnabled)
			{
				Debug.LogError($"[PlaceManager] Runner가 활성 상태가 아닙니다.");
				return false;
			}

			NetworkObject networkObject = null;
			try
			{
				if (!Runner.TryFindObject(objectId, out networkObject))
				{
					Debug.LogError($"파괴할 오브젝트를 찾지 못함: {objectId}");
					return false;
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[PlaceManager] TryFindObject 실행 중 오류: {e.Message}");
				return false;
			}

			// networkObject null 체크
			if (networkObject == null)
			{
				Debug.LogError($"[PlaceManager] NetworkObject가 null입니다: {objectId}");
				return false;
			}

			try
			{
				var building = networkObject.GetComponent<NetworkBuilding>();
				if (building == null) 
				{
					Debug.LogWarning($"[PlaceManager] NetworkBuilding 컴포넌트를 찾을 수 없음: {objectId}");
					return false;
				}
				
				var connectedBuildings = new List<NetworkBuilding>();
				
				// ConnectedBuildings null 체크 및 안전한 순회
				if (building.ConnectedBuildings.Count > 0 )
				{
					foreach (var connectedId in building.ConnectedBuildings)
					{
						// NetworkRunner.Instances 안전 체크
						if (NetworkRunner.Instances == null || NetworkRunner.Instances.Count <= 0) continue;
						var runner = NetworkRunner.Instances[0];
						if (runner == null || !runner.isActiveAndEnabled) continue;
						try
						{
							if (runner.TryFindObject(connectedId, out var obj))
							{
								var connected = obj?.GetComponent<NetworkBuilding>();
								if (connected != null)
								{
									connectedBuildings.Add(connected);
								}
							}
						}
						catch (Exception e)
						{
							Debug.LogWarning($"[TryDespawnItem] 연결된 건물 찾기 실패 {connectedId}: {e.Message}");
							// 연결된 건물을 찾지 못해도 계속 진행
						}
					}
				}
				
				// BuildingSystem 이벤트 발생
				if (BuildingSystem.Instance != null)
				{
					BuildingSystem.Instance.NotifyBuildingRemoved(building, connectedBuildings);
				}
				
				// 최종 Despawn 전 한 번 더 체크
				if (Runner != null && networkObject != null && networkObject.IsValid)
				{
					try
					{
						Runner.Despawn(networkObject);
						Debug.Log($"[PlaceManager] 오브젝트 파괴 성공: {objectId}");
					}
					catch (Exception despawnEx)
					{
						Debug.LogError($"[PlaceManager] Despawn 실행 중 오류: {despawnEx.Message}");
						return false;
					}
				}
				else
				{
					Debug.LogWarning($"[PlaceManager] Despawn 조건 불만족 - Runner: {Runner != null}, NetworkObject: {networkObject != null}, IsValid: {networkObject?.IsValid}");
					return false;
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[PlaceManager] Despawn 실패: {e.Message}\n{e.StackTrace}");
				return false;
			}

			return true;
		}
	}
}