namespace OniBow.AI.BT
{
    /// <summary>
    /// [설명]: Behavior Tree 노드의 실행 상태를 정의합니다.
    /// </summary>
    public enum NodeState
    {
        Running,
        Success,
        Failure
    }

    /// <summary>
    /// [설명]: Behavior Tree의 모든 노드에 대한 기본 추상 클래스입니다.
    /// </summary>
    public abstract class Node
    {
        /// <summary>
        /// [설명]: 노드의 로직을 실행합니다.
        /// </summary>
        /// <returns>노드의 실행 결과 상태를 반환합니다.</returns>
        public abstract NodeState Evaluate();
    }
}
