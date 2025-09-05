using Data.Building;
using UnityEngine;

namespace Placeable
{
	public static class SnapTypeColors
	{
		public static Color GetColor(SnapType type)
		{
			return type switch
			{
				SnapType.Floor => Color.green,
				SnapType.Wall => Color.blue,
				SnapType.Ceiling => Color.cyan,
				SnapType.Foundation => new Color(0.5f, 0.2f, 0.1f),
				SnapType.Roof => Color.red,
				SnapType.Socket => Color.yellow,
				SnapType.Pillar => Color.magenta,
				SnapType.None => Color.gray,
				_ => Color.white
			};
		}
	}
}