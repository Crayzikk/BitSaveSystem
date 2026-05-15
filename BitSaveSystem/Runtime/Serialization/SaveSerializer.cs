using MessagePack;

namespace BitSaveSystem
{
    /// <summary>
    /// Серіалізує/десеріалізує SaveFile через MessagePack + LZ4BlockArray.
    /// Стиснення вмикається/вимикається опціями, передаваними в SaveManager.
    /// </summary>
    public sealed class SaveSerializer
    {
        private readonly bool _compress;

        public SaveSerializer(bool compress = true) { _compress = compress; }

        private MessagePackSerializerOptions GetOptions()
            => _compress
                ? MessagePackOptions.Standard
                : MessagePackOptions.Standard.WithCompression(MessagePackCompression.None);

        public byte[] Serialize(SaveFile file)   => MessagePackSerializer.Serialize(file, GetOptions());
        public SaveFile Deserialize(byte[] data) => MessagePackSerializer.Deserialize<SaveFile>(data, GetOptions());

        public byte[] SerializeMeta(SlotMeta meta)
            => MessagePackSerializer.Serialize(meta, MessagePackOptions.Standard);
        public SlotMeta DeserializeMeta(byte[] data)
            => MessagePackSerializer.Deserialize<SlotMeta>(data, MessagePackOptions.Standard);

        /// <summary>Для Editor Debug Window — конвертує MessagePack у JSON.</summary>
        public string ToDebugJson(byte[] data)
            => MessagePackSerializer.ConvertToJson(data, GetOptions());
    }
}
