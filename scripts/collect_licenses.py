from importlib import metadata
from pathlib import Path
import shutil
root=Path(__file__).resolve().parents[1]
dest=root/'docs/licenses';dest.mkdir(exist_ok=True)
rows=['# Installed Python dependency notices','', '| Package | Version | License metadata | Copied notice files |','|---|---|---|---|']
for line in (root/'python/requirements-lock.txt').read_text(encoding='utf-8-sig').splitlines():
    if '==' not in line: continue
    name,version=line.split('==');distribution=metadata.distribution(name);notices=[]
    for entry in distribution.files or []:
        base=Path(str(entry)).name.lower()
        if base.startswith(('license','copying','notice','copyright')):
            source=distribution.locate_file(entry)
            if source.is_file():
                target=dest/name/Path(str(entry));target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
                notices.append(str(target.relative_to(root/'docs')).replace('\\','/'))
    license=distribution.metadata.get('License-Expression') or distribution.metadata.get('License') or 'See copied license'
    license=license.splitlines()[0][:100].replace('|','/')
    rows.append(f'| {name} | {version} | {license} | {len(notices)} |')
(root/'docs/python-license-inventory.md').write_text('\n'.join(rows)+'\n',encoding='utf-8')
