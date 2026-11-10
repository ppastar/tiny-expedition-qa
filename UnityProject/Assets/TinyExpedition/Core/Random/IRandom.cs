namespace TinyExpedition.Core.Random
{
    /// <summary>
    /// 게임 로직이 쓰는 난수 소스. 테스트·재현을 위해 항상 시드로 만든다.
    /// System.Random은 런타임(Mono/.NET)마다 수열이 다를 수 있어 쓰지 않는다.
    /// </summary>
    public interface IRandom
    {
        /// <summary>[0, maxExclusive) 범위의 정수.</summary>
        int NextInt(int maxExclusive);

        /// <summary>[0, 1) 범위의 실수.</summary>
        double NextDouble();
    }
}
