using System.Collections.Generic;
using System.Linq;
using Data.Building;
using UnityEngine;
using SnapType = Data.Building.SnapType;

namespace SObject
{
	[CreateAssetMenu(fileName = "BuildingSystemSettings", menuName = "Scriptable Objects/Building System Settings")]
	public class BuildingSystemSettings : ScriptableObject
	{
		[Header("Snap Compatibility Rules")] public List<SnapCompatibilityRule> compatibilityRules;

		[Header("Building Part Presets")] public List<BuildingPartPreset> presets;

		public bool AreTypesCompatible(SnapType typeA, SnapType typeB)
		{
			return typeA == typeB ||
			       compatibilityRules.Any
			       (rule => (rule.typeA == typeA && rule.typeB == typeB)
			                || (rule.typeA == typeB && rule.typeB == typeA));
		}

		public BuildingPartPreset GetPreset(BuildingPartType partType)
		{
			return presets.FirstOrDefault(p => p.partType == partType);
		}
	}
}