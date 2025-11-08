namespace Application;

public static class DictionaryExtensions
{
    public static TValue? GetValueOrNull<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
        where TKey : notnull
    {
        TValue? value;

        dict.TryGetValue(key, out value);

        return value;
    }
}
