from PIL import Image, ImageDraw
import os
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
shield = [(S * 0.5, S * 0.06), (S * 0.88, S * 0.2), (S * 0.86, S * 0.56), (S * 0.72, S * 0.8), (S * 0.5, S * 0.95),
          (S * 0.28, S * 0.8), (S * 0.14, S * 0.56), (S * 0.12, S * 0.2)]
d.polygon(shield, fill=(217, 119, 87, 255))
inner = [(x * 0.78 + S * 0.11, y * 0.78 + S * 0.1) for x, y in shield]
d.polygon(inner, fill=(15, 23, 42, 255))
w = int(S * 0.07)
d.line([(S * 0.33, S * 0.5), (S * 0.46, S * 0.63), (S * 0.69, S * 0.38)], fill=(255, 214, 190, 255), width=w, joint="curve")
here = os.path.dirname(os.path.abspath(__file__))
img.save(os.path.join(here, "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
