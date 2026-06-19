using System.Reflection;

namespace SystrayWrapDoubler.Installer;

internal static class PayloadExtractor
{
    private const string PayloadPrefix = "Payload/";

    public static void ExtractTo(string installPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("The installer payload is empty. Rebuild the release package.");
        }

        foreach (var resourceName in resourceNames)
        {
            var relativePath = resourceName[PayloadPrefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(installPath, relativePath));
            if (!destinationPath.StartsWith(Path.GetFullPath(installPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to extract outside the install folder: {relativePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var input = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing installer payload resource: {resourceName}");
            using var output = File.Create(destinationPath);
            input.CopyTo(output);
        }
    }
}
