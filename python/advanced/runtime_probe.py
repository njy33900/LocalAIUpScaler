"""Package metadata probe: no model load, CUDA allocation or network activity."""
import importlib.metadata
import json
import sys

versions={'torch':'2.2.2+cu121','torchvision':'0.17.2+cu121','xformers':'0.0.25.post1',
          'numpy':'1.26.4','omegaconf':'2.3.0'}
if sys.argv[-1]=='supir':
    versions.update({'transformers':'4.28.1','open-clip-torch':'2.17.1','k-diffusion':'0.1.1.post1'})
else:
    versions.update({'transformers':'4.37.2','accelerate':'0.28.0'})
missing=[];incompatible=[]
for name,expected in versions.items():
    try:
        actual=importlib.metadata.version(name)
        if actual!=expected: incompatible.append(name+': '+actual+' != '+expected)
    except importlib.metadata.PackageNotFoundError:
        missing.append(name)
print(json.dumps(dict(missing=missing,incompatible=incompatible)))
