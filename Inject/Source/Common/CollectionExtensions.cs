namespace Gneedle.Inject;

internal static class CollectionExtensions
{
    internal static bool TryAdd<T>(this ICollection<T> collection, T element)
    {
        if (collection.Contains(element)) return false;
        collection.Add(element);
        return true;
    }

    internal static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> elements)
    {
        foreach (var element in elements)
        {
            collection.Add(element);
        }
    }

    internal static void AddRangeUniquely<T>(this ICollection<T> collection, IEnumerable<T> elements)
    {
        foreach (var element in elements)
        {
            collection.TryAdd(element);
        }
    }
}