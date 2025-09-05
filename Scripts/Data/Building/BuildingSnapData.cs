using Fusion;
using UnityEngine;

namespace Data.Building
{
	[System.Serializable]
	public struct BuildingSnapData : INetworkStruct
	{
		public NetworkId TargetBuildingId;
		public int MySnapIndex;
		public int TheirSnapIndex;
		public Vector3 SnapPosition;
		public bool HasSnapData;
        
		public static BuildingSnapData Empty => new BuildingSnapData { HasSnapData = false };
	}
}