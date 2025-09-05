using System.Collections.Generic;
using System.Linq;
using Manager;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SObject
{
    [CreateAssetMenu(fileName = "New PlaceableDatabase", menuName = "Scriptable Objects/PlaceableDatabase")]
    public class PlaceableDatabase : ScriptableObject
    {
        private static PlaceableDatabase _instance;
        public static PlaceableDatabase Instance
        {
            get
            {
                //Debug.Log($"[PlaceableDatabase] Instance 접근 - _instance null 여부: {_instance == null}, IsHost: {PlaceManager.Instance?.HasStateAuthority}");

                if (_instance != null) return _instance;
                _instance = Resources.Load<PlaceableDatabase>("Buildings/PlaceableDatabase"); 

                if (_instance == null)
                {
                    Debug.LogError("[PlaceableDatabase] Resources 폴더에서 인스턴스를 찾을 수 없습니다.");
                    return null;
                }
            
                if (_instance != null)
                {
                    _instance.Init();
                }
                return _instance;
            }
        }

        public List<PlaceableData> placeableList;
        private Dictionary<string, PlaceableData> _dict;
        private bool _isInitialized;

        private void Init()
        {
            if (_isInitialized) return;

            try
            {
                _dict = new Dictionary<string, PlaceableData>();
        
                if (placeableList == null)
                {
                    Debug.LogError("[PlaceableDatabase] placeableList가 null입니다.");
                    return;
                }
        
                foreach (var item in placeableList.Where(item => item != null && !_dict.ContainsKey(item.id)))
                {
                    _dict.Add(item.id, item);
                }
        
                _isInitialized = true;
                Debug.Log($"[PlaceableDatabase] 데이터베이스가 초기화되었습니다. 아이템 수: {_dict.Count}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlaceableDatabase] 초기화 중 오류 발생: {e}");
                _dict = new Dictionary<string, PlaceableData>(); // 빈 딕셔너리라도 생성
            }
        }
    
        public static PlaceableData GetById(string id)
        {
            // 초기화 재시도
            if (Instance == null)
            {
                Debug.LogError("[PlaceableDatabase] Instance가 null입니다.");
                return null;
            }
    
            // _dict가 null이면 Init 재시도
            if (Instance._dict == null)
            {
                Debug.LogWarning("[PlaceableDatabase] _dict가 null입니다. Init을 다시 시도합니다.");
                Instance._isInitialized = false;
                Instance.Init();
            }
    
            if (Instance._dict == null)
            {
                Debug.LogError("[PlaceableDatabase] Init 실패. _dict가 여전히 null입니다.");
                return null;
            }
    
            if (Instance._dict.TryGetValue(id, out var data))
            {
                return data;
            }
    
            Debug.LogWarning($"[PlaceableDatabase] ID '{id}'에 해당하는 데이터를 찾을 수 없습니다.");
            return null;
        }

        [Button("Clean Up Database")]
        private void CleanUp()
        {
            if (placeableList == null) return;
        
            placeableList.RemoveAll(item => item == null);
        
            var names = new HashSet<string>();
            placeableList = placeableList.Where(item => names.Add(item.name)).ToList();
        
            Debug.Log("Database cleaned up.");
        }
    }
}