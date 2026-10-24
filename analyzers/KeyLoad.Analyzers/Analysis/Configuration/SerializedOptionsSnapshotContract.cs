using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class SerializedOptionsSnapshotContract
{
    internal static bool IsDataConstructor(Compilation compilation, IMethodSymbol constructor,
        CancellationToken cancellationToken)
    {
        var type = constructor.ContainingType;
        return type.IsRecord &&
            RuntimeHasAttribute(compilation, type, OrleansMetadataNames.GenerateSerializerAttribute) &&
            RuntimeHasAttribute(compilation, type, ConfigurationMetadataNames.OrleansAlias) &&
            type.DeclaringSyntaxReferences.All(reference =>
                reference.GetSyntax(cancellationToken) is RecordDeclarationSyntax { ParameterList: { } parameters } record &&
                parameters.Parameters.All(parameter => IsSerializedParameter(compilation, type, parameter)) &&
                record.Members.All(member => IsDataMember(compilation, member, cancellationToken)));
    }

    private static bool IsSerializedParameter(Compilation compilation, INamedTypeSymbol type, ParameterSyntax parameter) =>
        type.GetMembers(parameter.Identifier.ValueText).OfType<IPropertySymbol>()
            .Any(property => RuntimeHasAttribute(compilation, property, OrleansMetadataNames.IdAttribute));

    private static bool IsDataMember(Compilation compilation, MemberDeclarationSyntax member,
        CancellationToken cancellationToken) => member switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword),
            PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors } property =>
                property.Initializer is null && accessors.Accessors.All(static accessor =>
                    accessor.Body is null && accessor.ExpressionBody is null &&
                    accessor.Kind() is SyntaxKind.GetAccessorDeclaration or SyntaxKind.InitAccessorDeclaration) &&
                compilation.GetSemanticModel(member.SyntaxTree).GetDeclaredSymbol(property, cancellationToken) is { } symbol &&
                RuntimeHasAttribute(compilation, symbol, OrleansMetadataNames.IdAttribute),
            _ => false
        };

    private static bool RuntimeHasAttribute(Compilation compilation, ISymbol symbol, string metadataName)
    {
        var attribute = compilation.GetTypeByMetadataName(metadataName);
        return attribute?.ContainingAssembly.Name == ConfigurationMetadataNames.OrleansAssembly &&
            symbol.GetAttributes().Any(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute));
    }
}
