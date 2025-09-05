using System;

using Manager;

using UnityEngine;
using UnityEngine.Serialization;
using VolumetricFogAndMist2;
using Object = System.Object;


namespace Data
{
    public class RoomInfo : MonoBehaviour
    {
        [Header("Room Settings")] public int roomId;
        public InOutDoorSystem indoorSystem;
        
        // 정적 이벤트 (전역에서 구독 가능)
        public static event Action<RoomEventArgs> OnPlayerEnteredRoom;
        public static event Action<RoomEventArgs> OnPlayerExitedRoom;

        // 인스턴스 이벤트 (특정 룸에서만 구독)
        public event Action<RoomEventArgs> OnEnteredThisRoom;
        public event Action<RoomEventArgs> OnExitedThisRoom;
        
        // Fog of War 관련
        private VolumetricFog _nearbyVolumetricFog;
        [SerializeField] private GameObject fogColliderObj;
        private BoxCollider _fogCollider;
        private float _fogOfWarSize;
        private bool _fogOfWarApplied = false; // 한 번만 적용되도록 체크
        
        private WallRoom _cachedRoomData;
        
        private const float ClearDuration = 0.5f;
        private const float RestoreDuration = 2f;

        private void OnDestroy()
        {
            ApplyFogOfWarDestroy();
        }

        private static GameObject GetPlayerObject(Collider other)
        {
            return other.attachedRigidbody.gameObject;
        }
    
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            
            var playerObject = GetPlayerObject(other);
            HandlePlayerEnter(playerObject);
            
        }
    
        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            
            var playerObject = GetPlayerObject(other);
            HandlePlayerExit(playerObject);
        }
        
        private void HandlePlayerEnter(GameObject player)
        {
            Debug.Log($"플레이어가 룸 {roomId}에 입장했습니다.");

            // 룸 데이터 업데이트
            if (indoorSystem != null)
            {
                _cachedRoomData = indoorSystem.GetRoomById(roomId);
            }
            
            //Debug.Log($"젤발좀 실행좀 되래ㅏㅇ너라ㅣㄴ어ㅏ리ㅓㅇ니러너랑러");

            
            // 플레이어 첫 입장 시 Fog of War 적용 (한 번만)
            ApplyFogOfWarOnFirstEnter();

            // 이벤트 발생
            var eventArgs = new RoomEventArgs(
                roomId,
                true,
                _cachedRoomData?.isEnclosed ?? false,
                _cachedRoomData,
                player
            );

            OnPlayerEnteredRoom?.Invoke(eventArgs);
            OnEnteredThisRoom?.Invoke(eventArgs);
        }

        private void HandlePlayerExit(GameObject player)
        {
            Debug.Log($"플레이어가 룸 {roomId}에서 나갔습니다.");

            // 이벤트만 발생 (안개 처리 X)
            var eventArgs = new RoomEventArgs(
                roomId,
                false,
                false,
                _cachedRoomData,
                player
            );

            OnPlayerExitedRoom?.Invoke(eventArgs);
            OnExitedThisRoom?.Invoke(eventArgs);
        }

        
        public void RefreshRoomData()
        {
            if (indoorSystem != null)
            {
                _cachedRoomData = indoorSystem.GetRoomById(roomId);
            }
        }
        
        /// <summary>
        /// VolumetricFog 설정 (콜라이더 생성 시 호출)
        /// </summary>
        public void SetNearbyVolumetricFog(VolumetricFog fog, float fogOfWarSize)
        {
            _nearbyVolumetricFog = fog;
            _fogOfWarSize = fogOfWarSize;
            Debug.Log($"[RoomInfo] 룸 {roomId}에 VolumetricFog 설정됨: {fog.name}");
        }
        
        /// <summary>
        /// 플레이어 입장 시 Fog of War 적용 (한 번만)
        /// </summary>
        private void ApplyFogOfWarOnFirstEnter()
        {
            //Debug.Log($"젤발좀 실행좀 되래ㅏㅇ너라ㅣㄴ어ㅏ리ㅓㅇ니러너랑러");

            if (_fogOfWarApplied)
            {
                Debug.Log("이미 off됨.");
                return;
            }

            if (_nearbyVolumetricFog == null)
            {
                Debug.Log("주변에 인식되는 안게 가 없음.");
                return;
            }
            
            Debug.Log($"젤발좀 실행좀 되래ㅏㅇ너라ㅣㄴ어ㅏ리ㅓㅇ니러너랑러");

            
            var roomCollider = GetComponent<BoxCollider>();
            if (roomCollider == null)
            {
                Debug.LogError($"[RoomInfo] 룸 {roomId}에 BoxCollider가 없음");
                return;
            }
            
            try
            {
                // Fog of War가 비활성화되어 있다면 활성화
                if (!_nearbyVolumetricFog.enableFogOfWar)
                {
                    Debug.Log($"[RoomInfo] ⚠️ Fog of War가 비활성화되어 있음. 활성화 중...");
                    _nearbyVolumetricFog.enableFogOfWar = true;
                }
                
                // 임시 확장된 콜라이더 생성 (roomCollider 크기 * fogOfWarSize)
                fogColliderObj = new GameObject("TempFogCollider");
                fogColliderObj.transform.position = roomCollider.bounds.center;
                
                _fogCollider = fogColliderObj.AddComponent<BoxCollider>();
                _fogCollider.isTrigger = true;
                _fogCollider.size = roomCollider.size * _fogOfWarSize;
                _fogCollider.center = Vector3.zero;
                
                Debug.Log($"[RoomInfo] 🌫️ 룸 {roomId} Fog of War 적용:");
                Debug.Log($"  - 원본 콜라이더 크기: {roomCollider.size}");
                Debug.Log($"  - 확장 배율: {_fogOfWarSize}");
                Debug.Log($"  - 임시 콜라이더 크기: {_fogCollider.size}");
                Debug.Log($"  - 임시 콜라이더 bounds: {_fogCollider.bounds}");
                
                // 임시 콜라이더로 Fog of War 적용
                _nearbyVolumetricFog.SetFogOfWarAlpha(_fogCollider, 0f);
                _nearbyVolumetricFog.UpdateFogOfWar(forceUpload: true);
                
                _fogOfWarApplied = true;
                Debug.Log($"[RoomInfo] ✅ 룸 {roomId} Fog of War 적용 완료 - 플레이어 첫 입장 (임시 콜라이더 사용)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoomInfo] ❌ 룸 {roomId} Fog of War 적용 실패: {e.Message}");
            }
        }
        
         private void ApplyFogOfWarDestroy()
        {
            Debug.Log($"젤발좀 실행좀 되래ㅏㅇ너라ㅣㄴ어ㅏ리ㅓㅇ니러너랑러");
            
            if (_nearbyVolumetricFog == null)
            {
                Debug.Log("주변에 인식되는 안개가 왜 사라졌냐.");
                return;
            }
            
            Debug.Log($"젤발좀 실행좀 되래ㅏㅇ너라ㅣㄴ어ㅏ리ㅓㅇ니러너랑러");

            
            if (_fogCollider == null)
            {
                Debug.LogError($"[RoomInfo] {_fogCollider}가 왜 없음");
                return;
            }
            
            try
            {
                Debug.Log($"[RoomInfo] 🌫️ 룸 {_fogCollider} Fog of War 삭제:");
                Debug.Log($"  - 원본 콜라이더 크기: {_fogCollider.size}");
                Debug.Log($"  - 확장 배율: {_fogOfWarSize}");
                Debug.Log($"  - 임시 콜라이더 크기: {_fogCollider.size}");
                Debug.Log($"  - 임시 콜라이더 bounds: {_fogCollider.bounds}");
                
                // 임시 콜라이더로 Fog of War 적용
                _nearbyVolumetricFog.SetFogOfWarAlpha(_fogCollider, 1f);
                _nearbyVolumetricFog.UpdateFogOfWar(forceUpload: true);
                
                // 임시 콜라이더 제거
                DestroyImmediate(fogColliderObj);
                
                Debug.Log($"[RoomInfo] ✅ 룸 {roomId} Fog of War 적용 완료 - 플레이어 첫 입장 (임시 콜라이더 사용)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoomInfo] ❌ 룸 {roomId} Fog of War 적용 실패: {e.Message}");
            }
        }
        
    }
}