# Third-party notices and provenance

## Direct source and model

- `python/rrdbnet_arch.py`: adapted from [BasicSR v1.4.2](https://github.com/XPixelGroup/BasicSR/blob/v1.4.2/basicsr/archs/rrdbnet_arch.py), Xintao Wang and contributors. Apache-2.0 text is retained in `BasicSR-LICENSE.txt`. Changes: standalone helper import and removal of architecture registry. `arch_util.py` supplies the small inference-relevant helpers. No BasicSR package is installed.
- `models/RealESRGAN_x4plus.pth`: [official v0.1.0 release asset](https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth). SHA256 is pinned in `models/SHA256SUMS`. The official repository license is BSD-3-Clause, copyright 2021 Xintao Wang; its complete text is retained in `Real-ESRGAN-LICENSE.txt`.
- Preserve original notices, license terms, modification notices and applicable disclaimers when distributing adapted source or binaries. Repository license evidence does not by itself replace a release-stage review of model-specific terms.

## Installed dependencies

`python-license-inventory.md` lists the exact installed packages. `docs/licenses/` contains their license/notice files copied from the installed distributions, including the notices shipped in NumPy and PyTorch wheels. Run `scripts/collect_licenses.py` with the project Python to regenerate the inventory after dependency changes.

The application uses .NET/WPF and CPython. Their runtimes are currently installed separately for development and are not included in a distribution artifact. Future packaging must retain the notices for the exact redistributed runtime binaries, CUDA/cuDNN libraries and image-codec dependencies. Final redistribution review and assembly of those runtime notices belong to the deferred packaging step.

No FastStone code, logo, icons or skin assets are included.
