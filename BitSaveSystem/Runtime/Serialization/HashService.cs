using MessagePack;

namespace BitSaveSystem
{
    /// <summary>
    /// FNV-1a 64-bit. Швидкий, не криптографічний.
    /// Використовується тільки для порівняння станів — не для безпеки.
    /// </summary>
    public static class HashService
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime  = 1099511628211UL;

        public static ulong Fnv1a(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return FnvOffset;
            ulong hash = FnvOffset;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }
            return hash;
        }

        /// <summary>Хеш від серіалізованої таблиці. Повільніше, але універсально.</summary>
        public static ulong HashSaveTable(SaveTable table)
            => Fnv1a(MessagePackSerializer.Serialize(table, MessagePackOptions.Standard));

        /// <summary>Інкрементальне хешування — для генератора (комбінує хеш поля з рантим-станом).</summary>
        public static ulong Combine(ulong seed, ulong value)
        {
            seed ^= value;
            seed *= FnvPrime;
            return seed;
        }

        /// <summary>Хеш одного значення через MessagePack.</summary>
        public static ulong HashValue<T>(T value)
            => Fnv1a(MessagePackSerializer.Serialize(value, MessagePackOptions.Standard));
    }
}
