using SObject;
using UnityEngine;

namespace Placeable
{
	public interface ISnapProvider
	{
		SnapConfigList SnapList { get; }
		Transform Transform { get; }
	}
}