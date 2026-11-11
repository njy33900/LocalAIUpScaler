"""Verify the exact installed dependency set and model without network access."""
import hashlib
import importlib.metadata
import json
import platform
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[1]
errors=[]
if sys.version_info[:2] != (3,11): errors.append('Python 3.11 is required')
for line in (ROOT/'python/requirements-lock.txt').read_text(encoding='utf-8-sig').splitlines():
    if not line.strip() or line.startswith('#'): continue
    name,version=line.split('==')
    try:
        actual=importlib.metadata.version(name)
        if actual != version: errors.append(f'{name}: expected {version}, found {actual}')
    except importlib.metadata.PackageNotFoundError: errors.append(f'{name}: missing')
model=ROOT/'models/RealESRGAN_x4plus.pth'
expected=(ROOT/'models/SHA256SUMS').read_text(encoding='utf-8-sig').split()[0]
if not model.exists() or hashlib.sha256(model.read_bytes()).hexdigest()!=expected: errors.append('Model missing or checksum mismatch')
if errors:
    print('\n'.join(errors));sys.exit(1)
import torch
from PIL import features
if not features.check('webp') or not features.check('libtiff'):
    print('WebP and TIFF codecs are required');sys.exit(1)
print(json.dumps(dict(python=platform.python_version(),torch=torch.__version__,cuda=torch.cuda.is_available(),model_sha256=expected),indent=2))
