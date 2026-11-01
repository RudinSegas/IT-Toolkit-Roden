using System;
using System.Threading;
using System.Threading.Tasks;
using Toolkit.Core;
namespace Toolkit.Software
{
    // Compatibility adapters keep the existing VS Code shortcut on the shared installer engine.
    public static class VendorPolicy
    {
        public static bool IsAllowed(Uri uri) { return DirectPolicy.Allowed("Microsoft.VisualStudioCode",uri); }
    }
    public sealed class VendorDownload
    {
        public Task<string> InstallCodeAsync(Action<OutputChunk> output,CancellationToken token)
        { return new DirectInstaller().InstallAsync("Microsoft.VisualStudioCode",output,token); }
    }
}
