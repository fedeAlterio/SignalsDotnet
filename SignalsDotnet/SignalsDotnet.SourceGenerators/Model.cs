using System.Collections;
using Microsoft.CodeAnalysis;

namespace SignalsDotnet.SourceGenerators;

sealed record SignalPropertyModel(
    string Name,
    string TypeName,
    string FieldName,
    string SignalPropertyName,
    string Accessibility,
    string? SetterAccessibility,
    bool IsInitOnly,
    bool HasSetter);

sealed record ComputedPropertyModel(
    string Name,
    string MethodName,
    string TypeName,
    string FieldName,
    string SignalPropertyName,
    string Accessibility);

sealed record AsyncComputedPropertyModel(
    string Name,
    string MethodName,
    string TypeName,
    string FieldName,
    string SignalPropertyName,
    string IsComputingPropertyName,
    string Accessibility,
    string ConcurrentChangeStrategy,
    bool ReturnsTask);

sealed record TypeDeclarationModel(string Keyword, string Name, string Constraints);

sealed record SignalClassModel(
    string? Namespace,
    EquatableArray<TypeDeclarationModel> Hierarchy,
    EquatableArray<SignalPropertyModel> Properties,
    EquatableArray<ComputedPropertyModel> ComputedProperties,
    EquatableArray<AsyncComputedPropertyModel> AsyncComputedProperties,
    bool NotifyPropertyChangedRequested,
    bool AlreadyImplementsINotifyPropertyChanged,
    string ClassName,
    string HintName);

readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    readonly T[] _items = items;

    public T[] Items => _items ?? [];
    public int Count => Items.Length;
    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var items = Items;
        var otherItems = other.Items;
        if (items.Length != otherItems.Length)
            return false;

        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].Equals(otherItems[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
}
