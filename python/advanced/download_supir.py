"""Only the Q/F files from the Drive folder linked in the pinned official README."""
import hashlib
import json
from pathlib import Path
import gdown
from catalog import ROOT

IDS={'SUPIR-v0Q.ckpt':'1ohCIBV_RAej1zuiidHph5qXNuD4GRxO3',
     'SUPIR-v0F.ckpt':'1aTLhTLUKK8nkNAotB7kZgfKc8SGA_TL2'}
folder='https://drive.google.com/drive/folders/1yELzm5SvAi9e7kPcO_jPp2XkTs4vK6aR'
files=gdown.download_folder(folder,skip_download=True,quiet=True,remaining_ok=True)
found={f.path:f.id for f in files}
dest=ROOT/'models/advanced/supir';dest.mkdir(parents=True,exist_ok=True)
receipt_path=dest/'download-receipts.json'
results=json.loads(receipt_path.read_text(encoding='utf-8')) if receipt_path.exists() else {}
for name, expected_id in IDS.items():
    if found.get(name)!=expected_id:
        raise ValueError('Official folder inventory changed: '+name)
    path=dest/name
    if not path.exists():
        result=gdown.download(id=expected_id,output=str(path),quiet=False,resume=True)
        if result is None:
            raise RuntimeError('Official Google Drive download failed: '+name)
    if path.stat().st_size<100000000:
        raise ValueError('Checkpoint unexpectedly small: '+name)
    with path.open('rb') as f:
        sha=hashlib.file_digest(f,'sha256').hexdigest()
    lock=ROOT/'config/advanced-models.lock.json'
    if lock.exists():
        locked=json.loads(lock.read_text(encoding='utf-8'))
        engine='supir_v0q' if name=='SUPIR-v0Q.ckpt' else 'supir_v0f'
        expected=locked[engine]['assets']['checkpoint']['sha256']
        if sha!=expected: raise ValueError('Pinned SHA256 mismatch: '+name)
    results[name]=dict(path=str(path.relative_to(ROOT)),filename=name,sha256=sha,
        size=path.stat().st_size,url='https://drive.google.com/uc?id='+expected_id,
        hash_source='locally computed from official Drive download; publisher hash not provided')
    (dest/'download-receipts.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
    print('VERIFIED',name,sha,flush=True)
