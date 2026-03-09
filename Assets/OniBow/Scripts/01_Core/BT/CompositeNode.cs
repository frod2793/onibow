using System.Collections.Generic;

namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: 여러 자식 노드를 가지는 복합 노드의 기본 클래스입니다.
    /// </summary>
    public abstract class CompositeNode : Node
    {
        protected List<Node> m_children = new List<Node>();

        public CompositeNode AddChild(Node child)
        {
            m_children.Add(child);
            return this;
        }
    }

    /// <summary>
    /// [설명]: 자식 노드 중 하나라도 성공할 때까지 차례대로 실행하는 Selector 노드입니다. (OR logic)
    /// </summary>
    public class Selector : CompositeNode
    {
        public override NodeState Evaluate()
        {
            foreach (var child in m_children)
            {
                switch (child.Evaluate())
                {
                    case NodeState.Running:
                        return NodeState.Running;
                    case NodeState.Success:
                        return NodeState.Success;
                    case NodeState.Failure:
                        continue;
                }
            }
            return NodeState.Failure;
        }
    }

    /// <summary>
    /// [설명]: 모든 자식 노드가 성공할 때까지 차례대로 실행하는 Sequence 노드입니다. (AND logic)
    /// </summary>
    public class Sequence : CompositeNode
    {
        public override NodeState Evaluate()
        {
            bool anyChildRunning = false;
            foreach (var child in m_children)
            {
                switch (child.Evaluate())
                {
                    case NodeState.Running:
                        anyChildRunning = true;
                        return NodeState.Running;
                    case NodeState.Success:
                        continue;
                    case NodeState.Failure:
                        return NodeState.Failure;
                }
            }
            return anyChildRunning ? NodeState.Running : NodeState.Success;
        }
    }
}
