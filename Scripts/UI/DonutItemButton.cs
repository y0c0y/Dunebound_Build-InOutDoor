using System;
using SObject;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace UI
{
    // EventSystems 네임스페이스를 추가해야 OnPointerEnter/Exit가 UI에서 정상 작동합니다.
    using UnityEngine.EventSystems;

    public class DonutItemButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI References")]
        public Button button;
        public Image iconImage;
        public TextMeshProUGUI nameText;
        public Image backgroundImage;

        [Header("Hover Effects")]
        public Color normalColor = Color.white;
        public Color hoverColor = Color.cyan;
        public float hoverOffset = 30f; // 바깥쪽으로 이동할 거리
        public float hoverDuration = 0.2f;

        private PlaceableData _placeableData;
        private Action<PlaceableData> _onClickCallback;
        private Action<PlaceableData> _onHoverCallback;
        
        // 버튼의 원래 위치를 저장할 변수
        private Vector3 _originalPosition;
        private RectTransform _rectTransform;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (button == null) button = GetComponent<Button>();
            if (iconImage == null) iconImage = transform.Find("Icon")?.GetComponent<Image>();
            if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>();
            if (backgroundImage == null) backgroundImage = GetComponent<Image>();

            if (backgroundImage != null)
            {
                backgroundImage.color = normalColor;
            }
        }

        // Setup 메서드에 initialPosition 파라미터를 추가합니다.
        public void Setup(PlaceableData placeableData, Vector3 initialPosition, Action<PlaceableData> onClickCallback,
            Action<PlaceableData> onHoverCallback)
        {
            _placeableData = placeableData;
            _onClickCallback = onClickCallback;
            _onHoverCallback = onHoverCallback;
            _originalPosition = initialPosition; // 초기 위치 저장

            UpdateUI();

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnButtonClicked);
            }
        }

        private void UpdateUI()
        {
            if (_placeableData == null) return;
            if (iconImage != null && _placeableData.icon != null)
            {
                iconImage.sprite = _placeableData.icon;
            }
            if (nameText != null)
            {
                nameText.text = _placeableData.itemName;
            }
        }

        private void OnButtonClicked()
        {
            if (_onClickCallback == null)
            {
                Debug.LogWarning("Button click callback is null.");
                return;
            }

            if (_placeableData == null)
            {
                Debug.LogWarning("Button click callback is null.");
                return;
            }
                
            
            if (_placeableData != null && _onClickCallback != null)
            {
                transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0), 0.2f)
                    .OnComplete(() => _onClickCallback.Invoke(_placeableData));
            }
        }

        // IPointerEnterHandler 인터페이스 구현
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 원래 위치로부터의 방향 계산 (중심이 0,0이므로 normalize하면 방향이 나옴)
            Vector3 direction = _originalPosition.normalized;
            Vector3 hoverPosition = _originalPosition + direction * hoverOffset;
            
            //Debug.LogWarning($"{direction}");
            
            var hoverScale = Vector3.one * 1.1f; // 명시적으로 설정하는 것이 좋음
            // 위치 이동 애니메이션
            _rectTransform.DOAnchorPos(hoverPosition, hoverDuration).SetEase(Ease.OutQuad);
            
            // 색상 및 크기 애니메이션 (기존 효과)
            if (backgroundImage != null) backgroundImage.DOColor(hoverColor, hoverDuration);
            transform.DOScale(hoverScale, hoverDuration).SetEase(Ease.OutQuad);
            
            _onHoverCallback?.Invoke(_placeableData);
        }

        // IPointerExitHandler 인터페이스 구현
        public void OnPointerExit(PointerEventData eventData)
        {
            // 원래 위치로 복귀
            _rectTransform.DOAnchorPos(_originalPosition, hoverDuration).SetEase(Ease.OutQuad);
            
            // 색상 및 크기 복귀 (기존 효과)
            if (backgroundImage != null) backgroundImage.DOColor(normalColor, hoverDuration);
            transform.DOScale(Vector3.one, hoverDuration).SetEase(Ease.OutQuad);
        }

        private void OnDestroy()
        {
            transform.DOKill();
            if (backgroundImage != null)
            {
                backgroundImage.DOKill();
            }
        }
    }
}