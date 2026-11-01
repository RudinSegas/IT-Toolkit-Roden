"""Inspect the delivered Windows PE without executing Windows startup."""
import hashlib,json,struct,sys,xml.etree.ElementTree as ET
from pathlib import Path
p=Path(sys.argv[1]);data=p.read_bytes()
def u16(n):return struct.unpack_from('<H',data,n)[0]
def u32(n):return struct.unpack_from('<I',data,n)[0]
assert data[:2]==b'MZ'
pe=u32(0x3c);assert data[pe:pe+4]==b'PE\0\0'
assert u16(pe+4)==0x8664,'Not x64'
opt=pe+24;assert u16(opt)==0x20b,'Not PE32+'
assert u16(opt+68)==2,'Not Windows GUI subsystem'
assert u32(opt+112+14*8)!=0,'Missing CLR directory'
assert b'v4.0.30319' in data,'Unexpected CLR metadata version'
section_count=u16(pe+6);section_table=opt+u16(pe+20)
def offset(rva):
 for i in range(section_count):
  s=section_table+i*40;virtual=u32(s+12);extent=max(u32(s+8),u32(s+16))
  if virtual<=rva<virtual+extent:return u32(s+20)+rva-virtual
 raise AssertionError('Unmapped resource RVA')
base=offset(u32(opt+112+2*8))
def entries(relative):
 start=base+relative;count=u16(start+12)+u16(start+14)
 return [(u32(start+16+i*8),u32(start+20+i*8)) for i in range(count)]
manifest=next(value for name,value in entries(0) if name==24)
while manifest&0x80000000:manifest=entries(manifest&0x7fffffff)[0][1]
entry=base+manifest;a=offset(u32(entry));length=u32(entry+4)
root=ET.fromstring(data[a:a+length]);ns={'m':'urn:schemas-microsoft-com:asm.v3'}
execution=root.find('.//m:requestedExecutionLevel',ns)
assert execution is not None and execution.attrib=={'level':'asInvoker','uiAccess':'false'}
identity=root.find('{urn:schemas-microsoft-com:asm.v1}assemblyIdentity')
assert identity is not None and identity.attrib=={'version':'1.0.0.0','name':'IT-Toolkit-Roden.App'}
assert b'BSJB' in data,'Missing managed metadata'
assert p.name=='IT-Toolkit-Roden.exe','Incorrect public executable filename'
assert 'IT Toolkit by Roden Segas'.encode('utf-16le') in data,'Missing native product metadata'
assert '1.0.0.0'.encode('utf-16le') in data,'Missing native version metadata'
def payload(value):
 while value&0x80000000:value=entries(value&0x7fffffff)[0][1]
 location=base+value
 return data[offset(u32(location)):offset(u32(location))+u32(location+4)]
types=dict(entries(0))
assert 3 in types and 14 in types,'Missing embedded native icon/group icon'
group=payload(types[14]);assert struct.unpack_from('<H',group,4)[0]==7,'Expected seven native icon resolutions'
icon_sizes=[]
for i in range(7):
 w,h=group[6+i*14:8+i*14];icon_sizes.append((w or 256,h or 256))
assert set(icon_sizes)=={(n,n) for n in (16,24,32,48,64,128,256)}
result={'file':p.name,'bytes':len(data),'architecture':'x64','subsystem':'Windows GUI','runtime':'CLR v4 / .NET Framework 4.8','manifest':'asInvoker / uiAccess=false','sha256':hashlib.sha256(data).hexdigest(),'version':'1.0.0','icon_sizes':icon_sizes,'windows_execution':'NOT TESTED by this inspection'}
print(json.dumps(result,indent=2))
