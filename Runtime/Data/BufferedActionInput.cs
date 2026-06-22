using System;

namespace NiumaAction.Data
{
    // 输入缓冲有明确有效期；到达 ExpiresAtTime 后 ActionService 应丢弃它，
    // 即使之后出现同名 InputId 的转换也不能继续消费。
    [Serializable]
    public sealed class BufferedActionInput
    {
        public string InputId;
        public float BufferedAtTime;
        public float ExpiresAtTime;

        public BufferedActionInput Clone()
        {
            return new BufferedActionInput
            {
                InputId = InputId,
                BufferedAtTime = BufferedAtTime,
                ExpiresAtTime = ExpiresAtTime
            };
        }

        public static BufferedActionInput[] CloneArray(BufferedActionInput[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<BufferedActionInput>();
            }

            var clone = new BufferedActionInput[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }
    }
}
