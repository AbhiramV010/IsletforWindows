namespace Islet.Hook;

/// <summary>
/// Every place the working name appears lives here (plus the assembly names in the
/// csproj files), so renaming the product is one find-and-replace. The app reads these too.
/// </summary>
public static class ProductInfo
{
    /// <summary>Product name shown in the UI and used for the log folder.</summary>
    public const string Name = "Islet";

    /// <summary>Named pipe the hook shim talks to.</summary>
    public const string PipeName = "islet-v1";

    /// <summary>Folder for both the app's and the shim's logs.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "logs");
}
