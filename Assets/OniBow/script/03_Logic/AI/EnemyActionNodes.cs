using System;
using Cysharp.Threading.Tasks;

namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: 적의 특정 상태를 설정하고 비동기 작업을 수행하는 액션 노드입니다.
    /// </summary>
    public class EnemyActionNode : EnemyNode
    {
        private readonly Func<UniTask> m_asyncAction;
        private bool m_isExecuting;

        public EnemyActionNode(Enemy enemy, Func<UniTask> asyncAction) : base(enemy)
        {
            m_asyncAction = asyncAction;
        }

        public override NodeState Evaluate()
        {
            if (m_isExecuting) return NodeState.Running;

            ExecuteAsync().Forget();
            return NodeState.Running;
        }

        private async UniTaskVoid ExecuteAsync()
        {
            m_isExecuting = true;
            try
            {
                await m_asyncAction();
            }
            finally
            {
                m_isExecuting = false;
            }
        }
    }
}
