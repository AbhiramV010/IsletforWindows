namespace Islet.App;

/// <summary>
/// The app's view of the product constants. The values live in
/// <see cref="Islet.Hook.ProductInfo"/>, shared with the shim, so a rename is still one edit.
/// </summary>
public static class IsletInfo
{
    /// <summary>Product name shown in the UI.</summary>
    public const string Name = Islet.Hook.ProductInfo.Name;

    /// <summary>Named pipe the hook shim talks to.</summary>
    public const string PipeName = Islet.Hook.ProductInfo.PipeName;

    /// <summary>Per-session mutex that keeps the app to a single instance.</summary>
    public const string SingleInstanceMutexName = @"Local\Islet.App.SingleInstance";
}
