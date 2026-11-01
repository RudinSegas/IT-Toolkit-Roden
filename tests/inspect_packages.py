"""Post-package structure/hash audit. Does not execute Windows."""
from pathlib import Path
import hashlib,re,sys,zipfile
folder=Path(sys.argv[1])
for kind,top in [('portable','IT-Toolkit-Roden'),('source','IT-Toolkit-Roden-source')]:
    path=folder/f'IT-Toolkit-Roden-v1.0.0-{kind}.zip'
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None,path
        names={n for n in z.namelist() if not n.endswith('/')}
        for line in z.read(top+'/docs/SHA256SUMS.txt').decode().splitlines():
            digest,name=line.split('  ',1)
            assert hashlib.sha256(z.read(top+'/'+name)).hexdigest()==digest,name
        if kind=='portable':
            assert {n[len(top)+1:] for n in names if '/' not in n[len(top)+1:]}=={'IT-Toolkit-Roden.exe','IT-Toolkit-Roden.exe.config'}
            assert all(n.startswith(top+'/docs/') or n.startswith(top+'/assets/') or n in {top+'/IT-Toolkit-Roden.exe',top+'/IT-Toolkit-Roden.exe.config'} for n in names)
            assert z.getinfo(top+'/IT-Toolkit-Roden.exe.config').external_attr&2,'Hidden config attribute missing'
            assert not any(Path(n).suffix.lower() in {'.cs','.csproj','.ps1','.py','.sln','.dll'} for n in names)
            exe=z.read(top+'/IT-Toolkit-Roden.exe');assert z.read(top+'/docs/IT-Toolkit-Roden.exe.sha256').decode().split()[0]==hashlib.sha256(exe).hexdigest()
        else:
            for n in ['IT-Toolkit-Roden.sln','Build.ps1','src/Toolkit.App/MainWindow.xaml','src/Toolkit.Software/UninstallService.cs','tests/PublicReleaseTests.cs','assets/IT-Toolkit-Roden.ico','assets/IT-Toolkit-Roden-logo.svg','validation/Toolkit.Tests.exe','validation/host-test-output.txt']:
                assert top+'/'+n in names,n
        readme=top+('/docs/README.md' if kind=='portable' else '/README.md')
        text=z.read(readme).decode()
        import posixpath
        for ref in re.findall(r'\]\(([^)]+)\)|src="([^"]+)"',text):
            link=next(v for v in ref if v)
            if not link.startswith(('https:','http:','#')):
                assert posixpath.normpath(posixpath.join(posixpath.dirname(readme),link)) in names,link
        print(f'PASS: {path.name}: CRC, hashes, required files, layout, README links'+(', Hidden config and no developer runtime files' if kind=='portable' else ''))
        print('ZIP SHA-256: '+hashlib.sha256(path.read_bytes()).hexdigest())
