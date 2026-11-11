"""Shared image decoding policy for preview and inference."""
from pathlib import Path
import warnings
import numpy as np
from PIL import Image, ImageOps

warnings.filterwarnings('error', category=Image.DecompressionBombWarning)

FORMATS = {'.png': 'PNG', '.jpg': 'JPEG', '.jpeg': 'JPEG', '.bmp': 'BMP',
           '.tif': 'TIFF', '.tiff': 'TIFF', '.webp': 'WEBP'}

def read_image(path):
    with Image.open(path) as source:
        if source.format != FORMATS.get(Path(path).suffix.lower()):
            raise ValueError('확장자와 실제 이미지 형식이 일치하지 않습니다.')
        source.seek(0)
        image = ImageOps.exif_transpose(source)
        # Explicit conversion rather than Pillow's clipping conversion for 16-bit grayscale.
        if image.mode.startswith('I;16') or image.mode == 'I':
            array = np.asarray(image).astype(np.float32)
            image = Image.fromarray(np.rint(np.clip(array, 0, 65535) / 257).astype(np.uint8))
        elif image.mode == 'F':
            raise ValueError('부동소수점 TIFF는 지원하지 않습니다.')
        alpha = 'A' in image.getbands() or 'transparency' in source.info
        return image.convert('RGBA' if alpha else 'RGB')
