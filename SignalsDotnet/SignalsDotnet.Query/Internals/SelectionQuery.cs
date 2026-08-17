using System.Text.Json.Nodes;

namespace SignalsDotnet.Query.Internals;

sealed record SelectionField(string Name, IReadOnlyList<SelectionField> Children)
{
    public bool Equals(SelectionField? other) => other is not null
                                              && Name == other.Name
                                              && Children.SequenceEqual(other.Children);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);

        foreach (var child in Children)
            hash.Add(child);

        return hash.ToHashCode();
    }
}

static class SelectionQuery
{
    internal static IReadOnlyList<SelectionField> Parse(string query)
    {
        var position = 0;
        SkipWhitespace(query, ref position);

        var fields = position < query.Length && query[position] == '{'
            ? ParseSelectionSet(query, ref position)
            : ParseFields(query, ref position);

        SkipWhitespace(query, ref position);

        if (position < query.Length)
            throw new FormatException($"Unexpected '{query[position]}' at position {position}.");

        return fields;
    }

    static IReadOnlyList<SelectionField> ParseSelectionSet(string query, ref int position)
    {
        position++;
        var fields = ParseFields(query, ref position);

        SkipWhitespace(query, ref position);

        if (position >= query.Length || query[position] != '}')
            throw new FormatException($"Expected '}}' at position {position}.");

        position++;
        return fields;
    }

    static IReadOnlyList<SelectionField> ParseFields(string query, ref int position)
    {
        var fields = new List<SelectionField>();

        while (true)
        {
            SkipWhitespace(query, ref position);

            if (position >= query.Length || query[position] == '}')
                break;

            var name = ParseName(query, ref position);
            SkipWhitespace(query, ref position);

            var children = position < query.Length && query[position] == '{'
                ? ParseSelectionSet(query, ref position)
                : [];

            fields.Add(new SelectionField(name, children));
        }

        if (fields.Count == 0)
            throw new FormatException($"Expected at least one field at position {position}.");

        return fields;
    }

    static string ParseName(string query, ref int position)
    {
        var start = position;

        if (position < query.Length && (char.IsLetter(query[position]) || query[position] == '_'))
            position++;
        else
            throw new FormatException($"Expected a field name at position {position}.");

        while (position < query.Length && (char.IsLetterOrDigit(query[position]) || query[position] == '_'))
            position++;

        return query[start..position];
    }

    static void SkipWhitespace(string query, ref int position)
    {
        while (position < query.Length && (char.IsWhiteSpace(query[position]) || query[position] == ','))
            position++;
    }

    internal static JsonNode? Apply(this IReadOnlyList<SelectionField> fields, JsonNode? node)
    {
        if (node is null)
            return null;

        if (fields.Count == 0)
            return node.DeepClone();

        if (node is JsonArray array)
        {
            var items = new JsonArray();

            foreach (var item in array)
                items.Add(fields.Apply(item));

            return items;
        }

        if (node is not JsonObject obj)
            return null;

        var result = new JsonObject();

        foreach (var field in fields)
            result[field.Name] = obj.TryGetPropertyValue(field.Name, out var child)
                ? field.Children.Apply(child)
                : null;

        return result;
    }
}
