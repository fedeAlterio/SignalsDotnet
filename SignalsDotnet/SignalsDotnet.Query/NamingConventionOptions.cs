using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SignalsDotnet.Query;

interface INamingConvention
{
    bool TryGetPropertyName(Type owner, PropertyInfo property, out string? name);

    string GetName(string declaredName);
}

public abstract class NamingConventionOptions
{
    private protected NamingConventionOptions()
    {
    }

    public static NamingConventionOptions Default { get; } = FromJson(SignalsQueryExtensions.DefaultJsonOptions);

    public static NamingConventionOptions FromJson(JsonSerializerOptions options) => new Json(options);

    internal abstract INamingConvention Convention { get; }

    sealed class Json : NamingConventionOptions, INamingConvention
    {
        readonly JsonSerializerOptions _options;

        internal Json(JsonSerializerOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            _options = options.TypeInfoResolver is null
                ? new JsonSerializerOptions(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
                : options;
        }

        internal override INamingConvention Convention => this;

        public bool TryGetPropertyName(Type owner, PropertyInfo property, out string? name)
        {
            foreach (var candidate in _options.GetTypeInfo(owner).Properties)
                if (candidate is { Get: not null, AttributeProvider: PropertyInfo clr } && clr.Name == property.Name)
                {
                    name = candidate.Name;
                    return true;
                }

            name = null;
            return false;
        }

        public string GetName(string declaredName) => _options.PropertyNamingPolicy?.ConvertName(declaredName) ?? declaredName;
    }
}
