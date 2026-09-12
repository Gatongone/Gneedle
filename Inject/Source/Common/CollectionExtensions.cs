namespace Gneedle.Inject;

/// <summary>
/// Extensions for the collections which the metadata is built from.
/// </summary>
internal static class CollectionExtensions
{
    /// <summary>
    /// Append an element to the collection when it does not hold it already.
    /// </summary>
    /// <param name="collection">The collection which the element is appended to.</param>
    /// <param name="element">The element which is appended.</param>
    /// <typeparam name="T">The type of the elements of the collection.</typeparam>
    /// <returns>Whether the element was appended.</returns>
    internal static bool TryAdd<T>(this ICollection<T> collection, T element)
    {
        if (collection.Contains(element)) return false;
        collection.Add(element);
        return true;
    }

    /// <summary>
    /// Append every element to the collection, the ones which it holds already included.
    /// </summary>
    /// <param name="collection">The collection which the elements are appended to.</param>
    /// <param name="elements">The elements which are appended.</param>
    /// <typeparam name="T">The type of the elements of the collection.</typeparam>
    internal static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> elements)
    {
        foreach (var element in elements)
        {
            collection.Add(element);
        }
    }

    /// <summary>
    /// Append every element which the collection does not hold yet.
    /// </summary>
    /// <param name="collection">The collection which the elements are appended to.</param>
    /// <param name="elements">The elements which are appended.</param>
    /// <typeparam name="T">The type of the elements of the collection.</typeparam>
    internal static void AddRangeUniquely<T>(this ICollection<T> collection, IEnumerable<T> elements)
    {
        foreach (var element in elements)
        {
            collection.TryAdd(element);
        }
    }
}
