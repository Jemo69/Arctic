from PIL import Image, ImageDraw
from pathlib import Path
out=Path(__file__).resolve().parent
im=Image.new('RGB',(600,400),'magenta'); d=ImageDraw.Draw(im)
for box,c in [((100,0,299,199),'red'),((300,0,499,199),(0,255,0)),((100,200,299,399),'blue'),((300,200,499,399),'yellow')]:d.rectangle(box,fill=c)
for orientation in range(1,9):
 exif=Image.Exif();exif[274]=orientation;exif[270]='private fixture metadata';exif[34853]={1:'N',2:(38.0,0.0,0.0)}
 im.save(out/f'orientation-{orientation}.jpg',quality=95,exif=exif)
im.save(out/'landscape.png');im.save(out/'landscape.webp',quality=95,exif=exif)
transparent=Image.new('RGBA',(300,300),(0,0,0,0));ImageDraw.Draw(transparent).rectangle((75,75,224,224),fill=(255,0,0,128));transparent.save(out/'transparent.png')
first=Image.new('RGB',(300,300),'red');second=Image.new('RGB',(300,300),'blue')
first.save(out/'animated.webp',save_all=True,append_images=[second],duration=100,loop=0,lossless=True)
