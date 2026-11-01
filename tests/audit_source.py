"""Static source/resource regression audit; does not run WPF or Windows APIs."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
for p in list((root/'src').rglob('*.xaml'))+list(root.rglob('*.csproj'))+list((root/'src').rglob('*.config'))+list((root/'src').rglob('*.manifest')):
    ET.parse(p)
source='\n'.join(p.read_text() for p in (root/'src').rglob('*.cs'))
xaml=(root/'src/Toolkit.App/MainWindow.xaml').read_text()
properties=set(re.findall(r'public\s+(?:[\w<>?,.\[\]]+)\s+(\w+)\s*\{',source))
commands=re.findall(r'Command="\{Binding (?:DataContext\.)?(\w+)',xaml)
assert set(commands)<=properties, set(commands)-properties
assert 'File.WriteAllText(file+":Zone.Identifier"' not in source
assert 'CreateFile(full+":Zone.Identifier"' in source
assert 'summary.Contains("Installation failed")' not in source
assert 'gpupdate.exe' in source.lower() and 'UserActions.GroupPolicy(Environment.SystemDirectory)' in source
assert '"/boot"' not in source.lower() and '"/logoff"' not in source.lower()
assert 'RefreshPolicyEx' not in source
assert 'Progress, Mode=TwoWay' not in xaml
assert 'Progress, Mode=OneWay' in xaml
assert 'ReadFeatureWithoutElevation' in source and 'FeatureDecision(before)' in source
assert 'Installed — Update Status Unknown' in source
assert '3.1.2' not in source+xaml and 'Version = "1.0.0"' in source
assert 'CollectDiagnostic(false)' in source and 'CategoryCachePolicy.Fresh' in source
assert 'Search collected results' not in xaml
assert 'ServiceRows, Mode=OneWay' in xaml and 'EventRows, Mode=OneWay' in xaml
assert 'IT Toolkit by Roden Segas' in xaml
assert 'StandardInput.Write' not in source
print(f'PASS: XML, {len(commands)} command bindings, explicit progress, interactive fixed policy launch/no redirected input, typed installer results, MOTW native API, state-aware entry points and version (static only).')

# Immutable template fingerprints from supplied v3.1.1 for explicitly preserved pages.
import hashlib
expected_templates={'Dashboard': '982da41c57518b837e4ead3f559a2a86a6ffc791e601b9d6a93e7e9bd5ea80c7', 'System': 'd07b234dc46ac06904924a82ec7948a42067b8169c8a5cb4c881018a18371cec', 'Network': 'a5b29b133c8ce8d808370dfb3265bcc8043bf80a670b71b2e9fa2cf098e59918', 'Software': '0e2906deb2c6b9ecf73b04d0558dc41a906bea664b55ff6c9f01664dfe81139d', 'Reports': 'c90de4c8cca1711c13d11c798d46f3146cb96a01db68885f62273cc6746b768d'}
tree=ET.fromstring(xaml)
ns={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
key='{http://schemas.microsoft.com/winfx/2006/xaml}Key'
for template in tree.iter():
    if template.get(key)=='Dashboard':
        panel=template.find('w:StackPanel',ns)
        header=list(panel)[0]
        assert header.tag.endswith('DockPanel') and 'IT Toolkit by Roden Segas' in ET.tostring(header).decode()
        panel.text=(panel.text or "")+(header.tail or "")
        panel.remove(header)
resources={e.get(key):ET.tostring(e) for e in tree.iter() if e.get(key)}
for name, fingerprint in expected_templates.items():
    assert hashlib.sha256(resources[name]).hexdigest()==fingerprint, "Preserved page changed: "+name
assert 'UninstallPolicy.Create' in source and 'Win32_Product' not in source
assert 'Uninstall:' not in (root/'src/Toolkit.Core/Execution.cs').read_text()
assert 'HasUninstallCommand' in source and 'FileShare.None' in (root/'src/Toolkit.Software/UninstallService.cs').read_text()
assert 'NotepadInstaller.Parse' in source and 'REBOOT=ReallySuppress' in (root/'src/Toolkit.Software/NotepadInstaller.cs').read_text()
assert 'Process.Start(new ProcessStartInfo(NativeSecurity.ImagePath' in source
assert 'imagePath + ".config"' in (root/'src/Toolkit.Privilege/NativeSecurity.cs').read_text() or 'image + ".config"' in (root/'src/Toolkit.Privilege/NativeSecurity.cs').read_text()
print("PASS: preserved page fingerprints (Dashboard branding header excluded), selected-uninstall boundary, official Notepad++ MSI, dynamic helper image and adjacent config (static).")
