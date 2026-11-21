namespace Wrangler.Analyzers;

internal static class HelpLinks
{
    public static string ForRule(string diagnosticId) =>
        $"https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/README.md#{diagnosticId.ToLowerInvariant()}";
}
