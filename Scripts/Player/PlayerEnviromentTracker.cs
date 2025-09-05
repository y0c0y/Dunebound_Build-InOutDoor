using System;
using Cysharp.Threading.Tasks;
using Data;
using Fusion;
using Manager;
using UnityEngine;
using VolumetricFogAndMist2;

public class PlayerEnvironmentTracker : NetworkBehaviour
{
    [Header("Indoor/Outdoor Status")]
    [Networked] public NetworkBool IsIndoor { get; set; }
    [Networked] public int CurrentRoomId { get; set; } = -1;
    
    [Networked] public NetworkBool IsInFog { get; set; }
    
    private bool _wasInFog = false;
    
    private WallRoom _currentRoom;
    private InOutDoorSystem _indoorSystem;
    
    // Events
    public event Action<bool, WallRoom> OnIndoorStatusChanged;
 
    
    public WallRoom CurrentRoom => _currentRoom;
    
    public override void Spawned()
    {
        Debug.Log($"[PlayerEnvironmentTracker] Spawned 호출됨 - HasInputAuthority: {Object.HasInputAuthority}");
        
        if (!Object.HasInputAuthority) 
        {
            Debug.Log($"[PlayerEnvironmentTracker] InputAuthority 없음, 스킵");
            return;
        }
        
        _indoorSystem = InOutDoorSystem.Instance;
        
        // 초기 상태 체크
        CheckInitialEnvironment();
        
        // 이벤트 구독
        SubscribeToEvents();
    }
    
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState) UnsubscribeFromEvents();
    }
    
    private void CheckInitialEnvironment()
    {
        // 시작 위치가 실내인지 체크
        if (_indoorSystem != null)
        {
            var position = transform.position;
            IsIndoor = _indoorSystem.IsIndoor(position);
            
            if (IsIndoor)
            {
                // 현재 룸 찾기
                var room = _indoorSystem.GetRoom(position);
                if (room != null)
                {
                    CurrentRoomId = room.roomId;
                    _currentRoom = room;
                }
            }
        }
    }
    
    private void SubscribeToEvents()
    {
        Debug.Log($"[PlayerEnvironmentTracker] 이벤트 구독 시작 - GameObject: {gameObject.name}");
        
        RoomInfo.OnPlayerEnteredRoom += OnRoomEntered;
        RoomInfo.OnPlayerExitedRoom += OnRoomExited;
        FogColliderSystem.OnPlayerEnteredFog += OnFogEntered;
        FogColliderSystem.OnPlayerExitedFog += OnFogExited;
        
        Debug.Log($"[PlayerEnvironmentTracker] ✅ 모든 이벤트 구독 완료");
        Debug.Log($"[PlayerEnvironmentTracker] HasInputAuthority: {Object.HasInputAuthority}");
    }
    
    private void UnsubscribeFromEvents()
    {
        RoomInfo.OnPlayerEnteredRoom -= OnRoomEntered;
        RoomInfo.OnPlayerExitedRoom -= OnRoomExited;
        FogColliderSystem.OnPlayerEnteredFog -= OnFogEntered;
        FogColliderSystem.OnPlayerExitedFog -= OnFogExited;
    }
    
    private void OnRoomEntered(RoomEventArgs args)
    {
        if (args.player != gameObject || !Object.HasInputAuthority) return;
        
        UpdateIndoorStatus(true, args.roomData);
        
        
        
        // Environment Effect Manager에 알림
        EnvironmentEffectManager.Instance?.UpdateEnvironmentEffects(true, args.roomData);
    }
    
    private void OnRoomExited(RoomEventArgs args)
    {
        if (args.player != gameObject || !Object.HasInputAuthority) return;
        
        // 잠시 후 다시 체크 (다른 룸으로 이동했을 수 있음)
        CheckEnvironmentAfterDelay().Forget();
    }
    
    private async UniTaskVoid CheckEnvironmentAfterDelay()
    {
        await UniTask.Delay(100);
        
        if (_indoorSystem != null)
        {
            var stillIndoor = _indoorSystem.IsIndoor(transform.position);
            
            if (!stillIndoor)
            {
                UpdateIndoorStatus(false, null);
                EnvironmentEffectManager.Instance?.UpdateEnvironmentEffects(false);
            }
        }
    }
    
    private void UpdateIndoorStatus(bool indoor, WallRoom room)
    {
        IsIndoor = indoor;
        CurrentRoomId = room?.roomId ?? -1;
        _currentRoom = room;
        
        if (indoor)
        {
            _wasInFog = IsInFog;
            IsInFog = false; //실내 IsIndoor : true. IsInFog : false
        }
        else
        {
            IsInFog = _wasInFog;
        }
        
        Debug.Log($"[Enviroment Tracker] {IsInFog}");
        
        
        OnIndoorStatusChanged?.Invoke(indoor, room);
    }
    
    private void OnFogEntered(VolumetricFog fogVolume)
    {
        Debug.Log($"[PlayerEnvironmentTracker] OnFogEntered 호출됨 - HasInputAuthority: {HasInputAuthority}");
        
        if (!HasInputAuthority) 
        {
            Debug.Log($"[PlayerEnvironmentTracker] InputAuthority 없음, 스킵");
            return;
        }
        
        IsInFog = fogVolume != null;
        
        Debug.Log($"[PlayerEnvironmentTracker] FogHandler에 VolumetricFog 전달: {(fogVolume != null ? fogVolume.name : "null")}");
    }
    
    private void OnFogExited()
    {
        Debug.Log($"[PlayerEnvironmentTracker] OnFogExited 호출됨 - HasInputAuthority: {HasInputAuthority}");
        
        if (!HasInputAuthority) 
        {
            Debug.Log($"[PlayerEnvironmentTracker] InputAuthority 없음, 스킵");
            return;
        }
        
        IsInFog = false;
        
        Debug.Log($"[PlayerEnvironmentTracker] FogHandler에 null 전달");
    }
}