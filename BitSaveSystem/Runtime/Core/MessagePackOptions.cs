using MessagePack;
using MessagePack.Resolvers;

namespace BitSaveSystem
{
    /// <summary>
    /// Глобальні опції MessagePack для системи.
    /// LZ4BlockArray дає 5–10x швидше декомпресію, ніж GZip.
    /// </summary>
    public static class MessagePackOptions
    {
        public static readonly MessagePackSerializerOptions Standard =
            MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithResolver(ContractlessStandardResolver.Instance);
    }
}
