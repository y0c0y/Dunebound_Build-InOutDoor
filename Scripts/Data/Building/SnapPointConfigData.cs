using System;
using UnityEngine;

namespace Data.Building
{
	[Serializable]
	public class SnapPointConfigData
	{
		[Header("Snap Point Configuration")]
        public SnapType type = SnapType.Wall;
        
        [Header("Transform")]
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        
        [Header("Constraints")]
        [Range(0f, 180f)]
        public float maxAngle = 45f;
        
        [Range(0f, 5f)]
        public float snapRadius = 2f;

        [Header("Additional Settings")]
        public bool isActive = true;
        public int priority = 0;

        public float maxPenetration = 0.01f;  // 각 스냅 포인트별 penetration
        public float gridResolution = 0.5f; 
        
        public Vector3 GetWorldPosition(Transform parent)
        {
            return parent.TransformPoint(localPosition);
        }
        
        public Quaternion GetWorldRotation(Transform parent)
        {
            return parent.rotation * localRotation;
        }
        public Vector3 GetWorldDirection(Transform parent)
        {
            return GetWorldRotation(parent) * Vector3.forward;
        }

        public override string ToString()
        {
            return $"SnapPoint({type}) at {localPosition} (Active: {isActive})";
        }
	}
}