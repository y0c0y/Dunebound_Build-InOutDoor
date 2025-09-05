using System;
using System.Collections.Generic;
using UnityEngine;

namespace Data.Building
{
	[Serializable]
	public class FaceSnapConfig
	{
		public int FaceIndex;
		public bool Generate = true;
		public SnapType Type = SnapType.Wall;
		public List<SnapPointConfigData> SnapPoints = new();
	}
}