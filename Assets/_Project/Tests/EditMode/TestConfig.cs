using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 코드 기본값으로 만든 설정. 프로젝트의 설정 에셋(플레이테스트로 바뀐 값)과 무관하게 테스트한다.
    static class TestConfig
    {
        public static TennisConfig Create() => ScriptableObject.CreateInstance<TennisConfig>();

        public static void Destroy(TennisConfig config) => Object.DestroyImmediate(config);
    }
}
