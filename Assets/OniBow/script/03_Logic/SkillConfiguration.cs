using UnityEngine;
using OniBow.Data;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 씬 내에서 스킬에 필요한 수동 할당 참조들(프리팹 등)을 관리하고
    /// VContainer를 통해 주입할 수 있도록 변환해주는 컴포넌트입니다.
    /// </summary>
    public class SkillConfiguration : MonoBehaviour
    {
        [SerializeField] private SkillConfigData m_skillConfigData;
        
        // 씬 내 특정 오브젝트 참조 (MonoBehaviour 주입용)
        [SerializeField] private PlayerControl m_playerControl;
        [SerializeField] private GameObject m_playerHand;
        [SerializeField] private Transform m_playerFirePoint;

        public SkillConfigData ConfigData => m_skillConfigData;
        public PlayerControl Player => m_playerControl;
        public GameObject PlayerHand => m_playerHand;
        public Transform PlayerFirePoint => m_playerFirePoint;
    }
}
