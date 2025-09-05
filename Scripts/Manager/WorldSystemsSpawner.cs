using Fusion;
using UnityEngine;

namespace Manager
{
	// GameManager 또는 별도 시스템 객체에
	public class WorldSystemsSpawner : NetworkBehaviour
	{
		[SerializeField] private GameObject indoorSystemPrefab;
		[SerializeField] private GameObject environmentSystemPrefab;
		[SerializeField] private GameObject weatherSystemPrefab;
    
		public override void Spawned()
		{
			if (!HasStateAuthority) return;
			
			Runner.Spawn(indoorSystemPrefab, Vector3.zero);
			//Runner.Spawn(environmentSystemPrefab, Vector3.zero);
			//Runner.Spawn(weatherSystemPrefab, Vector3.zero);
		}
		
		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (!hasState) return;
			
			
		}
	}
}