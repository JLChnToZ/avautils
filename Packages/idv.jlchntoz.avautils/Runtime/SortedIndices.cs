using System;
using System.Collections;
using System.Collections.Generic;

public static class SortedIndices {
    public static int[] GetSortedIndices<T>(this T[] array, IComparer<T> comparer = null) =>
        GetSortedIndices(array as IReadOnlyList<T>, comparer);

    public static int[] GetSortedIndices<T>(this IReadOnlyList<T> list, IComparer<T> comparer = null) {
        if (list == null) throw new ArgumentNullException(nameof(list));
        comparer ??= Comparer<T>.Default;
        var indices = CreateArrayWithIndices(list.Count);
        Array.Sort(indices, new Comparar<T>(list, comparer));
        return indices;
    }

    public static int[] GetSortedIndices<T>(this IList<T> list, IComparer<T> comparer = null) {
        if (list == null) throw new ArgumentNullException(nameof(list));
        comparer ??= Comparer<T>.Default;
        var indices = CreateArrayWithIndices(list.Count);
        if (list is not IReadOnlyList<T> readOnlyList) readOnlyList = new ReadOnlyWrapper<T>(list);
        Array.Sort(indices, new Comparar<T>(readOnlyList, comparer));
        return indices;
    }

    static int[] CreateArrayWithIndices(int count) {
        var indices = new int[count];
        for (var i = 0; i < count; i++)
            indices[i] = i;
        return indices;
    }

    class Comparar<T> : IComparer<int> {
        private readonly IComparer<T> comparer;
        private readonly IReadOnlyList<T> list;

        public Comparar(IReadOnlyList<T> list, IComparer<T> comparer) {
            this.list = list;
            this.comparer = comparer;
        }

        public int Compare(int x, int y) {
            if (x == y) return 0;
            return comparer.Compare(list[x], list[y]);
        }
    }

    class ReadOnlyWrapper<T> : IReadOnlyList<T> {
        private readonly IList<T> list;

        public ReadOnlyWrapper(IList<T> list) =>
            this.list = list ?? throw new ArgumentNullException(nameof(list));

        public T this[int index] => list[index];

        public int Count => list.Count;

        public IEnumerator<T> GetEnumerator() => list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}