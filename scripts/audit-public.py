#!/usr/bin/env python3
"""Heuristic local publication audit. Never sends files or prints matched values."""
import argparse,hashlib,json,math,os,re,subprocess,sys,struct
from pathlib import Path
SKIP={'.git','bin','obj','artifacts','.vs','__pycache__','node_modules'}
FORBIDDEN={'.exe','.dll','.pdb','.zip','.7z','.pfx','.p12','.key','.pem','.snk','.log','.etl','.dmp','.bak','.tmp'}
PATTERNS={
 'user-home-path':r'[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s"<>]+|/(?:home|Users)/[^/\s"<>]+',
 'full-windows-sid':r'S-1-5-21-\d+-\d+-\d+(?:-\d+)?',
 'private-key':r'-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----',
 'access-token':r'gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|sk-(?:proj-)?[A-Za-z0-9_-]{24,}',
 'credential-url':r'https?://[^\s/:]+:[^\s/@]+@',
 'secret-assignment':r'''(?:api[_-]?key|password|client[_-]?secret|access[_-]?token)\s*[=:]\s*["'][^"'\s]{8,}["']''',
 'webhook-or-jwt':r'https://hooks[.]slack[.]com/services/[^\s"<>]+|https://(?:discord[.]com|discordapp[.]com)/api/webhooks/[^\s"<>]+|eyJ[A-Za-z0-9_-]{12,}[.]eyJ[A-Za-z0-9_-]{12,}[.][A-Za-z0-9_-]{12,}',
 'bearer-credential':r'Bearer[ \t]+[A-Za-z0-9_.-]{24,}',
 'email':r'[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}',
}
REGEX={k:re.compile(v,re.I if k not in {'access-token'} else 0) for k,v in PATTERNS.items()}
def run_git(root,*args):
 p=subprocess.run(['git','-c','core.hooksPath=/dev/null','-C',str(root),*args],capture_output=True)
 if p.returncode:raise RuntimeError('Git audit command failed: '+args[0])
 return p.stdout

def inspect(data,location,denylist,allow_legal=False):
 texts=[data.decode('utf-8','ignore')]
 if b'\0' in data:texts.extend([data.decode('utf-16-le','ignore'),data.decode('utf-16-be','ignore')])
 findings=[];reviewed=[]
 for kind,rx in REGEX.items():
  if any(rx.search(t) for t in texts):
   item={'location':location,'category':kind}
   if kind=='email' and allow_legal:reviewed.append(item|{'disposition':'upstream public attribution retained'})
   elif kind=='email' and all(not rx.search(t) or set(rx.findall(t))<= {'contributors@example.invalid'} for t in texts):reviewed.append(item|{'disposition':'reserved public Git identity'})
   else:findings.append(item)
 for name in denylist:
  if len(name)>2 and any(re.search(r'(?<![\w])'+re.escape(name)+r'(?![\w])',t,re.I) for t in texts):findings.append({'location':location,'category':'local-identity'})
 # Long, quoted, high-entropy values warrant manual review. Never output their contents.
 for t in texts:
  for value in re.findall(r'''["']([A-Za-z0-9_+/=-]{36,})["']''',t):
   if re.fullmatch(r'[0-9a-fA-F-]+',value):continue
   entropy=-sum((value.count(c)/len(value))*math.log2(value.count(c)/len(value)) for c in set(value))
   if entropy>4.5 and re.search(r'\d',value) and re.search('[a-z]',value) and re.search('[A-Z]',value):
    findings.append({'location':location,'category':'high-entropy-review'});break
 return findings,reviewed

def main():
 ap=argparse.ArgumentParser();ap.add_argument('root',nargs='?',default='.');ap.add_argument('--output');ap.add_argument('--denylist-file',help='Private local list, one exact identity per line; never include it in a repository')
 args=ap.parse_args();root=Path(args.root).resolve();deny=[os.environ.get('USERNAME','')]
 if args.denylist_file:deny+=Path(args.denylist_file).read_text(encoding='utf-8-sig').splitlines()
 deny=[x.strip() for x in deny if x.strip()];findings=[];reviewed=[];files=0;binary=[];meta=[];skipped=[]
 for cur,dirs,names in os.walk(root,followlinks=False):
  cp=Path(cur)
  for d in list(dirs):
   p=cp/d;rel=p.relative_to(root).as_posix()
   if p.is_symlink() or (hasattr(p,'is_junction') and p.is_junction()):findings.append({'location':rel,'category':'link-or-junction'});dirs.remove(d)
   elif d in SKIP:skipped.append(rel);dirs.remove(d)
  for name in names:
   p=cp/name;rel=p.relative_to(root).as_posix()
   if p.is_symlink():findings.append({'location':rel,'category':'symlink'});continue
   if args.output and p==Path(args.output).resolve():continue
   files+=1;data=p.read_bytes()
   if p.suffix.lower() in FORBIDDEN or name.startswith('.env') or name.lower() in ['profiles.json','monitoring.json','energy-settings.json','journal.json']:
    findings.append({'location':rel,'category':'excluded-runtime-or-secret-file'})
   f,v=inspect(data,rel,deny,rel.startswith('licenses/'));findings+=f;reviewed+=v
   if p.suffix.lower() in ['.png','.ico']:
    binary.append(rel)
    # PNG text / EXIF metadata may be embedded directly or in ICO frames.
    start=0;chunks=[]
    while (start:=data.find(b'\x89PNG\r\n\x1a\n',start))!=-1:
     pos=start+8
     while pos+12<=len(data):
      n=int.from_bytes(data[pos:pos+4],'big');kind=data[pos+4:pos+8];chunks.append(kind.decode('ascii','replace'))
      if kind in [b'tEXt',b'zTXt',b'iTXt',b'eXIf']:findings.append({'location':rel,'category':'image-metadata-review'})
      pos+=n+12
      if kind==b'IEND':break
     start+=8
    meta.append({'path':rel,'pngChunks':sorted(set(chunks)),'textExifFound':any(x in chunks for x in ['tEXt','zTXt','iTXt','eXIf'])})
   elif b'\0' in data:findings.append({'location':rel,'category':'unexpected-binary'})
 history={'present':(root/'.git').exists(),'objectsScanned':0,'commitCount':0,'refCount':0,'remotesPresent':False,'fsckPassed':None}
 if history['present']:
  # Every object, including unreachable and reflog-only objects. No hooks or remote access.
  listing=run_git(root,'cat-file','--batch-all-objects','--batch-check=%(objectname) %(objecttype)').decode().splitlines()
  allowed=set()
  for line in run_git(root,'ls-tree','-r','HEAD').decode().splitlines():
   fields=line.split(None,3)
   if len(fields)==4 and fields[3].startswith('licenses/'):allowed.add(fields[2])
  for line in listing:
   oid,kind=line.split();data=run_git(root,'cat-file',kind,oid);history['objectsScanned']+=1
   f,v=inspect(data,'git-object:'+oid,deny,oid in allowed);findings+=f;reviewed+=v
   if kind=='commit':history['commitCount']+=1
  history['refCount']=len(run_git(root,'for-each-ref','--format=%(refname)').splitlines());history['remotesPresent']=bool(run_git(root,'remote').strip())
  if history['remotesPresent']:findings.append({'location':'.git/config','category':'remote-review-required'})
  # Local metadata, author/committer, tags, reflogs, config and index without content output.
  gd=Path(run_git(root,'rev-parse','--absolute-git-dir').decode().strip())
  for p in gd.rglob('*'):
   if p.is_file() and not any(x in {'objects'} for x in p.relative_to(gd).parts):
    f,v=inspect(p.read_bytes(),'.git/'+p.relative_to(gd).as_posix(),deny);findings+=f;reviewed+=v
  run_git(root,'fsck','--full');history['fsckPassed']=True
 result={'schema':1,'filesScanned':files,'findings':findings,'reviewedAttribution':reviewed,'binaryAssets':binary,'imageMetadata':meta,'excludedGeneratedDirectories':skipped,'gitHistory':history,'passed':not findings,'limits':'Heuristics cannot prove absence of all personal data or secrets. Upstream public attribution is retained. No external service queried.'}
 text=json.dumps(result,ensure_ascii=False,indent=2)
 if args.output:Path(args.output).write_text(text+'\n',encoding='utf-8')
 print(json.dumps({'files':files,'findings':len(findings),'reviewedAttribution':len(reviewed),'gitObjects':history['objectsScanned'],'passed':not findings}))
 return 0 if not findings else 1
if __name__=='__main__':sys.exit(main())

