namespace Islet.App;

/// <summary>
/// Every place the working name appears lives here (plus the assembly names in the
/// csproj files), so renaming the product is one find-and-replace.
/// </summary>
public static class IsletInfo
{
    /// <summary>Product name shown in the UI.</summary>
    public const string Name = "Islet";

    /// <summary>Named pipe the hook shim talks to (used from milestone 3 on).</summary>
    public const string PipeName = "islet-v1";
}
