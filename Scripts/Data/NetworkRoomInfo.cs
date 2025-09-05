using System;
using Fusion;
using UnityEngine;

namespace Data
{
	
	// RoomInfo 구조체 추가 (간단한 데이터 전송용)
	[Serializable]
	public struct NetworkRoomInfo : INetworkStruct
	{
		public int roomId;
		public NetworkBool isEnclosed;
		public float protectionLevel;
		public float lightLevel;
		public NetworkBool hasVentilation;
		public Vector3 boundsCenter;
		public Vector3 boundsSize;
	}

}