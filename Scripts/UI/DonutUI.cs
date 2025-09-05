using System;
using System.Collections;
using System.Collections.Generic;
using Manager;
using SObject;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace UI
{
    public class DonutUI : MonoBehaviour
    {
        [Header("UI References")]
        public Transform itemContainer;
        public GameObject itemButtonPrefab;
        public GameObject buildUICanvas;
        
        [Header("Donut Settings")]
        public float radius = 150f;
        public float animationDuration = 0.3f;
        public AnimationCurve showCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve hideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        // 이벤트
        public event Action<PlaceableData> OnItemSelected;
        public event Action<PlaceableData> OnItemHovered;
        public event Action OnUIShow;
        public event Action OnUIHide;
        
        public event Action OnUIClose;
        public event Action OnDeleteModeSelected;

        
        private List<DonutItemButton> _itemButtons = new List<DonutItemButton>();
        private bool _isVisible = false;
        private bool _isAnimating = false;
        private void Awake()
        {
            if (buildUICanvas == null)
            {
                Debug.LogError("[Donut UI] Canvas is null");
                return;
            }
            buildUICanvas.SetActive(false);
        }
        
        public void ShowUI()
        {
            if (_isVisible || _isAnimating) return;
            
            _isVisible = true;
            buildUICanvas.SetActive(true);
            
            CreateItemButtons();
            AnimateShow();
            
            OnUIShow?.Invoke();
        }
        
        public void HideUI()
        {
            if (!_isVisible) return;
            
            _isVisible = false;
            AnimateHide(() => {
               buildUICanvas.SetActive(false);
                ClearItemButtons();
            });
            
            OnUIHide?.Invoke();
        }
        
        private void CreateItemButtons()
        {
            ClearItemButtons();
            
            if (PlaceableDatabase.Instance == null || PlaceableDatabase.Instance.placeableList == null) return;
            
            int itemCount = PlaceableDatabase.Instance.placeableList.Count;
            if (itemCount == 0) return;
            
            float angleStep = 360f / itemCount;
            
            for (int i = 0; i < itemCount; i++)
            {
                var placeableData = PlaceableDatabase.Instance.placeableList[i];
                if (placeableData == null) continue;
                
                // 버튼 생성
                GameObject buttonObj = Instantiate(itemButtonPrefab, itemContainer);
                DonutItemButton itemButton = buttonObj.GetComponent<DonutItemButton>();
                
                if (itemButton == null)
                {
                    itemButton = buttonObj.AddComponent<DonutItemButton>();
                }
                
                float angle = (i * angleStep - 90f) * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f
                );
                
                // 버튼 설정
                itemButton.Setup(placeableData, position, OnItemButtonClicked, OnItemButtonHovered);                
                // 위치 계산 (12시 방향부터 시계방향으로)
              
                
                buttonObj.GetComponent<RectTransform>().anchoredPosition = position;
                
                _itemButtons.Add(itemButton);
            }
        }
        
        private void ClearItemButtons()
        {
            foreach (var button in _itemButtons)
            {
                if (button)
                {
                    Destroy(button.gameObject);
                }
            }
            _itemButtons.Clear();
        }
        
        private void OnItemButtonClicked(PlaceableData selectedItem)
        {
            OnItemSelected?.Invoke(selectedItem);
            HideUI();
        }

        private void OnItemButtonHovered(PlaceableData hoveredItem)
        {
            OnItemHovered?.Invoke(hoveredItem);
        }
        
        public void OnDeleteButtonClicked()
        {
            OnDeleteModeSelected?.Invoke();
            HideUI();
        }
        
        private void AnimateShow()
        {
            // 컨테이너 스케일 애니메이션
            itemContainer.localScale = Vector3.zero;
            itemContainer.DOScale(Vector3.one, animationDuration)
                .SetEase(showCurve);
            
            // 버튼들 개별 애니메이션
            for (int i = 0; i < _itemButtons.Count; i++)
            {
                var button = _itemButtons[i];
                if (button != null)
                {
                    button.transform.localScale = Vector3.zero;
                    button.transform.DOScale(Vector3.one, animationDuration)
                        .SetDelay(i * 0.05f)
                        .SetEase(showCurve);
                }
            }
        }
        
        private void AnimateHide(Action onComplete = null)
        {
            // 컨테이너 스케일 애니메이션
            itemContainer.DOScale(Vector3.zero, animationDuration)
                .SetEase(hideCurve)
                .OnComplete(() => onComplete?.Invoke());
        }
        
        private void Update()
        {
            // ESC 키로 UI 닫기
            if (_isVisible && Input.GetKeyDown(KeyCode.Escape))
            {
                OnUIClose?.Invoke();
                HideUI();
            }
        }
    }
}