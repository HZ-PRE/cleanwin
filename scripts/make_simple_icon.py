"""Generate the current flat broom logo for the sidebar, executable and installer."""
from pathlib import Path
from PIL import Image, ImageDraw
import io
import struct


def make_icon(size):
    s = size * 4
    image = Image.new('RGBA', (s, s))
    draw = ImageDraw.Draw(image)
    def box(values):
        return tuple(round(v * s) for v in values)
    draw.rounded_rectangle(box((.03, .03, .97, .97)), radius=round(s * .22), fill='#3575e8')
    # Two broad shapes remain legible at 16 px; no gradients, lettering or fine details.
    draw.line([box((.64, .27)), box((.47, .49))], fill='white', width=round(s * .105))
    draw.polygon([box(p) for p in ((.39, .43), (.61, .59), (.48, .77), (.23, .58))], fill='white')
    return image.resize((size, size), Image.Resampling.LANCZOS)


def main():
    assets = Path(__file__).resolve().parents[1] / 'src/CleanOptimize.App/Assets'
    make_icon(256).save(assets / 'logo.png', optimize=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = []
    for size in sizes:
        data = io.BytesIO()
        make_icon(size).save(data, 'PNG', optimize=True)
        frames.append(data.getvalue())
    with (assets / 'app.ico').open('wb') as output:
        output.write(struct.pack('<HHH', 0, 1, len(sizes)))
        offset = 6 + 16 * len(sizes)
        for size, data in zip(sizes, frames):
            output.write(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
            offset += len(data)
        for data in frames:
            output.write(data)
    print('Generated logo.png and app.ico')


if __name__ == '__main__':
    main()
