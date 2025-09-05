using System.Collections.Generic;
using Data.Building;
using UnityEngine;

namespace SObject
{
	[CreateAssetMenu(fileName = "PlaceableData", menuName = "Scriptable Objects/PlaceableData")]
	public class PlaceableData : ScriptableObject
	{
		public string itemName;
		public Sprite icon;
		public string id;

		public BuildingPartType buildingType;
		
		
		[Header("Snap Settings")]
		public SnapConfigList snapList;
		public float snapRadius = 2f;

		[Header("Prefab References")] public GameObject previewPrefab;

		public GameObject networkPrefab;

		[Header("Placement Settings")] public Vector3 boundsExtents = Vector3.one * 0.5f;

		public float maxSlopeAngle = 30f;
		public LayerMask placementMask; // Raycast 할 레이어(지형)
		public LayerMask collisionMask; // 충돌 검사할 레이어(장애물, 빌딩 등)

		[Header("Crafting Requirements")]
		public List<ItemRequirement> itemRequirements;
	}
}