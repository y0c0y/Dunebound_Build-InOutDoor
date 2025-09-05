using System;
using System.Collections.Generic;
using Data.Building;
using Fusion;
using SObject;
using UnityEngine;

namespace Placeable
{
	public class NetworkBuilding : NetworkBehaviour, ISnapProvider
	{
		[Networked] public NetworkString<_16> ItemID { get; set; }
		private bool _isInitialized;
		private Material[] _materials;
		private Renderer[] _renderers;
		[SerializeField] public BuildingPartType buildingType;

		public bool IsWallRelated() =>
			buildingType is BuildingPartType.Wall or BuildingPartType.Door or BuildingPartType.Window;
		
		public bool IsRightPartType(BuildingPartType otherPartType) => buildingType == otherPartType;
		
		[Networked, Capacity(32)]
		public NetworkLinkedList<NetworkId> ConnectedBuildings { get;}
		
		public void RegisterSnapConnection(NetworkBuilding other)
		{
			if (other == null || other.Object == null) return;
			
			Debug.Log($"[NetworkBuilding] 연결 등록 시도: {name} <-> {other.name}");
    
			// ⭐ 양방향 연결 보장
			RegisterOneWayConnection(this, other);
			RegisterOneWayConnection(other, this);
		}
		
		/// <summary>
		/// ⭐ 단방향 연결 등록 (중복 및 용량 체크 포함)
		/// </summary>
		private static void RegisterOneWayConnection(NetworkBuilding from, NetworkBuilding to)
		{
			if (from == null || to == null || from.Object == null || to.Object == null) 
			{
				Debug.LogError($"[NetworkBuilding] RegisterOneWayConnection - Null 객체: from={from}, to={to}");
				return;
			}
			
			Debug.Log($"[NetworkBuilding] 연결 등록 시도: {from.name}(ID:{from.Object.Id}) -> {to.name}(ID:{to.Object.Id})");
			
			// 용량 체크
			if (from.ConnectedBuildings.Count >= from.ConnectedBuildings.Capacity - 1)
			{
				Debug.LogWarning($"[NetworkBuilding] {from.name} ConnectedBuildings가 거의 가득 참: {from.ConnectedBuildings.Count}/{from.ConnectedBuildings.Capacity}");
				return;
			}
    
			// 중복 체크
			if (from.ConnectedBuildings.Contains(to.Object.Id)) 
			{
				Debug.LogWarning($"[NetworkBuilding] 이미 연결됨: {from.name} -> {to.name}");
				LogCurrentConnections(from);
				return;
			}
    
			try
			{
				from.ConnectedBuildings.Add(to.Object.Id);
				Debug.Log($"[NetworkBuilding] ✅ 연결 성공: {from.name} -> {to.name} (총 {from.ConnectedBuildings.Count}개 연결)");
				LogCurrentConnections(from);
			}
			catch (InvalidOperationException e)
			{
				Debug.LogError($"[NetworkBuilding] ❌ 연결 추가 실패 {from.name} -> {to.name}: {e.Message}");
			}
		}
		
		/// <summary>
		/// ⭐ 현재 연결 상태를 로그로 출력 (디버깅용)
		/// </summary>
		private static void LogCurrentConnections(NetworkBuilding building)
		{
			if (building?.ConnectedBuildings == null) return;
			
			var connections = new List<string>();
			foreach (var connectedId in building.ConnectedBuildings)
			{
				// 연결된 객체가 실제로 존재하는지 확인
				if (building.Runner != null && building.Runner.TryFindObject(connectedId, out var obj))
				{
					var connectedBuilding = obj.GetComponent<NetworkBuilding>();
					if (connectedBuilding != null)
					{
						connections.Add($"{connectedBuilding.name}({connectedBuilding.buildingType})");
					}
					else
					{
						connections.Add($"ID:{connectedId}(Component없음)");
					}
				}
				else
				{
					connections.Add($"ID:{connectedId}(객체없음)");
				}
			}
			
			Debug.Log($"[NetworkBuilding] {building.name}의 현재 연결: [{string.Join(", ", connections)}] (총 {building.ConnectedBuildings.Count}개)");
		}

		// NetworkBuilding 파괴 시 호출될 메서드
		public void NotifyDisconnection()
		{
			if (!HasStateAuthority) return;

			foreach (var connectedId in ConnectedBuildings)
			{
				if (!Runner.TryFindObject(connectedId, out var obj)) continue;
				var otherBuilding = obj.GetComponent<NetworkBuilding>();
				if (otherBuilding != null)
				{
					// 상대방의 리스트에서 나를 제거
					otherBuilding.ConnectedBuildings.Remove(this.Object.Id);
				}
			}
		}

		public bool IsConnectedTo(NetworkBuilding other)
		{
			return ConnectedBuildings.Contains(other.Object.Id);
		}
		
		/// <summary>
		/// ⭐ 현재 연결 상태를 컨텍스트 메뉴에서 확인 (디버깅용)
		/// </summary>
		[ContextMenu("Show Connection Status")]
		public void ShowConnectionStatus()
		{
			Debug.Log($"=== {name} 연결 상태 ===");
			LogCurrentConnections(this);
			
			// 역방향 연결도 확인
			Debug.Log($"=== 역방향 연결 확인 ===");
			foreach (var connectedId in ConnectedBuildings)
			{
				if (Runner != null && Runner.TryFindObject(connectedId, out var obj))
				{
					var otherBuilding = obj.GetComponent<NetworkBuilding>();
					if (otherBuilding != null)
					{
						bool hasReverseConnection = otherBuilding.ConnectedBuildings.Contains(this.Object.Id);
						Debug.Log($"{otherBuilding.name} -> {name}: {(hasReverseConnection ? "✅ 있음" : "❌ 없음")}");
					}
				}
			}
		}
		
		private void OnDrawGizmosSelected()
		{
			if (SnapList?.snapPoints == null) return;

			foreach (var snapPoint in SnapList.snapPoints)
			{
				var worldPos = transform.TransformPoint(snapPoint.localPosition);

				// 스냅 타입에 따라 색상 변경
				Gizmos.color = snapPoint.type switch
				{
					SnapType.Wall => Color.blue,
					SnapType.Floor => Color.green,
					SnapType.Ceiling => Color.cyan,
					SnapType.Foundation => Color.brown,
					SnapType.Roof => Color.red,
					SnapType.Socket => Color.yellow,
					SnapType.Pillar => Color.magenta,
					_ => Color.white
				};

				Gizmos.DrawWireSphere(worldPos, 0.1f);

				// 방향 표시
				var worldRot = transform.rotation * snapPoint.localRotation;
				Gizmos.DrawRay(worldPos, worldRot * Vector3.forward * 0.3f);
			}
		}

		public SnapConfigList SnapList { get; private set; }
		public Transform Transform => transform;

		public override void Spawned()
		{
			base.Spawned();
			
			LoadData();
			
			_renderers = GetComponentsInChildren<Renderer>();
			if (_renderers == null) return;
			_materials = new Material[_renderers.Length];
			for (var i = 0; i < _renderers.Length; i++) _materials[i] = _renderers[i].sharedMaterial;
			
		}

		private void LoadData()
		{
			if (string.IsNullOrEmpty(ItemID.Value))
			{
				Debug.LogError($"[Network Object] {ItemID.Value} Item ID is empty.");
				return;
			}

			var data = PlaceableDatabase.GetById(ItemID.Value);
			if (data != null)
			{
				SnapList = data.snapList;
				Debug.Log($"Data loaded for item '{ItemID.Value}' on {(Runner.IsServer ? "Host" : "Client")}");
			}
			else
			{
				Debug.LogWarning($"[NetworkBuilding] Data not found for ID: {ItemID.Value}");
			}
		}

		public void SetHighlight(bool highlight)
		{
			if (!highlight)
			{
				for (var i = 0; i < _renderers.Length; i++) _renderers[i].material = _materials[i];

				return;
			}

			if (_renderers == null) return;

			var m = Color.red;

			foreach (var t in _renderers) t.material.color = m;
		}
	}
}