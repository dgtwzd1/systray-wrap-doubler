namespace SystrayWrapDoubler;

internal static class Cleanup
{
    public static void RemoveTempFiles()
    {
        var log = Path.Combine(Path.GetTempPath(), "SystrayWrapDoubler.Native.log");
        TryDeleteFile(log);

        if (Directory.Exists(PromotionStateStore.StateDirectory))
        {
            Directory.Delete(PromotionStateStore.StateDirectory, recursive: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
