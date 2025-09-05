using Manager;
using UnityEngine;

namespace Data
{
	[System.Serializable]
	public class RoomEventArgs
	{
		public int roomId;
		public bool isIndoor;
		public bool isEnclosed;
		public WallRoom roomData;
		public GameObject player;

		public RoomEventArgs(int id, bool indoor, bool enclosed, WallRoom data, GameObject playerObj)
		{
			roomId = id;
			isIndoor = indoor;
			isEnclosed = enclosed;
			roomData = data;
			player = playerObj;
		}
	}
}