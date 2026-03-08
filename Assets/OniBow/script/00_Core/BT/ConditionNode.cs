using System;

namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: 조건을 체크하는 리프 노드입니다.
    /// </summary>
    public class ConditionNode : Node
    {
        private readonly Func<bool> m_condition;

        public ConditionNode(Func<bool> condition)
        {
            m_condition = condition;
        }

        public override NodeState Evaluate()
        {
            if (m_condition == null) return NodeState.Failure;
            return m_condition.Invoke() ? NodeState.Success : NodeState.Failure;
        }
    }
}
