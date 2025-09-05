using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Data.Building;
using Manager;
using SObject;
using UnityEngine;

namespace Placeable
{
	public class PreviewBuilding : MonoBehaviour, ISnapProvider
	{
		private static BuildingSystem _buildingSystem;
		[HideInInspector] public LayerMask placementMask;
		[HideInInspector] public LayerMask collisionMask;
		[HideInInspector] public Vector3 boundsExtents;
		[HideInInspector] public float maxSlopeAngle;
		[HideInInspector] public float globalMaxPenetration = 0.1f; // 전역 설정

		[Header("Movement Settings")] 
		[Range(1f, 30f)]
		public float moveSmoothness = 15f; // 움직임 부드러움 계수 (높을수록 빠름)

		[Header("Visuals")] public Material green;
		public Material red;

		[Header("Terrain Adaptation")] [SerializeField]
		private bool adaptToRoughTerrain = true;

		[SerializeField] private float terrainAdaptationMultiplier = 1.2f;

		[Header("Overlap Settings")] [SerializeField]
		private float groundPenetrationTolerance = 0.2f; // 바닥 파묻힘 허용 (20cm)

		[SerializeField] private float snapOverlapTolerance = 0.05f; // 스냅 시 겹침 허용 (5cm)
		[SerializeField] private float generalOverlapTolerance = 0.01f; // 일반 겹침 허용 (1cm)

		[Header("Placement Behavior")] [SerializeField]
		private bool stickToWalls = true;

		[SerializeField] private float wallStickDistance = 3f;

		private Collider _collider;
		private Collider[] _collisionOverlaps;

		private Ray _currentAimRay;

		// 스냅 가이드 시각화를 위한 필드
		private SnapInfo _currentSnapInfo;
		private bool _isFirstFrame = true;

		private bool _isSnapped;

		private bool _lastBuildableState;
		private Vector3 _lastSurfaceNormal;
		private float _localBottomOffsetY;

		private Camera _mainCamera;

		private Collider[] _nearbyColliders;
		private Renderer[] _renderers;

		private Vector3 _smoothedPosition;
		private Quaternion _smoothedRotation;

		private Vector3 _targetPoint;
		private float _targetRotation;

		private CancellationTokenSource _visualResetCts;
		public Transform Visuals { get; set; }
		public bool IsInitialized { get; private set; }

		public SnapConfigList SnapList { get; private set; }
		public Transform Transform => transform;

		private void Update()
		{
			UpdateColor();
			DrawSnapGuides(); // 스냅 가이드 표시
		}

// OnDestroy에서 정리
		private void OnDestroy()
		{
			_visualResetCts?.Cancel();
			_visualResetCts?.Dispose();
		}

		private void OnDrawGizmos()
		{
			if (!Application.isPlaying) return;

			if (SnapList.snapPoints != null)
				foreach (var snap in SnapList.snapPoints)
				{
					var worldPos = transform.TransformPoint(snap.localPosition);

					Gizmos.color = SnapTypeColors.GetColor(snap.type) * 0.3f;
					Gizmos.DrawWireSphere(worldPos, snap.maxPenetration);

					Gizmos.color = SnapTypeColors.GetColor(snap.type);
					Gizmos.DrawSphere(worldPos, 0.05f);

					var worldRot = transform.rotation * snap.localRotation;
					Gizmos.DrawRay(worldPos, worldRot * Vector3.forward * 0.3f);
				}

			if (_isSnapped && _currentSnapInfo.Success)
			{
				var mySnapWorld = transform.TransformPoint(_currentSnapInfo.MySnapPoint.localPosition);
				var theirSnapWorld =
					_currentSnapInfo.TheirTransform.TransformPoint(_currentSnapInfo.TheirSnapPoint.localPosition);

				Gizmos.color = Color.green;
				Gizmos.DrawLine(mySnapWorld, theirSnapWorld);
			}
		}

		public void Setup(PlaceableData data)
		{
			if (!data)
			{
				Debug.LogWarning("[PreviewObject] Setup: PlaceableData가 null입니다.");
				return;
			}

			_collider = GetComponent<Collider>();
			if (!_collider)
			{
				Debug.LogError("[Preview Object] Collider not found.");
				return;
			}

			_renderers = gameObject.GetComponentsInChildren<Renderer>();
			if (_renderers == null)
			{
				Debug.LogError("[Preview Object] Renderer not found.");
				return;
			}

			placementMask = data.placementMask;
			collisionMask = data.collisionMask;
			boundsExtents = data.boundsExtents;
			maxSlopeAngle = data.maxSlopeAngle;



			if (data.snapList != null && data.snapList.snapPoints != null)
			{
				SnapList = data.snapList;
			}

			const int maxOverlaps = 32;
			_collisionOverlaps = new Collider[maxOverlaps];
			_nearbyColliders = new Collider[maxOverlaps];
			_localBottomOffsetY = transform.position.y - _collider.bounds.min.y;

			if (BuildingSystem.Instance != null)
				_buildingSystem = BuildingSystem.Instance;

			IsInitialized = true;
			_mainCamera = Camera.main;
			_isSnapped = false;
			
		}

		private static float FindHighestGroundPointBeneath(Vector3 center, Quaternion rotation, Vector3 extents,
			LayerMask mask)
		{
			var highestY = float.NegativeInfinity;

			var cornerOffsets = new Vector3[]
			{
				new(extents.x, -extents.y, extents.z),
				new(-extents.x, -extents.y, extents.z),
				new(extents.x, -extents.y, -extents.z),
				new(-extents.x, -extents.y, -extents.z)
			};

			foreach (var offset in cornerOffsets)
			{
				var worldCorner = center + rotation * offset;

				if (!Physics.Raycast(worldCorner + Vector3.up * 1f, Vector3.down, out var hit, 2f, mask)) continue;
				if (hit.point.y > highestY)
					highestY = hit.point.y;
			}

			return highestY;
		}

		public void SetTargetPoint(Vector3 targetPoint, Ray aimRay, float targetRotation)
		{
			_targetPoint = targetPoint;
			_currentAimRay = aimRay;
			if (_isSnapped && Mathf.Abs(_targetRotation - targetRotation) > 45f)
			{
				OnSnapReleased();
				_isSnapped = false;
			}

			_targetRotation = targetRotation;
		}

		public void UpdatePreviewPosition()
		{
			if (!IsInitialized) return;

			// 첫 프레임에서는 즉시 적용
			if (_isFirstFrame)
			{
				_smoothedPosition = transform.position;
				_smoothedRotation = transform.rotation;
				_isFirstFrame = false;
			}

			// 스냅 타겟 찾기
			_currentSnapInfo = FindBestSnapTarget(_targetPoint, _currentAimRay);

			if (_currentSnapInfo.Success)
			{
				ApplySnapTransform(_currentSnapInfo);
				_isSnapped = true;
			}
			else
			{
				if (!_isSnapped) OnSnapReleased();
				// 자유 배치 시 목표 위치와 회전 설정
				var targetPosition = _targetPoint;
				var targetRotation = Quaternion.Euler(0f, _targetRotation, 0f);

				// 부드러운 이동 (떨림 방지)
				_smoothedPosition = Vector3.Lerp(_smoothedPosition, targetPosition, Time.deltaTime * moveSmoothness);
				_smoothedRotation =
					Quaternion.Slerp(_smoothedRotation, targetRotation, Time.deltaTime * moveSmoothness);

				transform.position = _smoothedPosition;
				transform.rotation = _smoothedRotation;

				ApplyFreePlacement();
				_isSnapped = false;
			}

			// if (stickToWalls && !_isSnapped) TryStickToNearbyWall();
		}

		private void ApplySnapTransform(SnapInfo snapInfo)
		{
			// 스냅 포인트 위치
			var theirSnapWorld = snapInfo.TheirTransform.TransformPoint(snapInfo.TheirSnapPoint.localPosition);

			// 사용자가 설정한 회전 유지
			var userRotation = Quaternion.Euler(0f, _targetRotation, 0f);

			// 내 스냅 포인트의 월드 위치 계산 (사용자 회전 적용)
			var mySnapLocal = snapInfo.MySnapPoint.localPosition;
			var mySnapOffset = userRotation * mySnapLocal;

			// 목표 위치 계산
			var targetPosition = theirSnapWorld - mySnapOffset;

			// 부드러운 스냅 (떨림 방지)
			if (Vector3.Distance(transform.position, targetPosition) > 0.01f)
			{
				_smoothedPosition =
					Vector3.Lerp(_smoothedPosition, targetPosition, Time.deltaTime * moveSmoothness * 2f);
				_smoothedRotation =
					Quaternion.Slerp(_smoothedRotation, userRotation, Time.deltaTime * moveSmoothness * 2f);
			}
			else
			{
				_smoothedPosition = targetPosition;
				_smoothedRotation = userRotation;
			}

			transform.position = _smoothedPosition;
			transform.rotation = _smoothedRotation;

			// 3. 간격 문제 해결 - Visuals만 오프셋 적용
			if (Visuals != null)
			{
				// 스냅 대상의 법선 방향으로 아주 작은 오프셋만 적용
				var theirNormal = snapInfo.TheirTransform.rotation * snapInfo.TheirSnapPoint.localRotation *
				                  Vector3.forward;
				var offsetAmount = 0.001f; // 1mm로 줄임 (기존 0.01f에서)
				Visuals.localPosition = transform.InverseTransformDirection(theirNormal * offsetAmount);
			}
		}

		private void ApplyFreePlacement()
		{
			var highestY = FindHighestGroundPointBeneath(
				transform.position,
				transform.rotation,
				boundsExtents,
				placementMask
			);

			if (highestY > float.NegativeInfinity)
			{
				var correctedPosition = transform.position;
				correctedPosition.y = highestY + _localBottomOffsetY;
				transform.position = correctedPosition;
			}
		}

		// PreviewBuilding.cs - FindBestSnapTarget 수정 (면 법선 기반)
		private SnapInfo FindBestSnapTarget(Vector3 targetPoint, Ray aimRay)
		{
			if (BuildingSystem.Instance == null)
			{
				Debug.LogError("[PreviewBuilding] BuildingSystem.Instance가 null입니다!");
				return new SnapInfo { Success = false };
			}
    
			var mySnapProvider = GetComponent<ISnapProvider>();
			if (mySnapProvider == null)
			{
				Debug.LogError("[PreviewBuilding] ISnapProvider가 없습니다!");
				return new SnapInfo { Success = false };
			}
    
			var userRotation = Quaternion.Euler(0f, _targetRotation, 0f);
    
			// BuildingSystem을 통해 스냅 후보 찾기
			var candidates = _buildingSystem.FindAllSnapCandidates(
				mySnapProvider,
				targetPoint,
				userRotation,
				targetPoint,
				5.0f,
				placementMask
			);
			
			if (candidates.Count == 0)
			{
				return new SnapInfo { Success = false };
			}
    
			// 면 법선 기반 필터링 및 점수 계산
			if (aimRay.direction != Vector3.zero)
			{
				var scored = new List<(SnapInfo candidate, float score)>();
				
				foreach (var candidate in candidates)
				{
					// 1. 스냅포인트가 있는 면의 법선 계산
					var snapWorldPos = candidate.TheirTransform.TransformPoint(candidate.TheirSnapPoint?.localPosition ?? Vector3.zero);
					var snapNormal = candidate.TheirTransform.rotation * (candidate.TheirSnapPoint?.localRotation ?? Quaternion.identity) * Vector3.forward;
					
					// 2. 카메라가 그 면을 "정면으로" 보고 있는지 확인
					var cameraToSnap = (snapWorldPos - aimRay.origin).normalized;
					var faceAlignment = Vector3.Dot(aimRay.direction, -snapNormal); // 면의 반대방향과 비교
					var distanceAlignment = Vector3.Dot(aimRay.direction, cameraToSnap);
					
					// 3. 종합 점수 계산
					var distance = Vector3.Distance(targetPoint, snapWorldPos);
					var distanceScore = Mathf.Clamp01(1f - distance / 5.0f);
					var faceScore = Mathf.Clamp01(faceAlignment); // 면을 정면으로 보고 있는 정도
					var directionScore = Mathf.Clamp01(distanceAlignment); // 일반적인 방향 정렬
					
					// 면 정면도를 가장 중요하게, 거리를 두 번째로
					var totalScore = faceScore * 0.6f + distanceScore * 0.3f + directionScore * 0.1f;
					
					// 임계값: 면을 어느 정도 이상 보고 있어야 함
					if (faceScore > 0.2f && IsSnapPointVisible(candidate, aimRay)) // 약 78도 이내 + 가시성 검증
					{
						scored.Add((candidate, totalScore));
					}
				}
				
				// 점수순 정렬해서 최고점 반환
				if (scored.Count > 0)
				{
					var best = scored.OrderByDescending(x => x.score).First();
					
					// 디버깅용 로그 (프레임당 한 번만)
					if (Time.frameCount % 60 == 0 && scored.Count > 1)
					{
						Debug.Log($"[Snap] 최적 스냅 선택: 점수 {best.score:F2}, 후보 {scored.Count}개");
					}
					
					return best.candidate;
				}
			}
    
			// 카메라 방향 정보가 없으면 기존 방식 사용
			return BuildingSystem.Instance.ProcessSnapCandidates(candidates);
		}
		
		// 스냅포인트가 카메라에서 실제로 보이는지 검증
		private bool IsSnapPointVisible(SnapInfo candidate, Ray aimRay)
		{
			if (candidate.TheirSnapPoint == null || candidate.TheirTransform == null) return false;
			
			var snapWorldPos = candidate.TheirTransform.TransformPoint(candidate.TheirSnapPoint.localPosition);
			var directionToSnap = (snapWorldPos - aimRay.origin).normalized;
			var distanceToSnap = Vector3.Distance(aimRay.origin, snapWorldPos);
			
			// 카메라에서 스냅포인트로 레이캐스트
			if (Physics.Raycast(aimRay.origin, directionToSnap, out var hit, distanceToSnap + 0.1f))
			{
				// 스냅포인트가 있는 오브젝트에 직접 닿는지 확인
				return hit.transform == candidate.TheirTransform;
			}
			
			return true; // 레이캐스트 실패시 허용 (장애물이 없다고 가정)
		}
		
		private void DrawSnapGuides()
		{
			if (!_isSnapped || !_currentSnapInfo.Success) return;

			var mySnapWorld = transform.TransformPoint(_currentSnapInfo.MySnapPoint.localPosition);
			var theirSnapWorld =
				_currentSnapInfo.TheirTransform.TransformPoint(_currentSnapInfo.TheirSnapPoint.localPosition);

			Debug.DrawLine(mySnapWorld, theirSnapWorld, Color.green, 0.1f);

			foreach (var snap in SnapList.snapPoints)
			{
				var snapWorld = _currentSnapInfo.TheirTransform.TransformPoint(snap.localPosition);
				var color = SnapTypeColors.GetColor(snap.type);
				Debug.DrawRay(snapWorld, Vector3.up * 0.5f, color, 0.1f);
				Debug.DrawRay(snapWorld, Vector3.right * 0.3f, color, 0.1f);
				Debug.DrawRay(snapWorld, Vector3.forward * 0.3f, color, 0.1f);
			}
		}
		
		public BuildingSnapData GetCurrentSnapData()
		{
			if (!_isSnapped || !_currentSnapInfo.Success)
				return BuildingSnapData.Empty;
    
			// 스냅 대상의 NetworkBuilding 가져오기
			var targetBuilding = _currentSnapInfo.TheirTransform.GetComponent<NetworkBuilding>();
			if (targetBuilding == null || targetBuilding.Object == null)
				return BuildingSnapData.Empty;
    
			// 스냅 인덱스 찾기
			var mySnapIndex = SnapList.snapPoints.IndexOf(_currentSnapInfo.MySnapPoint);
    
			var targetSnapProvider = targetBuilding.GetComponent<ISnapProvider>();
			int theirSnapIndex = -1;
			if (targetSnapProvider?.SnapList != null)
			{
				theirSnapIndex = targetSnapProvider.SnapList.snapPoints.IndexOf(_currentSnapInfo.TheirSnapPoint);
			}
    
			return new BuildingSnapData
			{
				TargetBuildingId = targetBuilding.Object.Id,
				MySnapIndex = mySnapIndex,
				TheirSnapIndex = theirSnapIndex,
				SnapPosition = _currentSnapInfo.TheirTransform.TransformPoint(_currentSnapInfo.TheirSnapPoint.localPosition),
				HasSnapData = true
			};
		}

		private void OnSnapReleased()
		{
			//Debug.Log("[Snap] 스냅 해제됨 - 부드러운 전환 시작");

			_smoothedPosition = transform.position;
			_smoothedRotation = transform.rotation;

			if (Visuals != null)
				ResetVisualsOffset().Forget();
		}

		private async UniTaskVoid ResetVisualsOffset()
		{
			if (Visuals == null) return;

			_visualResetCts?.Cancel();
			_visualResetCts = new CancellationTokenSource();
			var token = _visualResetCts.Token;

			var startOffset = Visuals.localPosition;
			var elapsed = 0f;
			var duration = 0.2f;

			try
			{
				while (elapsed < duration)
				{
					if (token.IsCancellationRequested) return;

					elapsed += Time.deltaTime;
					var t = elapsed / duration;
					Visuals.localPosition = Vector3.Lerp(startOffset, Vector3.zero, t);

					await UniTask.Yield(PlayerLoopTiming.Update, token);
				}

				Visuals.localPosition = Vector3.zero;
			}
			catch (OperationCanceledException)
			{
				// 취소됨 - 정상적인 종료
			}
		}

		private bool IsDirectlySnappedTo(NetworkBuilding other)
		{
			var otherSnapProvider = other.GetComponent<ISnapProvider>();
			return otherSnapProvider?.SnapList && (from mySnap in SnapList.snapPoints
				let mySnapWorld = transform.TransformPoint(mySnap.localPosition)
				from theirSnap in otherSnapProvider.SnapList.snapPoints
				where _buildingSystem.Settings.AreTypesCompatible(mySnap.type, theirSnap.type)
				let theirSnapWorld = other.transform.TransformPoint(theirSnap.localPosition)
				let distance = Vector3.Distance(mySnapWorld, theirSnapWorld)
				where distance < mySnap.snapRadius + theirSnap.snapRadius
				select mySnap).Any();
		}

		private bool IsOnSameFoundation(NetworkBuilding other)
		{
			var myBottom = _collider.bounds.center - Vector3.up * _collider.bounds.extents.y;
			var otherBottom = other.GetComponent<Collider>().bounds.center -
			                  Vector3.up * other.GetComponent<Collider>().bounds.extents.y;

			var foundationMask = LayerMask.GetMask("Ground");

			if (!Physics.Raycast(myBottom, Vector3.down, out var myHit, 0.5f, foundationMask) ||
			    !Physics.Raycast(otherBottom, Vector3.down, out var otherHit, 0.5f, foundationMask)) return false;
			// 같은 오브젝트 위에 있는지 확인
			if (myHit.collider.gameObject != otherHit.collider.gameObject) return false;
			var foundation = myHit.collider.GetComponent<NetworkBuilding>();
			return foundation && foundation.IsRightPartType(BuildingPartType.Foundation);
		}

		private bool IsConnectedViaSnapChain(NetworkBuilding target, int maxDepth)
		{
			if (maxDepth <= 0) return false;

			var visited = new HashSet<NetworkBuilding>();
			var toCheck = new Queue<NetworkBuilding>();
			toCheck.Enqueue(GetComponent<NetworkBuilding>());
			visited.Add(GetComponent<NetworkBuilding>());

			var currentDepth = 0;

			while (toCheck.Count > 0 && currentDepth < maxDepth)
			{
				var nodesInCurrentLevel = toCheck.Count;

				for (var i = 0; i < nodesInCurrentLevel; i++)
				{
					var current = toCheck.Dequeue();
					var size = Physics.OverlapSphereNonAlloc(current.transform.position, 5f, _nearbyColliders,
						LayerMask.GetMask("Building"));

					foreach (var col in _nearbyColliders)
					{
						var building = col.GetComponent<NetworkBuilding>();
						if (!building || visited.Contains(building))
							continue;
						if (current.GetComponent<PreviewBuilding>()?.IsDirectlySnappedTo(building) ?? false)
						{
							if (building == target)
								return true;

							visited.Add(building);
							toCheck.Enqueue(building);
						}
					}
				}

				currentDepth++;
			}

			return false;
		}

		private bool IsOverlapping()
		{
			// 일반 배치 시 검사
			var size = Physics.OverlapBoxNonAlloc(
				_collider.bounds.center,
				_collider.bounds.extents * 0.98f, // 약간의 여유
				_collisionOverlaps,
				transform.rotation,
				collisionMask
			);

			for (var i = 0; i < size; i++)
			{
				var other = _collisionOverlaps[i];
				if (other == _collider) continue;

				// 바닥/지형과의 충돌은 더 관대하게
				if (IsGroundOrTerrain(other))
				{
					if (!IsPenetratingBeyondTolerance(other, groundPenetrationTolerance))
						continue;
				}
				// 일반 오브젝트와의 충돌은 엄격하게
				else
				{
					if (IsPenetratingBeyondTolerance(other, generalOverlapTolerance))
						return true;
				}
			}

			return false;
		}
		
		private bool IsPartOfSnapTargetStructure(Collider other)
		{
			if (!_currentSnapInfo.Success) return false;
    
			var targetBuilding = _currentSnapInfo.TheirTransform.GetComponent<NetworkBuilding>();
			var otherBuilding = other.GetComponent<NetworkBuilding>();
    
			if (!targetBuilding || !otherBuilding) return false;
    
			// 직접 연결 확인
			if (targetBuilding.IsConnectedTo(otherBuilding))
				return true;
    
			// 같은 기초 위에 있는지 확인 (빠른 체크)
			if (IsOnSameFoundationQuick(targetBuilding, otherBuilding))
				return true;
    
			return false;
		}
		
		// 빠른 기초 체크 (성능 최적화)
		private bool IsOnSameFoundationQuick(NetworkBuilding building1, NetworkBuilding building2)
		{
			// 두 건물의 아래쪽에서 레이캐스트
			var pos1 = building1.transform.position;
			var pos2 = building2.transform.position;
    
			// Y 좌표가 너무 다르면 같은 층이 아님
			if (Mathf.Abs(pos1.y - pos2.y) > 1f) return false;
    
			// 거리가 너무 멀면 같은 구조물이 아님
			if (Vector3.Distance(pos1, pos2) > 10f) return false;
    
			// 간단한 기초 체크
			var foundationMask = LayerMask.GetMask("Building");
    
			if (Physics.Raycast(pos1, Vector3.down, out var hit1, 2f, foundationMask) &&
			    Physics.Raycast(pos2, Vector3.down, out var hit2, 2f, foundationMask))
			{
				// 같은 콜라이더에 닿았다면 같은 기초
				return hit1.collider == hit2.collider;
			}
    
			return false;
		}

		// BuildCheckResult 체크 메서드도 수정
		public BuildCheckResult CheckBuildAbility()
		{
			if (_isSnapped && _currentSnapInfo.Success)
			{
				// 스냅 시에는 별도의 오버랩 체크 사용
				if (IsOverlappingExceptSnapTarget())
					return BuildCheckResult.Overlapping;

				return BuildCheckResult.Success;
			}

			// 일반 배치
			if (IsOverlapping())
				return BuildCheckResult.Overlapping;

			if (IsFloatingOrPenetrating())
				return BuildCheckResult.InvalidSurface;

			if (!IsOnValidSurface())
				return BuildCheckResult.InvalidSurface;

			if (!IsSlopeAcceptable())
				return BuildCheckResult.BadSlope;

			return BuildCheckResult.Success;
		}

		private bool IsOverlappingExceptSnapTarget()
		{
			var size = Physics.OverlapBoxNonAlloc(
				_collider.bounds.center,
				_collider.bounds.extents * 0.9f ,
				_collisionOverlaps,
				transform.rotation,
				collisionMask
			);

			for (var i = 0; i < size; i++)
			{
				var other = _collisionOverlaps[i];
				if (other == _collider) continue;

				// 1. 현재 스냅 대상과의 충돌은 허용
				if (_currentSnapInfo.Success && other.transform == _currentSnapInfo.TheirTransform)
				{
					var dynamicTolerance = CalculateSnapTolerance();
					if (!IsPenetratingBeyondTolerance(other, dynamicTolerance))
						continue;
				}

				// 2. 바닥/지형과의 충돌
				else if (IsGroundOrTerrain(other))
				{
					if (!IsPenetratingBeyondTolerance(other, groundPenetrationTolerance))
						continue;
				}
        
				// 3. 스냅 대상과 연결된 구조물과의 충돌 체크
				else if (_currentSnapInfo.Success && IsPartOfSnapTargetStructure(other))
				{
					// 같은 구조물의 일부라면 더 관대한 허용치 적용
					var structureTolerance = 0.1f; // 10cm
					if (!IsPenetratingBeyondTolerance(other, structureTolerance))
						continue;
				}
        
				// 4. 일반 오브젝트와의 충돌
				else
				{
					if (IsPenetratingBeyondTolerance(other, generalOverlapTolerance))
						return true;
				}
			}

			return false;
		}

// 스냅 허용치를 동적으로 계산
		private float CalculateSnapTolerance()
		{
			if (!_currentSnapInfo.Success) return snapOverlapTolerance;

			// 현재 스냅 포인트의 반경 가져오기
			var mySnapRadius = _currentSnapInfo.MySnapPoint.snapRadius;
			var theirSnapRadius = _currentSnapInfo.TheirSnapPoint.snapRadius;

			// 더 작은 반경을 기준으로 허용치 설정 (최대 반경의 80%)
			return Mathf.Min(mySnapRadius, theirSnapRadius) * 0.8f;
		}

// 바닥/지형인지 확인
		private bool IsGroundOrTerrain(Collider other)
		{
			// Layer로 확인
			var groundLayer = LayerMask.NameToLayer("Ground");
			var terrainLayer = LayerMask.NameToLayer("Default");

			return other.gameObject.layer == groundLayer ||
			       other.gameObject.layer == terrainLayer;
		}

// 더 정교한 penetration 체크 수정
		private bool IsPenetratingBeyondTolerance(Collider other, float tolerance)
		{
			var isPenetrating = Physics.ComputePenetration(
				_collider,
				transform.position,
				transform.rotation,
				other,
				other.transform.position,
				other.transform.rotation,
				out var direction,
				out var distance
			);

			if (!isPenetrating) return false;

			// 바닥 방향 penetration은 더 관대하게
			if (IsGroundOrTerrain(other) && direction.y < -0.7f)
				// 바닥에 파묻히는 것은 더 많이 허용
				return distance > tolerance * 2f;

			return distance > tolerance;
		}

		private bool IsFloatingOrPenetrating()
		{
			var bottomPoint = _collider.bounds.center - Vector3.up * _collider.bounds.extents.y;

			if (Physics.Raycast(
				    bottomPoint + Vector3.up * 0.1f,
				    Vector3.down,
				    out var hit,
				    2.0f,
				    placementMask))
			{
				var distanceToFloor = hit.point.y - bottomPoint.y;

				const float maxPenetration = 0.2f; // 20cm까지 파묻힘 허용 (기존 5cm)
				const float maxFloatHeight = 0.1f; // 10cm까지만 떠있기 허용 (기존 50cm)

				return distanceToFloor < -maxPenetration || distanceToFloor > maxFloatHeight;
			}

			return true;
		}


		private bool IsOnValidSurface()
		{
			var rayOrigins = GetBottomRayOrigins();
			var validHits = 0;
			var averageNormal = Vector3.zero;

			foreach (var origin in rayOrigins)
				if (Physics.Raycast(
					    origin + Vector3.up * 0.1f,
					    Vector3.down,
					    out var hit,
					    boundsExtents.y * 3f,
					    placementMask))
				{
					validHits++;
					averageNormal += hit.normal;
				}

			if (validHits < 3)
				return false;

			_lastSurfaceNormal = (averageNormal / validHits).normalized;
			return true;
		}

		private bool IsSlopeAcceptable()
		{
			var angle = Vector3.Angle(Vector3.up, _lastSurfaceNormal);
			return angle <= maxSlopeAngle;
		}

		private Vector3[] GetBottomRayOrigins()
		{
			return new[]
			{
				transform.position, // 중심
				transform.position + transform.right * (boundsExtents.x * 0.5f), // 오른쪽
				transform.position - transform.right * (boundsExtents.x * 0.5f), // 왼쪽
				transform.position + transform.forward * (boundsExtents.z * 0.5f), // 앞
				transform.position - transform.forward * (boundsExtents.z * 0.5f) // 뒤
			};
		}

		private void DrawValidationDebug()
		{
			if (!Application.isPlaying) return;

			// 바닥 체크 포인트 표시
			var rayOrigins = GetBottomRayOrigins();
			foreach (var origin in rayOrigins)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawRay(origin, Vector3.down * 0.5f);
			}

			// 경사면 법선 표시
			if (_lastSurfaceNormal != Vector3.zero)
			{
				Gizmos.color = IsSlopeAcceptable() ? Color.green : Color.red;
				Gizmos.DrawRay(transform.position, _lastSurfaceNormal * 2f);
			}
		}

		private void UpdateColor()
		{
			var result = CheckBuildAbility();
			var canPlace = result == BuildCheckResult.Success;

			var targetMat = canPlace ? green : red;
			var finalColor = targetMat.color;
			var alpha = 1.0f;
			var distanceToCamera = 0f;
			if (_mainCamera) distanceToCamera = Vector3.Distance(transform.position, _mainCamera.transform.position);

			var fadeStartDistance = 2.0f; // 투명해지기 시작하는 거리
			var fadeEndDistance = 1.0f;

			if (distanceToCamera < fadeStartDistance)
				// 거리에 따라 알파값을 1(불투명)에서 0.1(거의 투명) 사이로 조절
				alpha = Mathf.Lerp(0.1f, 1.0f,
					Mathf.InverseLerp(fadeEndDistance, fadeStartDistance, distanceToCamera));
			finalColor.a = alpha;

			// 모든 렌더러에 최종 색상 적용
			foreach (var r in _renderers) r.material.color = finalColor;

			if (!canPlace && Time.frameCount % 30 == 0) // 매 프레임 로그 방지
			{
				//Debug.Log($"[Preview Building] 배치 불가 원인: {result}");

				// 디버그 정보 추가
				//if (_currentSnapResult.Success) Debug.Log($"  - Snap Distance: {_currentSnapResult.SnapDistance:F3}m");
			}
		}
	}
}