using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SignalsDotnet.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class SignalsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("SignalsDotnet.Attributes.g.cs", Attributes.Source));

        var wholeClassModels = context.SyntaxProvider
                                      .ForAttributeWithMetadataName(Attributes.GenerateSignalsAttributeName,
                                                                    static (node, _) => node is TypeDeclarationSyntax,
                                                                    static (ctx, ct) => Parse(ctx.TargetSymbol as INamedTypeSymbol,
                                                                                              ctx.TargetNode as TypeDeclarationSyntax,
                                                                                              ctx.SemanticModel.Compilation,
                                                                                              wholeClass: true,
                                                                                              ct))
                                      .Where(static x => x is not null);

        var signalMembers = ParseContainingType(context, Attributes.SignalAttributeName,
                                                static node => node is PropertyDeclarationSyntax);

        var computedMembers = ParseContainingType(context, Attributes.ComputedAttributeName,
                                                  static node => node is MethodDeclarationSyntax);

        var asyncComputedMembers = ParseContainingType(context, Attributes.AsyncComputedAttributeName,
                                                       static node => node is MethodDeclarationSyntax);

        var perMemberModels = signalMembers.Collect()
                                     .Combine(computedMembers.Collect())
                                     .Combine(asyncComputedMembers.Collect())
                                     .SelectMany(static (tuple, _) =>
                                         tuple.Left.Left.Concat(tuple.Left.Right).Concat(tuple.Right)
                                              .Where(static x => x is not null)
                                              .GroupBy(static x => x!.Model?.HintName ?? "")
                                              .Select(static group => group.First())
                                              .ToImmutableArray());

        context.RegisterSourceOutput(wholeClassModels, static (ctx, result) => Emit(ctx, result!));
        context.RegisterSourceOutput(perMemberModels, static (ctx, result) => Emit(ctx, result!));
    }

    static IncrementalValuesProvider<ParseResult?> ParseContainingType(IncrementalGeneratorInitializationContext context,
                                                                       string attributeName,
                                                                       Func<SyntaxNode, bool> nodeFilter)
    {
        return context.SyntaxProvider
                      .ForAttributeWithMetadataName(attributeName,
                                                    (node, _) => nodeFilter(node),
                                                    (ctx, ct) =>
                                                    {
                                                        var type = ctx.TargetSymbol.ContainingType;
                                                        if (type is null || HasAttribute(type, Attributes.GenerateSignalsAttributeName))
                                                            return null;

                                                        var syntax = type.DeclaringSyntaxReferences
                                                                         .Select(x => x.GetSyntax(ct))
                                                                         .OfType<TypeDeclarationSyntax>()
                                                                         .FirstOrDefault();

                                                        return Parse(type, syntax, ctx.SemanticModel.Compilation, wholeClass: false, ct);
                                                    })
                      .Where(static x => x is not null);
    }

    static void Emit(SourceProductionContext context, ParseResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (result.Model is { } model)
        {
            context.AddSource(model.HintName, Emitter.Emit(model));
        }
    }

    static ParseResult? Parse(INamedTypeSymbol? type,
                              TypeDeclarationSyntax? classSyntax,
                              Compilation compilation,
                              bool wholeClass,
                              CancellationToken cancellationToken)
    {
        if (type is null || classSyntax is null)
            return null;

        var diagnostics = new List<Diagnostic>();

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ClassMustBePartial, classSyntax.Identifier.GetLocation(), type.Name));
            return new ParseResult(null, diagnostics.ToImmutableArray());
        }

        if (type.DeclaringSyntaxReferences
                .Select(static x => x.GetSyntax())
                .OfType<RecordDeclarationSyntax>()
                .Any(static x => x.ParameterList is not null))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.PositionalRecordNotSupported, classSyntax.Identifier.GetLocation(), type.Name));
            return new ParseResult(null, diagnostics.ToImmutableArray());
        }

        var hierarchy = BuildHierarchy(type, classSyntax, diagnostics);
        if (hierarchy is null)
            return new ParseResult(null, diagnostics.ToImmutableArray());

        var properties = new List<SignalPropertyModel>();
        var computedProperties = new List<ComputedPropertyModel>();
        var asyncComputedProperties = new List<AsyncComputedPropertyModel>();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is IPropertySymbol property)
            {
                if (!wholeClass && !HasAttribute(property, Attributes.SignalAttributeName))
                    continue;

                var model = ParseProperty(property, diagnostics);
                if (model is not null)
                    properties.Add(model);

                continue;
            }

            if (member is not IMethodSymbol method)
                continue;

            if (HasAttribute(method, Attributes.ComputedAttributeName))
            {
                var model = ParseComputedMethod(method, diagnostics);
                if (model is not null)
                    computedProperties.Add(model);

                continue;
            }

            if (HasAttribute(method, Attributes.AsyncComputedAttributeName))
            {
                var model = ParseAsyncComputedMethod(method, compilation, diagnostics);
                if (model is not null)
                    asyncComputedProperties.Add(model);
            }
        }

        if (properties.Count == 0 && computedProperties.Count == 0 && asyncComputedProperties.Count == 0)
            return new ParseResult(null, diagnostics.ToImmutableArray());

        var userParameterlessConstructor = type.InstanceConstructors
                                               .FirstOrDefault(static x => x.Parameters.Length == 0
                                                                           && !x.IsImplicitlyDeclared
                                                                           && !x.IsStatic);

        if (userParameterlessConstructor is not null)
        {
            var location = userParameterlessConstructor.DeclaringSyntaxReferences
                                                       .Select(static x => x.GetSyntax())
                                                       .OfType<ConstructorDeclarationSyntax>()
                                                       .Select(static x => x.Identifier.GetLocation())
                                                       .FirstOrDefault()
                           ?? classSyntax.Identifier.GetLocation();

            diagnostics.Add(Diagnostic.Create(Diagnostics.UserParameterlessConstructorNotSupported, location, type.Name));
            return new ParseResult(null, diagnostics.ToImmutableArray());
        }

        var inpc = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        var alreadyImplementsInpc = inpc is not null
                                    && type.AllInterfaces.Any(x => SymbolEqualityComparer.Default.Equals(x, inpc));

        var notifyRequested = IsNotifyPropertyChangedRequested(type);

        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();

        var emitModelChanged = properties.Count > 0
                               && type.TypeKind != TypeKind.Struct
                               && !type.GetMembers(Emitter.ModelChangedPropertyName).Any();

        var systemTextJsonAvailable =
            compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIgnoreAttribute") is not null;

        var result = new SignalClassModel(ns,
                                          new EquatableArray<TypeDeclarationModel>([.. hierarchy]),
                                          new EquatableArray<SignalPropertyModel>([.. properties]),
                                          new EquatableArray<ComputedPropertyModel>([.. computedProperties]),
                                          new EquatableArray<AsyncComputedPropertyModel>([.. asyncComputedProperties]),
                                          notifyRequested,
                                          alreadyImplementsInpc,
                                          emitModelChanged,
                                          systemTextJsonAvailable,
                                          type.IsRecord,
                                          type.IsRecord && type.TypeKind == TypeKind.Struct,
                                          type.IsSealed,
                                          type.Name,
                                          BuildHintName(type));

        return new ParseResult(result, diagnostics.ToImmutableArray());
    }

    static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
                           .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                                                     | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    static SignalPropertyModel? ParseProperty(IPropertySymbol property, List<Diagnostic> diagnostics)
    {
        if (property.IsStatic || property.IsIndexer || property.IsAbstract || property.IsOverride)
            return null;

        if (HasAttribute(property, Attributes.SignalIgnoreAttributeName))
            return null;

        var syntax = property.DeclaringSyntaxReferences
                             .Select(static x => x.GetSyntax())
                             .OfType<PropertyDeclarationSyntax>()
                             .FirstOrDefault();

        if (syntax is null)
            return null;

        if (syntax.ExpressionBody is not null)
            return null;

        var accessors = syntax.AccessorList?.Accessors;
        if (accessors is null)
            return null;

        if (accessors.Value.Any(static x => x.Body is not null || x.ExpressionBody is not null))
            return null;

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.PropertyMustBePartial, syntax.Identifier.GetLocation(), property.Name));
            return null;
        }

        if (property.DeclaringSyntaxReferences.Length > 1)
            return null;

        if (property.GetMethod is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.PropertyMustHaveGetter, syntax.Identifier.GetLocation(), property.Name));
            return null;
        }

        if (property.Type.IsRefLikeType)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.RefLikePropertyNotSupported, syntax.Identifier.GetLocation(), property.Name));
            return null;
        }

        var setter = property.SetMethod;
        var isInitOnly = setter?.IsInitOnly == true;

        string? setterAccessibility = null;
        if (setter is not null && setter.DeclaredAccessibility != property.DeclaredAccessibility)
            setterAccessibility = AccessibilityToString(setter.DeclaredAccessibility);

        return new SignalPropertyModel(property.Name,
                                       property.Type.ToDisplayString(FullyQualifiedNullableFormat),
                                       $"_{Camelize(property.Name)}Signal",
                                       $"{property.Name}Signal",
                                       AccessibilityToString(property.DeclaredAccessibility),
                                       setterAccessibility,
                                       isInitOnly,
                                       setter is not null);
    }

    static ComputedPropertyModel? ParseComputedMethod(IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var syntax = method.DeclaringSyntaxReferences
                           .Select(static x => x.GetSyntax())
                           .OfType<MethodDeclarationSyntax>()
                           .FirstOrDefault();

        if (syntax is null)
            return null;

        if (method.Parameters.Length != 0)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustBeParameterless, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (method.ReturnsVoid)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustReturnValue, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (method.IsStatic)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustBeInstance, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (method.ReturnType.IsRefLikeType)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.RefLikePropertyNotSupported, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (!method.Name.StartsWith("Compute", StringComparison.Ordinal) || method.Name.Length == "Compute".Length)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustBePrefixed, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        var propertyName = method.Name.Substring("Compute".Length);

        if (method.ContainingType.GetMembers(propertyName).Any())
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedPropertyNameConflict,
                                              syntax.Identifier.GetLocation(),
                                              method.Name,
                                              propertyName));
            return null;
        }

        return new ComputedPropertyModel(propertyName,
                                         method.Name,
                                         method.ReturnType.ToDisplayString(FullyQualifiedNullableFormat),
                                         $"_{Camelize(propertyName)}Signal",
                                         $"{propertyName}Signal",
                                         method.DeclaredAccessibility == Accessibility.Private
                                             ? "public"
                                             : AccessibilityToString(method.DeclaredAccessibility));
    }

    static bool IsNotifyPropertyChangedRequested(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes()
                            .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == Attributes.GenerateNotifyPropertyChangedAttributeName);

        if (attribute is null)
            return false;

        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is bool enabled)
            return enabled;

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Enabled" && argument.Value.Value is bool namedEnabled)
                return namedEnabled;
        }

        return true;
    }

    static AsyncComputedPropertyModel? ParseAsyncComputedMethod(IMethodSymbol method, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var syntax = method.DeclaringSyntaxReferences
                           .Select(static x => x.GetSyntax())
                           .OfType<MethodDeclarationSyntax>()
                           .FirstOrDefault();

        if (syntax is null)
            return null;

        if (method.IsStatic)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustBeInstance, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        var cancellationToken = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (method.Parameters.Length != 1
            || cancellationToken is null
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, cancellationToken))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.AsyncComputedMethodSignature, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (method.ReturnType is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } returnType)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.AsyncComputedMethodSignature, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        var valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        var taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var definition = returnType.OriginalDefinition;

        var isValueTask = valueTaskOfT is not null && SymbolEqualityComparer.Default.Equals(definition, valueTaskOfT);
        var isTask = taskOfT is not null && SymbolEqualityComparer.Default.Equals(definition, taskOfT);

        if (!isValueTask && !isTask)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.AsyncComputedMethodSignature, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        var valueType = returnType.TypeArguments[0];
        if (valueType.IsRefLikeType)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.RefLikePropertyNotSupported, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        if (!method.Name.StartsWith("Compute", StringComparison.Ordinal) || method.Name.Length == "Compute".Length)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedMethodMustBePrefixed, syntax.Identifier.GetLocation(), method.Name));
            return null;
        }

        var propertyName = method.Name.Substring("Compute".Length);

        if (method.ContainingType.GetMembers(propertyName).Any())
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ComputedPropertyNameConflict,
                                              syntax.Identifier.GetLocation(),
                                              method.Name,
                                              propertyName));
            return null;
        }

        var attribute = method.GetAttributes()
                              .First(x => x.AttributeClass?.ToDisplayString() == Attributes.AsyncComputedAttributeName);

        var strategy = "global::SignalsDotnet.ConcurrentChangeStrategy.ScheduleNext";
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != "ConcurrentChangeStrategy" || argument.Value.Value is not int value)
                continue;

            strategy = value == 1
                ? "global::SignalsDotnet.ConcurrentChangeStrategy.CancelCurrent"
                : "global::SignalsDotnet.ConcurrentChangeStrategy.ScheduleNext";
        }

        return new AsyncComputedPropertyModel(propertyName,
                                              method.Name,
                                              valueType.ToDisplayString(FullyQualifiedNullableFormat),
                                              $"_{Camelize(propertyName)}Signal",
                                              $"{propertyName}Signal",
                                              $"Is{propertyName}Computing",
                                              method.DeclaredAccessibility == Accessibility.Private
                                                  ? "public"
                                                  : AccessibilityToString(method.DeclaredAccessibility),
                                              strategy,
                                              isTask);
    }

    static List<TypeDeclarationModel>? BuildHierarchy(INamedTypeSymbol type, TypeDeclarationSyntax syntax, List<Diagnostic> diagnostics)
    {
        var hierarchy = new List<TypeDeclarationModel>
        {
            new(GetKeyword(type), GetTypeName(type), GetConstraints(type))
        };

        var containing = type.ContainingType;
        while (containing is not null)
        {
            var containingSyntax = containing.DeclaringSyntaxReferences
                                             .Select(static x => x.GetSyntax())
                                             .OfType<TypeDeclarationSyntax>()
                                             .FirstOrDefault();

            if (containingSyntax is null || !containingSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ContainingTypeMustBePartial,
                                                  syntax.Identifier.GetLocation(),
                                                  type.Name,
                                                  containing.Name));
                return null;
            }

            hierarchy.Insert(0, new TypeDeclarationModel(GetKeyword(containing), GetTypeName(containing), GetConstraints(containing)));
            containing = containing.ContainingType;
        }

        return hierarchy;
    }

    static string GetKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Struct when type.IsRecord => "record struct",
        TypeKind.Struct => "struct",
        TypeKind.Interface => "interface",
        _ when type.IsRecord => "record",
        _ => "class"
    };

    static string GetTypeName(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
            return type.Name;

        return $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(static x => x.Name))}>";
    }

    static string GetConstraints(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
            return "";

        var builder = new System.Text.StringBuilder();
        foreach (var parameter in type.TypeParameters)
        {
            var constraints = new List<string>();

            if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");

            if (parameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint)
                constraints.Add("struct");

            if (parameter.HasNotNullConstraint)
                constraints.Add("notnull");

            foreach (var constraintType in parameter.ConstraintTypes)
            {
                constraints.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            if (parameter.HasConstructorConstraint)
                constraints.Add("new()");

            if (constraints.Count == 0)
                continue;

            builder.Append($" where {parameter.Name} : {string.Join(", ", constraints)}");
        }

        return builder.ToString();
    }

    static string BuildHintName(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                       .Replace("global::", "")
                       .Replace('<', '{')
                       .Replace('>', '}')
                       .Replace(", ", "_")
                       .Replace(' ', '_');

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return $"{name}.Signals.g.cs";
    }

    static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == metadataName);

    static string AccessibilityToString(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Private => "private",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => "public"
    };

    static string Camelize(string name)
    {
        if (name.Length == 0)
            return name;

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    sealed record ParseResult(SignalClassModel? Model, ImmutableArray<Diagnostic> Diagnostics);
}
