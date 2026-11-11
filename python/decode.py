import sys
from imaging import read_image
try:
    image = read_image(sys.argv[1])
    print(f'{image.width}x{image.height}', file=sys.stderr)
    image.thumbnail((int(sys.argv[2]), int(sys.argv[2])))
    image.save(sys.stdout.buffer, format='PNG')
except Exception as error:
    print(str(error), file=sys.stderr)
    sys.exit(1)
