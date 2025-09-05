using System;
using Data.Building;

namespace Building
{
	[Serializable]
	public class SnapEditingData
	{
		private readonly FaceSnapConfig[] faceConfigs = new FaceSnapConfig[6];

		public SnapEditingData()
		{
			for (int i = 0; i < 6; i++)
			{
				faceConfigs[i] = new FaceSnapConfig
				{
					FaceIndex = i,
					Generate = true,
					Type = SnapType.Wall
				};
			}
		}

		public FaceSnapConfig GetFaceConfig(int faceIndex)
		{
			return faceIndex >= 0 && faceIndex < 6 ? faceConfigs[faceIndex] : null;
		}

		public void ClearAll()
		{
			foreach (var config in faceConfigs)
			{
				config.SnapPoints.Clear();
				config.Generate = false;
			}
		}
	}
}