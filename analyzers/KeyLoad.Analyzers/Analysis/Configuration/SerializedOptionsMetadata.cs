using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class SerializedOptionsMetadata
{
    internal static bool IsDataParameter(Compilation compilation, IParameterSymbol parameter,
        CancellationToken cancellationToken) =>
        ConfigurationOwnership.IsOptionsType(compilation, parameter.Type) &&
        parameter.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is ParameterSyntax
            { Parent: ParameterListSyntax { Parent: RecordDeclarationSyntax } }) &&
        parameter.ContainingType.GetMembers(parameter.Name).OfType<IPropertySymbol>().Any(property =>
            SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type) &&
            IsDataProperty(compilation, property, cancellationToken));

    internal static bool IsDataProperty(Compilation compilation, IPropertySymbol property,
        CancellationToken cancellationToken) =>
        property.ContainingType.IsRecord && !property.IsStatic &&
        IsImmutableRecord(property.ContainingType) &&
        ConfigurationOwnership.IsOptionsType(compilation, property.Type) &&
        ConfigurationOwnership.IsSerializedOptionsSnapshot(compilation, property) &&
        property.GetMethod is not null && property.SetMethod is { IsInitOnly: true } &&
        property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) switch
        {
            ParameterSyntax { Parent: ParameterListSyntax { Parent: RecordDeclarationSyntax } } => true,
            PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors } =>
                accessors.Accessors.All(static accessor => accessor.Body is null && accessor.ExpressionBody is null &&
                    accessor.Kind() is SyntaxKind.GetAccessorDeclaration or SyntaxKind.InitAccessorDeclaration),
            _ => false
        });

    private static bool IsImmutableRecord(INamedTypeSymbol type)
    {
        for (var current = type; current is { IsRecord: true }; current = current.BaseType)
        {
            if (current.GetMembers().Any(static member => member switch
                {
                    IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, IsReadOnly: false } => true,
                    IPropertySymbol { IsStatic: false, SetMethod: { IsInitOnly: false } } => true,
                    _ => false
                }))
            {
                return false;
            }
        }

        return true;
    }
}
