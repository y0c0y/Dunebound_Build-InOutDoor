using System;
using Placeable;
using UnityEngine;

namespace Data.Building
{
	// SnapInfo.cs
	public struct SnapInfo : IComparable<SnapInfo>
	{
		public bool Success;
		public SnapPointConfigData MySnapPoint;
		public SnapPointConfigData TheirSnapPoint;
		public Transform TheirTransform;
		public ISnapProvider TheirProvider;
		public float Distance;
		public float Score;
		public Vector3 MySnapWorldPos;
		public Vector3 TheirSnapWorldPos;
    
		// SortedSet을 위한 IComparable 구현
		public int CompareTo(SnapInfo other)
		{
			// Distance 기준 오름차순 정렬
			if (Distance < other.Distance) return -1;
			if (Distance > other.Distance) return 1;
        
			// 거리가 같으면 Score로 비교
			if (Score > other.Score) return -1;
			if (Score < other.Score) return 1;
        
			return 0;
		}
	}
}