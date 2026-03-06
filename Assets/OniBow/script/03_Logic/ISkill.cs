using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 모든 스킬의 기본이 되는 인터페이스입니다.
    /// 구체적인 스킬 로직은 이 인터페이스를 상속받아 구현됩니다.
    /// </summary>
    public interface ISkill
    {
        UniTask ExecuteAsync(SkillContext context, CancellationToken token);
    }

    /// <summary>
    /// [설명]: 스킬 실행 시 필요한 데이터를 전달하는 컨텍스트 클래스입니다.
    /// </summary>
    public class SkillContext
    {
        public Transform Owner { get; set; }
        public Transform Target { get; set; }
        public Transform FirePoint { get; set; }
        public GameObject Hand { get; set; }
        
        public SkillContext(Transform owner, Transform target = null, Transform firePoint = null, GameObject hand = null)
        {
            Owner = owner;
            Target = target;
            FirePoint = firePoint;
            Hand = hand;
        }
    }
}
