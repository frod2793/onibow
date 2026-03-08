using System;

namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: 실제 동작을 수행하는 리프 노드입니다.
    /// </summary>
    public class ActionNode : Node
    {
        private readonly Func<NodeState> m_action;

        public ActionNode(Func<NodeState> action)
        {
            m_action = action;
        }

        public override NodeState Evaluate()
        {
            return m_action != null ? m_action.Invoke() : NodeState.Failure;
        }
    }
}
