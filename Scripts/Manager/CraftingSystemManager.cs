using System;
using Data.Building;
using SObject;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Manager
{
    public class CraftingSystemManager : MonoBehaviour
    {
        public static CraftingSystemManager Instance { get; private set; }
        
        
        [Header("UI")]
        public DonutUI donutUI;
        
        [Header("Building")]
        public GameObject buildingParent;


        public float UserRotation
        {
            get;
            private set;
        }
        
        
        public bool CanChangeCamera => _state is PlayerBuildMode.Build or PlayerBuildMode.Destruction;
        private PlaceableData _selectedItemSo;
        
        private PlayerBuildMode _state;
        private PlayerInputActions _inputActions;
        public PlayerBuildMode CurrentState => _state;

        
        public event Action<PlaceableData, GameObject> CraftItem;
        public event Action CancelCraftItem;
        public event Action BuildItem;
        public event Action<bool> FindDestructionItem;
        public event Action DestructionItem;
        public event Action<PlaceableData> ItemSelected;
        
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            _inputActions = new PlayerInputActions();
        }
        
        private void Start()
        {
            if (_inputActions == null)
            {
                Debug.LogError($"{nameof(_inputActions)} is null");
            }
            
            
            if (donutUI != null)
            {
                donutUI.OnItemSelected += OnItemSelectedFromUI;
                donutUI.OnDeleteModeSelected += OnDeleteModeSelectedFromUI;

                donutUI.OnUIClose += OnUIClose;
            }
        }

        private void OnEnable()
        {
            _inputActions.Player.Confirm.performed += OnConfirmPerformed;
            _inputActions.Player.Cancle.performed += OnCancelPerformed;
            
            _inputActions.Player.ToggleBuild.performed += OnOpenBuildMenuPerformed;
            _inputActions.Player.RotateBuild.performed += OnRotateBuildPerformed;

            
            _inputActions.Player.Enable();
        }

        private void OnDisable()
        {
            _inputActions.Player.Confirm.performed -= OnConfirmPerformed;
            _inputActions.Player.Cancle.performed -= OnCancelPerformed;
            
            _inputActions.Player.ToggleBuild.performed -= OnOpenBuildMenuPerformed;
            _inputActions.Player.RotateBuild.performed -= OnRotateBuildPerformed;

            
            _inputActions.Player.Disable();
        }
        
        private void OnDestroy()
        {
            if (donutUI != null)
            {
                donutUI.OnItemSelected -= OnItemSelectedFromUI;
                donutUI.OnDeleteModeSelected -= OnDeleteModeSelectedFromUI;

                donutUI.OnUIClose -= OnUIClose;
            }
        }
        
        private void OnOpenBuildMenuPerformed(InputAction.CallbackContext context)
        { 
            if (EventSystem.current.IsPointerOverGameObject())
            {
                // UI 요소 위에 마우스가 있을 때는 빌드 메뉴를 열지 않음
                return;
            }
            
            if (_state == PlayerBuildMode.Idle)
            {
                OpenBuildMenu();
            }
        }
        
        public void OpenBuildMenu()
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                //UI 요소 위에 마우스가 있을 때는 빌드 메뉴를 열지 않음
                return;
            }
            
            if (donutUI != null && _state == PlayerBuildMode.Idle)
            {
                StateChange(PlayerBuildMode.UIOpen);
                donutUI.ShowUI();
            }
        }
        private void OnItemSelectedFromUI(PlaceableData selectedItem)
        {
            if (!Global.BuildingUsesRequirements)
            {
                _selectedItemSo = selectedItem;
                Debug.Log($"Item selected: {selectedItem.itemName}");
                // ItemSelected?.Invoke(selectedItem);
                CraftItem?.Invoke(_selectedItemSo, buildingParent);
                return;
            }
            
            var localInventory = Player.Local?.inventory;

            if (localInventory == null)
            {
                Debug.Log("인벤토리가 없음");
                return;
            }

            if (localInventory.HasItems(selectedItem.itemRequirements))
            {
                _selectedItemSo = selectedItem;
                Debug.Log($"Item selected: {selectedItem.itemName}");
                // ItemSelected?.Invoke(selectedItem);
                CraftItem?.Invoke(_selectedItemSo, buildingParent);
            }
            else
            {
                Debug.Log($"Cannot craft {selectedItem.itemName}, Not enough resources.");
                //TODO: ADD FEEDBACK
            }
        }
        
        private void OnUIClose()
        {
            StateChange(PlayerBuildMode.Idle);
        }
        
        private void OnDeleteModeSelectedFromUI()
        {
            FindDestructionItem?.Invoke(true);
            Debug.Log("Delete Mode");

        }
        private void OnRotateBuildPerformed(InputAction.CallbackContext context)
        {
            //Debug.Log($"[Input] RotateBuild performed, State: {_state}");
    
            if (_state != PlayerBuildMode.Build) 
            {
                //Debug.LogWarning("[Input] Not in Build mode, ignoring rotation");
                return;
            }
            var scrollDelta = context.ReadValue<Vector2>();

            if (!(Mathf.Abs(scrollDelta.y) > 0.01f)) return;
            var rotationAmount = scrollDelta.y > 0 ? 90f : -90f;
            UserRotation += rotationAmount;
            UserRotation = UserRotation % 360f;
        
            if (UserRotation < 0) UserRotation += 360f;
        }

        private void OnConfirmPerformed(InputAction.CallbackContext context)
        {
            switch (_state)
            {
                case PlayerBuildMode.Build:
                    BuildItem?.Invoke();
                    break;
                case PlayerBuildMode.Destruction:
                    DestructionItem?.Invoke();
                    break;
                case PlayerBuildMode.Idle:
                    break;
                case PlayerBuildMode.UIOpen:
                    OnUIClose();
                    break;
                default:
                    Debug.LogError("[CraftManager] 잘못된 State 값입니다.");
                    break;
            }
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            switch (_state)
            {
                case PlayerBuildMode.Build:
                    CancelCraftItem?.Invoke();
                    if (_selectedItemSo != null)
                    {
                        ClearSelectedItem();
                    }
                    break;
                case PlayerBuildMode.Destruction:
                    FindDestructionItem?.Invoke(false);
                    if (_selectedItemSo != null)
                    {
                        ClearSelectedItem();
                    }
                    break;
                case PlayerBuildMode.Idle:
                    break;
                case PlayerBuildMode.UIOpen:
                    donutUI?.HideUI();

                    break;
                default:
                    Debug.LogError("[CraftManager] 잘못된 State 값입니다.");
                    break;
            }
        }
        
        public void StateChange(PlayerBuildMode state)
        {
            _state = state;
        }

        private void ClearSelectedItem()
        {
            _selectedItemSo = null;
            UserRotation = 0f;
            Debug.Log("Selected item cleared");
        }
        
        public void SelectItemById(string id)
        {
            if (PlaceableDatabase.Instance == null) return;
            var item = PlaceableDatabase.GetById(id);
            if (item == null) return;
            _selectedItemSo = item;
            ItemSelected?.Invoke(item);
            Debug.Log($"Item selected by ID: {item.itemName}");
        }
    }
}