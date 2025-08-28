namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class SafeCastExtensions
    {
        /// <summary>
        /// Filters null elements and casts to non-nullable List.
        /// </summary>
        public static List<T> ToNonNullList<T>(this IEnumerable<T?> source) where T : class
        {
            return source
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();
        }

        /// <summary>
        /// Version for structs (e.g. WeatherForecastModel struct)
        /// </summary>
        public static List<T> ToNonNullStructList<T>(this IEnumerable<T?> source) where T : struct
        {
            return source
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .ToList();
        }

        public static List<T> ToNonNullList<T>(this IEnumerable<T?> source, Action<string>? logger = null) where T : class
        {
            var result = new List<T>();
            foreach (var item in source)
            {
                if (item is not null)
                    result.Add(item);
                else
                    logger?.Invoke($"[Warning] Null value filtered from {typeof(T).Name}");
            }
            return result;
        }
    }
}




















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
