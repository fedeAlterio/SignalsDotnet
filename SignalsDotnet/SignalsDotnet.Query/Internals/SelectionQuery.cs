using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SignalsDotnet.Query.Internals;

sealed record SelectionArgument(string Name, object? Value)
{
    public bool Equals(SelectionArgument? other) => other is not null
                                                 && Name == other.Name
                                                 && Equals(Value, other.Value);

    public override int GetHashCode() => HashCode.Combine(Name, Value);
}

sealed record SelectionField(string Name,
                             string? Alias,
                             IReadOnlyList<SelectionArgument>? Arguments,
                             IReadOnlyList<SelectionField> Children)
{
    public SelectionField(string name, IReadOnlyList<SelectionField> children) : this(name, null, null, children)
    {
    }

    public string Key => Alias ?? Name;

    public bool IsCall => Arguments is not null;

    public IReadOnlyList<SelectionArgument> ArgumentsOrEmpty => Arguments ?? [];

    public bool Equals(SelectionField? other) => other is not null
                                              && Name == other.Name
                                              && Alias == other.Alias
                                              && IsCall == other.IsCall
                                              && ArgumentsOrEmpty.SequenceEqual(other.ArgumentsOrEmpty)
                                              && Children.SequenceEqual(other.Children);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Alias);
        hash.Add(IsCall);

        foreach (var argument in ArgumentsOrEmpty)
            hash.Add(argument);

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
            string? alias = null;

            SkipWhitespace(query, ref position);

            if (position < query.Length && query[position] == ':')
            {
                position++;
                SkipWhitespace(query, ref position);
                alias = name;
                name = ParseName(query, ref position);
                SkipWhitespace(query, ref position);
            }

            IReadOnlyList<SelectionArgument>? arguments = null;

            if (position < query.Length && query[position] == '(')
            {
                arguments = ParseArguments(query, ref position);
                SkipWhitespace(query, ref position);
            }

            var children = position < query.Length && query[position] == '{'
                ? ParseSelectionSet(query, ref position)
                : [];

            fields.Add(new SelectionField(name, alias, arguments, children));
        }

        if (fields.Count == 0)
            throw new FormatException($"Expected at least one field at position {position}.");

        return fields;
    }

    static IReadOnlyList<SelectionArgument> ParseArguments(string query, ref int position)
    {
        position++;

        var arguments = new List<SelectionArgument>();

        while (true)
        {
            SkipWhitespace(query, ref position);

            if (position < query.Length && query[position] == ')')
                break;

            if (position >= query.Length)
                throw new FormatException($"Expected ')' at position {position}.");

            var name = ParseName(query, ref position);
            SkipWhitespace(query, ref position);

            if (position >= query.Length || query[position] != ':')
                throw new FormatException($"Expected ':' after argument '{name}' at position {position}.");

            position++;
            SkipWhitespace(query, ref position);

            if (arguments.Any(x => x.Name == name))
                throw new FormatException($"Duplicate argument '{name}' at position {position}.");

            arguments.Add(new SelectionArgument(name, ParseValue(query, ref position)));
        }

        if (arguments.Count == 0)
            throw new FormatException($"Expected at least one argument at position {position}.");

        position++;
        return arguments;
    }

    static object? ParseValue(string query, ref int position)
    {
        if (position >= query.Length)
            throw new FormatException($"Expected a value at position {position}.");

        var current = query[position];

        if (current == '"')
            return ParseString(query, ref position);

        if (current is '-' or '+' || char.IsDigit(current))
            return ParseNumber(query, ref position);

        if (char.IsLetter(current) || current == '_')
        {
            var start = position;
            var name = ParseName(query, ref position);

            return name switch
            {
                "true" => true,
                "false" => false,
                "null" => null,
                _ => throw new FormatException($"Unexpected value '{name}' at position {start}.")
            };
        }

        throw new FormatException($"Expected a value at position {position}.");
    }

    static string ParseString(string query, ref int position)
    {
        position++;
        var value = new StringBuilder();

        while (true)
        {
            if (position >= query.Length)
                throw new FormatException($"Unterminated string at position {position}.");

            var current = query[position];

            if (current == '"')
            {
                position++;
                return value.ToString();
            }

            if (current is '\n' or '\r')
                throw new FormatException($"Unterminated string at position {position}.");

            if (current != '\\')
            {
                value.Append(current);
                position++;
                continue;
            }

            position++;

            if (position >= query.Length)
                throw new FormatException($"Unterminated escape sequence at position {position}.");

            var escape = query[position];

            if (escape == 'u')
            {
                if (position + 4 >= query.Length
                 || !ushort.TryParse(query.AsSpan(position + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw new FormatException($"Invalid unicode escape sequence at position {position}.");

                value.Append((char)code);
                position += 5;
                continue;
            }

            value.Append(escape switch
            {
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw new FormatException($"Invalid escape sequence at position {position}.")
            });

            position++;
        }
    }

    static object ParseNumber(string query, ref int position)
    {
        var start = position;

        if (query[position] is '-' or '+')
            position++;

        var digits = 0;

        while (position < query.Length && char.IsDigit(query[position]))
        {
            position++;
            digits++;
        }

        if (digits == 0)
            throw new FormatException($"Expected a number at position {start}.");

        var isFloating = false;

        if (position < query.Length && query[position] == '.')
        {
            isFloating = true;
            position++;

            var fraction = 0;

            while (position < query.Length && char.IsDigit(query[position]))
            {
                position++;
                fraction++;
            }

            if (fraction == 0)
                throw new FormatException($"Expected a digit at position {position}.");
        }

        if (position < query.Length && query[position] is 'e' or 'E')
        {
            isFloating = true;
            position++;

            if (position < query.Length && query[position] is '-' or '+')
                position++;

            var exponent = 0;

            while (position < query.Length && char.IsDigit(query[position]))
            {
                position++;
                exponent++;
            }

            if (exponent == 0)
                throw new FormatException($"Expected a digit at position {position}.");
        }

        var text = query[start..position];

        if (!isFloating && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
            return floating;

        throw new FormatException($"'{text}' at position {start} is not a valid number.");
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
            result[field.Key] = obj.TryGetPropertyValue(field.Name, out var child)
                ? field.Children.Apply(child)
                : null;

        return result;
    }
}
