using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Toolkit.Software;
namespace Toolkit.Tests
{
    public static partial class Suite
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        private static void Run311WindowsFiles()
        {
            string directory=Path.Combine(Path.GetTempPath(),"Toolkit paths כפר test #& "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string file=Path.Combine(directory,"installer שלום & test.exe");
            try
            {
                File.WriteAllText(file,"Harmless fixture — never executed");InternetZone.Mark(file);
                using(var handle=CreateFile(file+":Zone.Identifier",0x80000000,1,IntPtr.Zero,3,0x80,IntPtr.Zero))
                {Check(!handle.IsInvalid,"MOTW stream opened through native Unicode API");using(var stream=new FileStream(handle,FileAccess.Read))using(var reader=new StreamReader(stream))Check(reader.ReadToEnd().Contains("ZoneId=3"),"MOTW persisted for spaces/Unicode/special characters");}
                Throws(()=>Signature.VerifyPublisher(file,"Mozilla Corporation",false),"unsigned fixture blocked before execution");
                File.Delete(file);Check(!File.Exists(file),"installer and alternate streams cleaned with parent file");
                using(var handle=CreateFile(file+":Zone.Identifier",0x80000000,1,IntPtr.Zero,3,0x80,IntPtr.Zero))Check(handle.IsInvalid,"MOTW stream removed with installer");
            }finally{if(File.Exists(file))File.Delete(file);Directory.Delete(directory,false);}
        }
    }
}
