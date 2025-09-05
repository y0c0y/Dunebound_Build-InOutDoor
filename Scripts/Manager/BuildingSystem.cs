using System;
using System.Collections.Generic;
using System.Linq;
using Data.Building;
using Placeable;
using SObject;
using UnityEngine;

namespace Manager
{
	public class BuildingSystem : MonoBehaviour
	{
		public BuildingSystemSettings settings;
		public static BuildingSystem Instance { get; private set; }

		public BuildingSystemSettings Settings
		{
			get => settings;
			private set => settings = value;
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}
    
			Instance = this;
			DontDestroyOnLoad(gameObject); // 씬 전환 시에도 유지
    
			Debug.Log("[BuildingSystem] 초기화 완료");
		}
		public event Action<NetworkBuilding> OnBuildingPlaced;
		public event Action<NetworkBuilding> OnBuildingRemoved;
		public event Action<NetworkBuilding, NetworkBuilding> OnBuildingsConnected;
		public event Action<NetworkBuilding, NetworkBuilding> OnBuildingsDisconnected;
		public static event Action<SnapEventData> OnSnapPointsFound;

		public class SnapEventData
		{
			public List<SnapInfo> ValidSnapPoints;
			public SnapInfo PrimarySnap;
			public Transform SourceTransform;
		}


		// BuildingSystem.cs - FindAllSnapCandidates에 디버그 추가
		public SortedSet<SnapInfo> FindAllSnapCandidates(
			ISnapProvider sourceProvider,
			Vector3 sourcePosition,
			Quaternion sourceRotation,
			Vector3 searchCenter,
			float searchRadius,
			LayerMask searchMask)
		{
			var candidates = new SortedSet<SnapInfo>();
			var colliders = new Collider[32];

			var size = Physics.OverlapSphereNonAlloc(searchCenter, searchRadius, colliders, searchMask);

			for (var i = 0; i < size; i++)
			{
				var col = colliders[i]; // 현재 순번의 콜라이더

				if (col.transform == sourceProvider.Transform) continue;

				var targetProvider = col.GetComponent<ISnapProvider>();
				if (targetProvider?.SnapList == null)
				{
					continue;
				}

				// 모든 스냅 포인트 조합 검사
				foreach (var snapInfo in from mySnap in sourceProvider.SnapList.snapPoints
				         let mySnapWorld = sourcePosition + sourceRotation * mySnap.localPosition
				         from theirSnap in targetProvider.SnapList.snapPoints
				         where settings.AreTypesCompatible(mySnap.type, theirSnap.type)
				         let theirSnapWorld = targetProvider.Transform.TransformPoint(theirSnap.localPosition)
				         let distance = Vector3.Distance(mySnapWorld, theirSnapWorld)
				         let maxSnapDistance = mySnap.snapRadius + theirSnap.snapRadius
				         where distance <= maxSnapDistance
				         select new SnapInfo
				         {
					         Success = true,
					         MySnapPoint = mySnap,
					         TheirSnapPoint = theirSnap,
					         TheirTransform = targetProvider.Transform,
					         TheirProvider = targetProvider,
					         Distance = distance,
					         Score = 1f - (distance / maxSnapDistance),
					         MySnapWorldPos = mySnapWorld,
					         TheirSnapWorldPos = theirSnapWorld
				         })
				{
					candidates.Add(snapInfo);
				}
			}

			return candidates;
		}

		/// <summary>
		/// 우선순위 큐에서 최적의 스냅을 선택하고 관련 스냅들에 이벤트 발생
		/// </summary>
		public SnapInfo ProcessSnapCandidates(
			SortedSet<SnapInfo> candidates,
			float snapGroupRadius = 1f)
		{
			if (candidates.Count == 0)
				return new SnapInfo { Success = false };

			// 가장 가까운 스냅 선택
			var primarySnap = candidates.First();

			// 주 스냅 근처의 다른 스냅들 찾기
			var relatedSnaps = new List<SnapInfo>();
			foreach (var candidate in candidates)
			{
				// 같은 대상의 다른 스냅 포인트들
				if (candidate.TheirTransform == primarySnap.TheirTransform)
				{
					relatedSnaps.Add(candidate);
				}
				// 또는 주 스냅 근처의 다른 오브젝트 스냅들
				else if (Vector3.Distance(candidate.TheirSnapWorldPos, primarySnap.TheirSnapWorldPos) <=
				         snapGroupRadius)
				{
					relatedSnaps.Add(candidate);
				}
			}

			// 스냅 이벤트 발생
			OnSnapPointsFound?.Invoke(new SnapEventData
			{
				ValidSnapPoints = relatedSnaps,
				PrimarySnap = primarySnap,
				SourceTransform = primarySnap.TheirProvider.Transform
			});

			// SnapInfo 반환
			return new SnapInfo
			{
				Success = true,
				MySnapPoint = primarySnap.MySnapPoint,
				TheirSnapPoint = primarySnap.TheirSnapPoint,
				TheirTransform = primarySnap.TheirTransform,
				Score = primarySnap.Score,
				Distance = primarySnap.Distance
			};
		}


		// 건물 배치 완료 시 호출
		public void NotifyBuildingPlaced(NetworkBuilding building, NetworkBuilding targetBuilding)
		{
			if (building == null || targetBuilding == null) return;
			Debug.Log($"[BuildingSystem] NotifyBuildingPlaced: {building.name}");

			OnBuildingPlaced?.Invoke(building);

			building.RegisterSnapConnection(targetBuilding);

			Debug.Log($"[BuildingSystem] 연결 이벤트 발생: {building.name} <-> {targetBuilding.name}");
			OnBuildingsConnected?.Invoke(building, targetBuilding);
		}

		// 건물 제거 시 호출
		public void NotifyBuildingRemoved(NetworkBuilding building, List<NetworkBuilding> connectedBuildings)
		{
			if (building == null || connectedBuildings == null) return;

			// 연결 해제 처리
			building.NotifyDisconnection();

			// 연결 해제 이벤트 발생
			foreach (var connected in connectedBuildings)
			{
				OnBuildingsDisconnected?.Invoke(building, connected);
			}

			OnBuildingRemoved?.Invoke(building);
		}
	}
}
