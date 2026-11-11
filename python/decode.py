"""One-shot preview decoder and persistent thumbnail decoder."""
import base64
import io
import json
import sys
from imaging import read_image


def thumbnail(path, size):
    image = read_image(path)
    dimensions = f'{image.width}x{image.height}'
    image.thumbnail((size, size))
    return image, dimensions


if __name__ == '__main__':
    if len(sys.argv) == 2 and sys.argv[1] == '--thumbnail-server':
        for line in sys.stdin:
            try:
                request = json.loads(line)
                image, dimensions = thumbnail(request['path'], 160)
                buffer = io.BytesIO()
                image.save(buffer, format='PNG')
                response = {'png': base64.b64encode(buffer.getvalue()).decode('ascii')}
            except Exception as error:
                response = {'error': str(error)}
            print(json.dumps(response, ensure_ascii=False), flush=True)
    else:
        try:
            image, dimensions = thumbnail(sys.argv[1], int(sys.argv[2]))
            print(dimensions, file=sys.stderr)
            image.save(sys.stdout.buffer, format='PNG')
        except Exception as error:
            print(str(error), file=sys.stderr)
            sys.exit(1)
