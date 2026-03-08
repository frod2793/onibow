using System;
using UnityEngine;

namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: 적 전용 BT 노드들의 공통 기반 클래스입니다.
    /// </summary>
    public abstract class EnemyNode : Node
    {
        protected readonly Enemy m_enemy;

        public EnemyNode(Enemy enemy)
        {
            m_enemy = enemy;
        }
    }

    /// <summary>
    /// [설명]: 타겟과의 거리를 확인하는 조건 노드입니다.
    /// </summary>
    public class CheckDistanceNode : EnemyNode
    {
        private readonly float m_threshold;
        private readonly Func<float, float, bool> m_comparison;
        private readonly Transform m_target;

        public CheckDistanceNode(Enemy enemy, Transform target, float threshold, Func<float, float, bool> comparison) : base(enemy)
        {
            m_target = target;
            m_threshold = threshold;
            m_comparison = comparison;
        }

        public override NodeState Evaluate()
        {
            if (m_target == null) return NodeState.Failure;

            float distance = Mathf.Abs(m_target.position.x - m_enemy.transform.position.x);
            return m_comparison(distance, m_threshold) ? NodeState.Success : NodeState.Failure;
        }
    }

    /// <summary>
    /// [설명]: 적의 체력 비율을 확인하는 조건 노드입니다.
    /// </summary>
    public class CheckHealthPercentNode : EnemyNode
    {
        private readonly float m_threshold;
        private readonly EnemyHealth m_health;

        public CheckHealthPercentNode(Enemy enemy, EnemyHealth health, float threshold) : base(enemy)
        {
            m_health = health;
            m_threshold = threshold;
        }

        public override NodeState Evaluate()
        {
            if (m_health == null) return NodeState.Failure;
            float ratio = (float)m_health.CurrentHp / m_health.MaxHp;
            return ratio <= m_threshold ? NodeState.Success : NodeState.Failure;
        }
    }

    /// <summary>
    /// [설명]: 특정 쿨다운이 완료되었는지 확인하는 조건 노드입니다.
    /// </summary>
    public class CheckCooldownNode : Node
    {
        private readonly Func<bool> m_cooldownCheck;

        public CheckCooldownNode(Func<bool> cooldownCheck)
        {
            m_cooldownCheck = cooldownCheck;
        }

        public override NodeState Evaluate()
        {
            return m_cooldownCheck() ? NodeState.Success : NodeState.Failure;
        }
    }
}
