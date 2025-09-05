using System.Collections.Generic;
using Data.Building;
using UnityEngine;

namespace SObject
{
	[CreateAssetMenu(fileName = "SnapConfigList", menuName = "Scriptable Objects/SnapConfigList")]
	public class SnapConfigList : ScriptableObject
	{
		public List<SnapPointConfigData> snapPoints = new List<SnapPointConfigData>();
        
        public List<SnapPointConfigData> GetSnapPointsByType(SnapType type)
        {
            var result = new List<SnapPointConfigData>();
            foreach (var snap in snapPoints)
            {
                if (snap.type == type)
                    result.Add(snap);
            }
            return result;
        }
        
        public int GetSnapPointCount()
        {
            return snapPoints?.Count ?? 0;
        }

        public int GetSnapPointCount(SnapType type)
        {
            int count = 0;
            foreach (var snap in snapPoints)
            {
                if (snap.type == type)
                    count++;
            }
            return count;
        }
        
        public void Clear()
        {
            snapPoints.Clear();
        }
        
        public void AddSnapPoint(SnapPointConfigData snapPoint)
        {
            if (snapPoints == null)
                snapPoints = new List<SnapPointConfigData>();
            
            snapPoints.Add(snapPoint);
        }
        
        public string GetDebugInfo()
        {
            var info = $"SnapConfigList '{name}': {GetSnapPointCount()} points\n";
            
            foreach (SnapType type in System.Enum.GetValues(typeof(SnapType)))
            {
                if (type == SnapType.None) continue;
                
                var count = GetSnapPointCount(type);
                if (count > 0)
                    info += $"- {type}: {count}\n";
            }
            
            return info;
        }
	}
}