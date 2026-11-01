using System;
using System.IO;
using System.Text.RegularExpressions;
namespace Toolkit.Software
{
    public static class NotepadInstaller
    {
        public static InstallerPlan Parse(string json)
        {
            // The official MSI has the commercial signature without the EXE's legacy secondary signature.
            var plan=DirectInstaller.ParseRelease("Notepad++.Notepad++",json,"notepad-plus-plus/notepad-plus-plus",@"^npp\.[0-9.]+\.Installer\.x64\.msi$","NOTEPAD++",new[]{"/qn","/norestart","REBOOT=ReallySuppress"});
            Validate(plan);return plan;
        }
        public static void Validate(InstallerPlan plan)
        {
            if(plan==null||plan.Id!="Notepad++.Notepad++"||plan.Extension!=".msi"||plan.AllowUnsignedWithDigest||plan.Publisher!="NOTEPAD++")throw new InvalidDataException("Notepad++ requires its trusted-publisher x64 MSI profile.");
            if(!Regex.IsMatch(plan.Hash??"",@"\A[0-9a-fA-F]{64}\z"))throw new InvalidDataException("Official Notepad++ MSI SHA-256 is missing or invalid; refusing installation. No EXE/signature fallback performed.");
            if(!DirectInstaller.KnownVersion(plan.Version))throw new InvalidDataException("Official Notepad++ release version is unavailable.");
            var match=Regex.Match(plan.Url??"",@"\Ahttps://github\.com/notepad-plus-plus/notepad-plus-plus/releases/download/v([0-9.]+)/npp\.([0-9.]+)\.Installer\.x64\.msi\z");
            if(!match.Success||!DirectInstaller.ExactVersion(match.Groups[1].Value,plan.Version)||!DirectInstaller.ExactVersion(match.Groups[2].Value,plan.Version))throw new InvalidDataException("Notepad++ asset/tag/version identity mismatch.");
            if(plan.Arguments==null||String.Join(" ",plan.Arguments)!="/qn /norestart REBOOT=ReallySuppress")throw new InvalidDataException("Unexpected Notepad++ MSI arguments.");
        }
    }
}
