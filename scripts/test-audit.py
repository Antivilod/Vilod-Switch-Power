"""Offline scanner smoke tests, using generated dummy values only."""
from pathlib import Path
import json,subprocess,sys,tempfile
script=Path(__file__).with_name('audit-public.py')
with tempfile.TemporaryDirectory(prefix='vsp-audit-test-') as td:
 root=Path(td)/'fixture';root.mkdir()
 def scan():
  out=Path(td)/'result.json'
  p=subprocess.run([sys.executable,str(script),str(root),'--output',str(out)],capture_output=True)
  return p.returncode,json.loads(out.read_text(encoding='utf-8'))
 (root/'safe.txt').write_text('Only synthetic text.\n',encoding='utf-8')
 code,result=scan();assert code==0 and result['passed']
 (root/'sample.txt').write_text('C:'+'/'+'Users'+'/'+'SyntheticOwner'+'/'+'settings.txt',encoding='utf-8')
 code,result=scan();assert code==1 and any(f['category']=='user-home-path' for f in result['findings'])
 (root/'sample.txt').unlink()
 subprocess.run(['git','init','-q',str(root)],check=True)
 subprocess.run(['git','-C',str(root),'config','user.name','Public contributors'],check=True)
 subprocess.run(['git','-C',str(root),'config','user.email','contributors@example.invalid'],check=True)
 subprocess.run(['git','-C',str(root),'add','safe.txt'],check=True)
 subprocess.run(['git','-C',str(root),'-c','commit.gpgsign=false','commit','-qm','Synthetic fixture'],check=True)
 # An unreachable object must still be examined, even though it is not in HEAD.
 fake=('gh'+'p_'+'x'*40).encode()
 subprocess.run(['git','-C',str(root),'hash-object','-w','--stdin'],input=fake,check=True,capture_output=True)
 code,result=scan();assert code==1 and any(f['category']=='access-token' and f['location'].startswith('git-object:') for f in result['findings'])
print('PASS: safe fixture, user path, unreachable Git object (3 checks)')
