using System;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

namespace Manager
{
	public class CameraManager : MonoBehaviour
	{
		public enum CameraMode
		{
			PlayerFollow,
			Build,
			Map
		}
		
		public static CameraManager Instance { get; private set; }

		public static event Action<CameraMode> OnCameraModeChanged;

		[SerializeField] private CinemachineCamera playerFollowCamera;
		[SerializeField] private CinemachineCamera buildCamera;
		[SerializeField] private CinemachineCamera mapCamera;
		[SerializeField] private CinemachineCamera mapFarCamera;

		private Transform _currentTarget;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}

			Instance = this;
		}
		

		public void SetPlayerFollowTarget(Transform playerTarget)
		{
			_currentTarget = playerTarget;
			if (playerFollowCamera != null)
			{
				playerFollowCamera.Follow = _currentTarget;
				playerFollowCamera.LookAt = _currentTarget;
			}

			if (buildCamera != null)
			{
				buildCamera.Follow = _currentTarget;
				buildCamera.LookAt = _currentTarget;
			}
		}


		public void SwitchToBuildMode()
		{
			if (mapCamera)
			{
				mapCamera.gameObject.SetActive(false);
			}

			if (mapFarCamera)
			{
				mapFarCamera.gameObject.SetActive(false);
			}
			
			if (buildCamera == null || playerFollowCamera == null) return;
			
			Debug.Log($"Switching to build mode:");
			if(!CraftingSystemManager.Instance.CanChangeCamera) return;

			buildCamera.Priority = 11;
			playerFollowCamera.Priority = 10;
			OnCameraModeChanged?.Invoke(CameraMode.Build);
		}
		
		public void SwitchToPlayerFollowMode()
		{
			if (mapCamera)
			{
				mapCamera.gameObject.SetActive(false);
			}

			if (mapFarCamera)
			{
				mapFarCamera.gameObject.SetActive(false);
			}
			
			if (buildCamera == null || playerFollowCamera == null) return;
			Debug.Log("Switching to player follow mode");
			
			if(CraftingSystemManager.Instance.CanChangeCamera) return;

			buildCamera.Priority = 9;
			playerFollowCamera.Priority = 10;
			OnCameraModeChanged?.Invoke(CameraMode.PlayerFollow);
		}

		public async UniTaskVoid SwitchToMapMode()
		{
			if (!mapCamera || !mapFarCamera)
			{
				Debug.Log("Cannot switch to map, no map camera on camera manager");
				return;
			}
			
			if (_currentTarget)
			{
				mapCamera.transform.position = new Vector3(_currentTarget.position.x, mapCamera.transform.position.y, _currentTarget.position.z);
				mapFarCamera.transform.position = new Vector3(_currentTarget.position.x, mapFarCamera.transform.position.y, _currentTarget.position.z);
			}

			mapCamera.gameObject.SetActive(true);
			await UniTask.WaitForSeconds(0.2f);
			mapFarCamera.gameObject.SetActive(true);
			OnCameraModeChanged?.Invoke(CameraMode.Map);
		}
	}
}