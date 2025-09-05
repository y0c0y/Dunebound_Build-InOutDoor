using Data;
using Fusion;
using UnityEngine;

public class DoorOnInteraction : NetworkBehaviour, IInteractable
{
	private DoorController _door;
	
	private DoorState _doorState;
	
	public override void Spawned()
	{
		_door = GetComponent<DoorController>();
		_doorState = DoorState.Done;
		_door.OnChangedHingeTargetAngle(_doorState);
	}

	public void OnInteract(PlayerRef playerRef)
	{
		if (!Runner.TryGetPlayerObject(playerRef, out var playerObj))
		{
			Debug.LogError($"No player object for {playerRef}");
			return;
		}

		if (!playerObj.TryGetBehaviour<PlayerEnvironmentTracker>(out var player))
		{
			Debug.LogError($"No player object for {playerObj}");
			return;
		}
		
		OnDoorStateChanged(player);

	}
	
	private void OnDoorStateChanged(PlayerEnvironmentTracker player)
	{
		if (_doorState == DoorState.Done)
		{
			Debug.Log($"OnDoorStateChanged: {_doorState} Player Is Indoor: {player.IsIndoor}");
			_doorState = player.IsIndoor ? DoorState.Exit : DoorState.Enter;
		}
		else
		{
			_doorState = DoorState.Done;
		}
		
		_door.OnChangedHingeTargetAngle(_doorState);
	}
}