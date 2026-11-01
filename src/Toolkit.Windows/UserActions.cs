using System;
using System.Diagnostics;
using System.IO;
namespace Toolkit.Windows
{
    public static class UserActions
    {
        public static ProcessStartInfo GroupPolicy(string systemDirectory)
        {return new ProcessStartInfo{FileName=Path.Combine(systemDirectory,"cmd.exe"),Arguments="/d /k \"\""+Path.Combine(systemDirectory,"gpupdate.exe")+"\" /force\"",UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal,CreateNoWindow=false,WorkingDirectory=systemDirectory};}
        public static ProcessStartInfo Reveal(string file)
        {return new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Arguments="/select,"+Toolkit.Core.Arguments.Quote(Path.GetFullPath(file)),UseShellExecute=true};}
    }
}
