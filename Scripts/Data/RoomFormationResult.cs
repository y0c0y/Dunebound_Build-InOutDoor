using System.Collections.Generic;
using Data.Building;
using Placeable;

namespace Data
{
	public class RoomFormationResult
	{
		public bool CanFormRoom { get; set; }
		public Dictionary<NetworkBuilding, SortedSet<SnapInfo>> SnapGroups { get; set; }
	}
}