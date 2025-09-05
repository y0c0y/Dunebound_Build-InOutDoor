using System;
using Data;
using Fusion;
using UnityEngine;

public class DoorController : NetworkBehaviour
{
	[SerializeField] private HingeJoint hinge;
	[SerializeField] private float angle = 0f;
	
	public override void Spawned()
	{
		Debug.Log("Spawned DoorController");
	}

	public void OnChangedHingeTargetAngle(DoorState state)
	{
		angle = state switch
		{
			DoorState.Done => 0f,
			DoorState.Enter => -90f,
			_ => 90f
		};

		SetAngle(angle);
	}

	private void SetAngle(float targetAngle)
	{
		var spring = hinge.spring;
		spring.targetPosition = targetAngle;
		hinge.spring = spring;
		
		Debug.Log(spring.targetPosition);
	}
}