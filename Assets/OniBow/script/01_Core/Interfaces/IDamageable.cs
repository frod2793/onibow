namespace OniBow
{
    /// <summary>
    /// [설명]: 데미지를 입을 수 있는 모든 객체가 구현해야 하는 인터페이스입니다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// [설명]: 입력을 받은 데미지를 처리합니다.
        /// </summary>
        /// <param name="damage">데미지 수치</param>
        void TakeDamage(int damage);
    }
}