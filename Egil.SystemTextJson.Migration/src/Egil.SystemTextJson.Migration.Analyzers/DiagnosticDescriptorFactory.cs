using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers;

internal static class DiagnosticDescriptorFactory
{
    private const string Category = "Egil.SystemTextJson.Migration";
    private const string HelpLink = "https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/error-diagnostics.md";

    public static DiagnosticDescriptor Create(string id, string title, string messageFormat, DiagnosticSeverity defaultSeverity)
    {
        return new DiagnosticDescriptor(id, title, messageFormat, Category, defaultSeverity, isEnabledByDefault: true,
            helpLinkUri: HelpLink);
    }
}
