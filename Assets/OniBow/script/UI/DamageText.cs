using UnityEngine;
using TMPro;
using DG.Tweening;
using OniBow.Managers;

namespace OniBow.UI
{
    /// <summary>
    /// 데미지 수치를 표시하고 위로 올라가며 사라지는 텍스트 이펙트 클래스입니다.
    /// </summary>
    public class DamageText : MonoBehaviour
    {
        [Header("컴포넌트")]
        [Tooltip("데미지를 표시할 TextMeshPro 컴포넌트")]
        [SerializeField] private TextMeshProUGUI damageText;

        [Header("애니메이션 설정")]
        [Tooltip("텍스트가 위로 올라갈 거리")]
        [SerializeField] private float moveAmountY = 50f;
        [Tooltip("애니메이션 지속 시간 (초)")]
        [SerializeField] private float duration = 1.5f;
        [Tooltip("애니메이션에 적용할 Ease 타입")]
        [SerializeField] private Ease easeType = Ease.OutQuad;

        private RectTransform _rectTransform;
        private Sequence _animationSequence;

        private void Awake()
        {
            if (damageText == null) damageText = GetComponentInChildren<TextMeshProUGUI>();
            _rectTransform = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            // 재사용 시 이전에 실행되던 트윈이 남아있을 수 있으므로, 확실하게 제거합니다.
            _animationSequence?.Kill();
            // 재사용을 위해 상태를 초기화합니다.
            damageText.alpha = 1f;
        }

        public void PlayAnimation()
        {
            _animationSequence = DOTween.Sequence();

            // 1. 위로 이동하는 애니메이션
            if (_rectTransform != null)
            {
                _animationSequence.Append(_rectTransform.DOAnchorPosY(moveAmountY, duration).SetRelative(true).SetEase(easeType));
            }

            // 2. 페이드 아웃 애니메이션 (이동과 동시에 실행)
            _animationSequence.Join(damageText.DOFade(0, duration).SetEase(easeType));

            // 3. 애니메이션이 끝나면 오브젝트 풀로 반환
            _animationSequence.OnComplete(() =>
            {
                if (EffectManager.Instance != null)
                {
                    EffectManager.Instance.ReturnDamageTextToPool(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            });
        }

        public void SetDamage(int damage)
        {
            if (damageText != null) damageText.text = damage.ToString();
        }

        public void SetAppearance(int damage, float normalScale, float criticalScale, Color normalColor, Color criticalColor, int criticalThreshold)
        {
            if (damageText == null || _rectTransform == null) return;

            bool isCritical = damage >= criticalThreshold;
            damageText.text = damage.ToString();
            damageText.color = isCritical ? criticalColor : normalColor;
            _rectTransform.localScale = Vector3.one * (isCritical ? criticalScale : normalScale);
        }
    }
}