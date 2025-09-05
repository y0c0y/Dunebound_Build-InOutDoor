using Fusion;

namespace Manager
{
	public class WeatherManager : NetworkBehaviour
	{
		public static WeatherManager  Instance { get; private set; }
		
		[Networked] public NetworkBool IsRaining { get; set; } = false;
		[Networked] public NetworkBool IsSnowing { get; set; } = false;


		public override void Spawned()
		{
			if (!HasStateAuthority) return;

			Instance = this;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (!hasState) return;
			
			Destroy(Instance);
		}

		

	}
}