"""Audit every Git object; only explicit public Git identities and legal attribution are allowed."""
from pathlib import Path
import importlib.util,json,re,subprocess,sys
spec=importlib.util.spec_from_file_location('vsp_audit',Path(__file__).with_name('audit-public.py'))
audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)
def git(*args):
    return subprocess.check_output(['git','-c','core.hooksPath=/dev/null',*args])
identities={
    '195017834+Antivilod@users.noreply.github.com',
    '41898282+github-actions[bot]@users.noreply.github.com',
    'noreply@github.com',
    'contributors@example.invalid',
}
legal=set()
for line in git('rev-list','--objects','--all','--','licenses').decode().splitlines():
    parts=line.split(' ',1)
    if len(parts)==2 and parts[1].startswith('licenses/'):
        legal.add(parts[0])
findings=[];count=0
for line in git('cat-file','--batch-all-objects','--batch-check=%(objectname) %(objecttype)').decode().splitlines():
    oid,kind=line.split();count+=1
    data=git('cat-file',kind,oid)
    # Commit identity headers are public Git metadata, not user profile data.
    # Validate those exact headers, then scan the entire remaining message normally.
    if kind in {'commit','tag'}:
        text=data.decode('utf-8','replace');lines=[]
        for part in text.splitlines():
            if part.startswith(('author ','committer ','tagger ')):
                match=re.search(r'<([^<>]+)>',part)
                if not match or match.group(1) not in identities:
                    findings.append({'location':'git-object:'+oid,'category':'unapproved-git-identity'})
                continue
            lines.append(part)
        data='\n'.join(lines).encode()
    f,_=audit.inspect(data,'git-object:'+oid,[],oid in legal)
    findings.extend(f)
git('fsck','--full')
result={'objects_scanned':count,'findings':findings,'passed':not findings}
print(json.dumps(result))
sys.exit(1 if findings else 0)
