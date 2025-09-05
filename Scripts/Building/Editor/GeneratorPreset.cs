using Data.Building;
using UnityEngine;

namespace Building.Editor
{
	[CreateAssetMenu(fileName = "GeneratorPreset", menuName = "Scriptable Objects/Generator Preset")]
	public class GeneratorPreset : ScriptableObject
	{
		[Header("Default Layer")] public string previewLayer = "Preview";

		public string networkLayer = "Building";

		[Header("Default Layer Masks")] public LayerMask placementMask;

		public LayerMask destructionMask;
		public LayerMask collisionMask;

		[Header("Default Materials")] public Material previewGreenMaterial;

		public Material previewRedMaterial;

		public BuildingPartType buildingPartType;

		[Header("Default Save Paths")] public string prefabSavePath = "Assets/Prefabs/Building3DModels/Generated";

		public string dataSavePath = "Assets/Resources/Buildings/Data";
	}
}