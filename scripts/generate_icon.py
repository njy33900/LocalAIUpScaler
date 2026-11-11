"""Generate the original code-defined app icon, SVG source, PNG and Windows ICO."""
from pathlib import Path
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'src/Upscaler.Wpf/Assets'
OUT.mkdir(exist_ok=True)
SVG='''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
<rect x="1" y="1" width="62" height="62" rx="15" fill="#192330" stroke="#354D68" stroke-width="1.5"/>
<path d="M33 15H15Q10 15 10 20V48Q10 53 15 53H44Q49 53 49 48V38" fill="none" stroke="#76B8FF" stroke-width="4" stroke-linecap="round"/>
<path d="M16 44L24 35L31 42L37 36L43 44" fill="none" stroke="#76B8FF" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="21" cy="26" r="3" fill="#A7D9FF"/>
<path d="M32 32L53 11M39 11H53V25" fill="none" stroke="#A7D9FF" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>
</svg>'''
(OUT/'App.svg').write_text(SVG+'\n',encoding='utf-8')
s=8
image=Image.new('RGBA',(64*s,64*s),(0,0,0,0));d=ImageDraw.Draw(image)
def line(points,fill,width):
    points=[(x*s,y*s) for x,y in points];d.line(points,fill=fill,width=round(width*s),joint='curve')
    r=width*s/2
    for x,y in (points[0],points[-1]):d.ellipse((x-r,y-r,x+r,y+r),fill=fill)
d.rounded_rectangle((s,s,63*s,63*s),radius=15*s,fill='#192330',outline='#354D68',width=round(1.5*s))
# Rounded frame sampled from the same simple vector geometry.
line([(33,15),(15,15),(12,16),(10,20),(10,48),(12,52),(15,53),(44,53),(48,51),(49,48),(49,38)],'#76B8FF',4)
line([(16,44),(24,35),(31,42),(37,36),(43,44)],'#76B8FF',3)
d.ellipse((18*s,23*s,24*s,29*s),fill='#A7D9FF')
line([(32,32),(53,11)],'#A7D9FF',5);line([(39,11),(53,11),(53,25)],'#A7D9FF',5)
image=image.resize((256,256),Image.Resampling.LANCZOS)
image.save(OUT/'App.png')
image.save(OUT/'App.ico',sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])
print('Generated original SVG, 256px PNG and multi-resolution ICO.')
