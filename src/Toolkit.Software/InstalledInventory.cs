using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Toolkit.Software
{
    public sealed class SoftwareSnapshot
    {
        public bool Complete=true;
        public List<InstalledApplication> Items=new List<InstalledApplication>();
        public List<string> Errors=new List<string>();
    }
    public static class InstalledInventory
    {
        public static SoftwareSnapshot Read()
        {
            var result=new SoftwareSnapshot();
            foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            try
            {
                using(var root=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if(uninstall==null)continue;
                    foreach(string child in uninstall.GetSubKeyNames())try{using(var key=uninstall.OpenSubKey(child)){if(key==null)continue;string name=Convert.ToString(key.GetValue("DisplayName"));if(String.IsNullOrWhiteSpace(name))continue;result.Items.Add(UninstallService.Describe(key,child,hive==RegistryHive.LocalMachine?"machine":"user",view==RegistryView.Registry64?64:32));}}
                    catch(Exception e){result.Complete=false;result.Errors.Add("Registry item unavailable: "+e.GetType().Name);}
                }
            }catch(Exception e){result.Complete=false;result.Errors.Add(hive+" "+view+": "+e.Message);}
            result.Items=result.Items.GroupBy(x=>x.Scope+"|"+x.RegistryView+"|"+x.Source+"|"+x.Version).Select(g=>g.First()).OrderBy(x=>x.Name).ToList();return result;
        }
        public static string Csv(IEnumerable<InstalledApplication> items)
        {return "Name,Version,Publisher,InstallDate,Scope,Architecture\r\n"+String.Join("\r\n",items.Select(x=>String.Join(",",new[]{x.Name,x.Version,x.Publisher,x.InstallDate,x.Scope,x.Architecture}.Select(Cell))));}
        public static string Cell(string value){value=value??"";if(value.Length>0&&"=+-@\t\r".Contains(value[0]))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    }
}
