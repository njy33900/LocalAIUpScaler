"""One-shot preview decoder and persistent thumbnail decoder."""

import base64
import io
import json
import sys
from imaging import read_image


def thumbnail(path, size):
    """EXIF 방향과 포맷 정책을 적용하고 종횡비를 유지한 축소 이미지를 만든다.

    Args:
        path: 디코딩할 로컬 이미지 경로.
        size: 축소 이미지의 가로·세로 상한.

    Returns:
        Pillow 이미지와 축소 전 원본 치수 문자열의 튜플.
    """
    image = read_image(path)
    dimensions = f"{image.width}x{image.height}"
    image.thumbnail((size, size))

    return image, dimensions


if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--thumbnail-server":
        # 이미지별 오류는 응답으로 반환하고 다음 요청을 계속 받는다.
        # stdout은 C#이 읽는 JSONL 프로토콜 전용이다.
        for line in sys.stdin:
            try:
                request = json.loads(line)
                image, dimensions = thumbnail(request["path"], 160)
                buffer = io.BytesIO()
                image.save(buffer, format="PNG")
                response = {"png": base64.b64encode(buffer.getvalue()).decode("ascii")}
            except Exception as error:
                response = {"error": str(error)}

            print(json.dumps(response, ensure_ascii=False), flush=True)
    else:
        try:
            image, dimensions = thumbnail(sys.argv[1], int(sys.argv[2]))
            print(dimensions, file=sys.stderr)
            image.save(sys.stdout.buffer, format="PNG")
        except Exception as error:
            print(str(error), file=sys.stderr)
            sys.exit(1)
