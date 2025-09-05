using System;
using UnityEngine;
using VolumetricFogAndMist2;



namespace Manager
{
	[RequireComponent(typeof(BoxCollider))]

	public class FogColliderSystem : MonoBehaviour
	{
		[SerializeField] private VolumetricFog fog;
		
		public static event Action<VolumetricFog> OnPlayerEnteredFog;
		public static event Action OnPlayerExitedFog;
		private void OnTriggerEnter(Collider other)
		{
			if(!other.CompareTag("Player")) 
			{
				return;
			}
			
			var playerObject = GetPlayerObject(other);
			HandlePlayerEnter(playerObject);
		}

		private void OnTriggerExit(Collider other)
		{
			if(!other.CompareTag("Player")) return;
			
			var playerObject = GetPlayerObject(other);
			
			HandlePlayerExit(playerObject);
		}
		
		private void HandlePlayerEnter(GameObject player)
		{
			Debug.Log($"[FogColliderSystem] 플레이어 {player.name} fog에 입장했습니다.");
			Debug.Log($"[FogColliderSystem] Fog 정보: {(fog != null ? fog.name : "null")}");
			
			// ⭐ 이벤트 구독자 수 확인
			var subscriberCount = OnPlayerEnteredFog?.GetInvocationList()?.Length ?? 0;
			Debug.Log($"[FogColliderSystem] OnPlayerEnteredFog 구독자 수: {subscriberCount}명");
			
			if (fog != null)
			{
				Debug.Log($"[FogColliderSystem] OnPlayerEnteredFog 이벤트 발생!");
				
				if (subscriberCount > 0)
				{
					OnPlayerEnteredFog?.Invoke(fog);
					Debug.Log($"[FogColliderSystem] ✅ 이벤트 호출 완료!");
				}
				else
				{
					Debug.LogError($"[FogColliderSystem] ❌ 이벤트 구독자가 없습니다!");
				}
			}
			else
			{
				Debug.LogError($"[FogColliderSystem] Fog가 설정되지 않았습니다!");
			}
		}

		private void HandlePlayerExit(GameObject player)
		{
			Debug.Log($"[FogColliderSystem] 플레이어 {player.name} fog에서 나갔습니다.");
			Debug.Log($"[FogColliderSystem] OnPlayerExitedFog 이벤트 발생!");
			OnPlayerExitedFog?.Invoke();
		}
		
		
		private static GameObject GetPlayerObject(Collider other)
		{
			return other.attachedRigidbody.gameObject;
		}

	}
}