using Microsoft.CodeAnalysis;

namespace SignalsDotnet.SourceGenerators;

static class Diagnostics
{
    public static readonly DiagnosticDescriptor ClassMustBePartial = new(
        "SIG001",
        "Class marked with [GenerateSignals] must be partial",
        "Class '{0}' is marked with [GenerateSignals] but is not declared 'partial'",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        "SIG002",
        "Containing type must be partial",
        "Class '{0}' is marked with [GenerateSignals] but its containing type '{1}' is not declared 'partial'",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor PropertyMustBePartial = new(
        "SIG003",
        "Auto property must be partial to be backed by a signal",
        "Property '{0}' must be declared 'partial' so that its accessors can be redirected to a signal. Add the 'partial' modifier, or mark it with [SignalIgnore] to exclude it.",
        "SignalsDotnet",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor PropertyMustHaveGetter = new(
        "SIG004",
        "Signal backed property must have a getter",
        "Property '{0}' must declare a getter to be backed by a signal",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ComputedMethodMustBeParameterless = new(
        "SIG006",
        "Computed method must be parameterless",
        "Method '{0}' is marked with [Computed] but declares parameters",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ComputedMethodMustReturnValue = new(
        "SIG007",
        "Computed method must return a value",
        "Method '{0}' is marked with [Computed] but returns void",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ComputedMethodMustBeInstance = new(
        "SIG008",
        "Computed method must be an instance method",
        "Method '{0}' is marked with [Computed] but is static",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor AsyncComputedMethodSignature = new(
        "SIG012",
        "Async computed method has an unsupported signature",
        "Method '{0}' is marked with [AsyncComputed] so it must take a single CancellationToken parameter and return ValueTask<T> or Task<T>",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ComputedMethodMustBePrefixed = new(
        "SIG010",
        "Computed method name must start with Compute",
        "Method '{0}' is marked with [Computed] so its name must start with 'Compute' followed by the name of the property to generate. For example 'ComputeFullName' generates 'FullName'.",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ComputedPropertyNameConflict = new(
        "SIG009",
        "Computed property name conflicts with an existing member",
        "Method '{0}' would generate a property named '{1}' but that member already exists. Rename the method to 'Compute{1}' or rename the conflicting member.",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor PositionalRecordNotSupported = new(
        "SIG011",
        "Positional records are not supported",
        "Record '{0}' declares a primary constructor. Its positional parameters generate properties and a constructor that conflict with the generated ones. Declare the record without a primary constructor and use partial properties instead.",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RefLikePropertyNotSupported = new(
        "SIG005",
        "Ref like types are not supported",
        "Property '{0}' has a ref struct type and cannot be backed by a signal",
        "SignalsDotnet",
        DiagnosticSeverity.Error,
        true);
}
